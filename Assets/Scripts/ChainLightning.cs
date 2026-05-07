using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Chain Lightning spell.
///
/// The bolt spawns at the caster's fire point, travels to the primary target over
/// travelTime seconds, deals damage, then waits jumpDelay seconds before arcing to
/// the next nearest enemy — repeating chainCount times total.
///
/// All timing and chain values are set by SpellCaster from SpellData before the
/// object is activated, so configure them on the SpellData asset.
///
/// Multiplayer: damage is server-authoritative. Hit effects are broadcast to all
/// clients via ClientRpc so every player sees the visual on every jump.
/// Singleplayer: runs in Start() with no networking.
/// </summary>
public class ChainLightning : NetworkBehaviour
{
    // ─── Set by SpellCaster (configured via SpellData) ────────────────────────

    [HideInInspector] public float      precomputedDamage;
    [HideInInspector] public float      baseDamage        = 0f;
    [HideInInspector] public Transform  soloTarget;                              // singleplayer
    [HideInInspector] public ulong      targetNetworkObjectId = ulong.MaxValue;  // multiplayer
    [HideInInspector] public int        chainCount        = 3;
    [HideInInspector] public float      chainRadius       = 6f;
    [HideInInspector] public float      chainDamageFalloff = 0.6f;
    [HideInInspector] public float      travelTime        = 0.2f;
    [HideInInspector] public float      jumpDelay         = 0.3f;
    [HideInInspector] public GameObject hitEffect;

    private bool _executed;

    // Cached overlap buffer reused by FindNearestEnemy — avoids GC pressure on every
    // jump. 32 colliders is ample for the default chainRadius; if a hop returns 32
    // we still pick the nearest, just from a bounded sample.
    private static readonly Collider[] s_overlapBuffer = new Collider[32];

    // ─── NGO Lifecycle ────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        StartCoroutine(ChainRoutine());
    }

    // ─── Singleplayer Fallback ────────────────────────────────────────────────

    void Start()
    {
        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networkActive || _executed) return;
        StartCoroutine(ChainRoutine());
    }

    // ─── Chain Routine ────────────────────────────────────────────────────────

    IEnumerator ChainRoutine()
    {
        if (_executed) yield break;
        _executed = true;

        Transform primary = ResolvePrimaryTarget();
        if (primary == null)
        {
            Debug.LogWarning("[ChainLightning] Primary target not found — aborting chain.");
            yield break;
        }

        var visited  = new HashSet<Transform> { primary };
        float damage = precomputedDamage > 0f ? precomputedDamage : baseDamage;
        var jumpWait = jumpDelay > 0f ? new WaitForSeconds(jumpDelay) : null;

        // Travel from spawn point (fire point) to the primary target.
        yield return TravelTo(primary.position);
        HitTarget(primary, damage);
        Transform current = primary;

        for (int i = 1; i < chainCount; i++)
        {
            if (jumpWait != null) yield return jumpWait;

            Transform next = FindNearestEnemy(current, visited);
            if (next == null) break;

            yield return TravelTo(next.position);
            damage *= chainDamageFalloff;
            HitTarget(next, damage);
            visited.Add(next);
            current = next;
        }

        Debug.Log($"[ChainLightning] Chained to {visited.Count} target(s).");

        // Wait for any lingering VFX before despawning.
        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networkActive)
        {
            yield return new WaitForSeconds(2f);
            if (NetworkObject.IsSpawned) NetworkObject.Despawn(true);
        }
        else
        {
            Destroy(gameObject, 2f);
        }
    }

    // ─── Movement ─────────────────────────────────────────────────────────────

    IEnumerator TravelTo(Vector3 destination)
    {
        Vector3 direction = destination - transform.position;
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction.normalized);

        if (travelTime <= 0f)
        {
            transform.position = destination;
            yield break;
        }

        Vector3 origin  = transform.position;
        float   elapsed = 0f;

        while (elapsed < travelTime)
        {
            elapsed            += Time.deltaTime;
            transform.position  = Vector3.Lerp(origin, destination, elapsed / travelTime);
            yield return null;
        }

        transform.position = destination;
    }

    // ─── Target Resolution ────────────────────────────────────────────────────

    Transform ResolvePrimaryTarget()
    {
        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (!networkActive) return soloTarget;
        if (targetNetworkObjectId == ulong.MaxValue) return null;
        NetworkManager.Singleton.SpawnManager.SpawnedObjects
            .TryGetValue(targetNetworkObjectId, out NetworkObject no);
        return no != null ? no.transform : null;
    }

    Transform FindNearestEnemy(Transform from, HashSet<Transform> excluded)
    {
        int       count      = Physics.OverlapSphereNonAlloc(from.position, chainRadius, s_overlapBuffer);
        Transform best       = null;
        float     bestSqDist = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider col = s_overlapBuffer[i];
            if (!col.CompareTag("Enemy")) continue;
            if (excluded.Contains(col.transform)) continue;
            float sqDist = (col.transform.position - from.position).sqrMagnitude;
            if (sqDist < bestSqDist) { bestSqDist = sqDist; best = col.transform; }
        }
        return best;
    }

    // ─── Hit ──────────────────────────────────────────────────────────────────

    void HitTarget(Transform target, float damage)
    {
        HealthSystem health = target.GetComponent<HealthSystem>()
                           ?? target.GetComponentInChildren<HealthSystem>();
        health?.TakeDamage(Mathf.RoundToInt(damage), false);

        SpawnHitEffect(target.position + Vector3.up * 0.5f);
    }

    void SpawnHitEffect(Vector3 pos)
    {
        if (hitEffect == null) return;

        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networkActive)
            SpawnHitEffectClientRpc(pos);
        else
            Instantiate(hitEffect, pos, Quaternion.identity);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void SpawnHitEffectClientRpc(Vector3 pos)
    {
        if (hitEffect != null)
            Instantiate(hitEffect, pos, Quaternion.identity);
    }
}
