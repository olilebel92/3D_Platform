using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Attach this to your HealingWave prefab.
/// Each channel tick, heals all nearby Players within healRadius.
///
/// Heal formula:
///   heal = (baseHeal + TotalSpellDamageBonus) * SpellDamageMultiplier (INT)
///
/// Multiplayer: must be a NetworkObject registered in NetworkManager.
///   SpellCaster spawns this server-side and passes precomputedHeal so the
///   server never needs to read per-player singletons (ExperienceManager, SkillTree).
///   HealNearbyPlayers runs only on the server; all clients see the visual effect.
///
/// SpellData setup:
///   spellType      = Channel
///   castTime       = 1.5   (big wind-up before channeling starts)
///   channelTickRate = 0.5  (heals every 0.5 s while held)
/// </summary>
public class HealingWave : NetworkBehaviour
{
    // ─── Heal ─────────────────────────────────────────────────────────────────

    [Header("Heal")]
    [Tooltip("Base heal per channel tick before stat bonuses.")]
    public float baseHeal = 20f;

    // ─── AOE ──────────────────────────────────────────────────────────────────

    [Header("AOE")]
    [Tooltip("Radius within which ally Players are healed each tick.")]
    public float healRadius = 8f;

    // ─── Visuals ──────────────────────────────────────────────────────────────

    [Header("Visuals")]
    [Tooltip("How long the wave VFX lingers before despawning. Keep <= channelTickRate.")]
    public float lifetime = 1f;

    [Tooltip("How far above/below the spawn point to raycast when searching for the ground. " +
             "Increase if the caster stands on tall geometry.")]
    [SerializeField] private float groundSnapRayLength = 3f;

    [Tooltip("Fine Y offset applied after ground snap (positive = above ground). " +
             "Use a small value (e.g. 0.02) to prevent Z-fighting with the terrain.")]
    [SerializeField] private float groundSnapYOffset = 0.02f;


    // ─── Runtime (set by SpellCaster before Spawn) ────────────────────────────

    /// <summary>
    /// Pre-computed heal amount passed in by SpellCaster.SpawnChannelObjectServerRpc.
    /// When > 0 it bypasses stat lookups on the server (ExperienceManager, SkillTree
    /// singletons are per-client and not valid server-side).
    /// </summary>
    [HideInInspector] public float precomputedHeal = 0f;

    /// <summary>
    /// NetworkObjectId of the player who cast this wave. Set by SpellCaster before/after Spawn.
    /// Used to guarantee the caster is always healed regardless of server-side transform sync lag
    /// (OwnerNetworkTransform is client-authoritative — the server's copy can lag behind the RPC).
    /// </summary>
    [HideInInspector] public ulong casterNetworkObjectId = ulong.MaxValue;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        // Solo / offline: VFX setup only. SpellCaster calls ChannelTick() each tick
        // and Destroy() when the channel ends — no auto-heal or auto-destroy here.
        // In networked play OnNetworkSpawn fires before Start, so this is skipped.
        if (!IsSpawned)
            SnapToGround();
    }

    // ─── NGO Lifecycle ────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        // VFX setup only on all clients — ground-snap so the ring sits flush.
        // SpellCaster.SpawnChannelObjectServerRpc calls ChannelTick() for the first
        // heal immediately after Spawn(), then again on every tick.
        // Lifetime is managed externally: StopChannelServerRpc() despawns this object.
        SnapToGround();
    }

    // ─── Heal Logic ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called by SpellCaster each channel tick (server-side in MP, direct in solo).
    /// Heals all nearby players within healRadius.
    /// </summary>
    public void ChannelTick(float healAmount)
    {
        HealNearbyPlayers(healAmount);
    }

    void HealNearbyPlayers(float healAmount)
    {
        int amount = Mathf.Max(1, Mathf.RoundToInt(healAmount));

        // Physics.OverlapSphere cannot be used here: CharacterController (the player's
        // collider) is disabled on non-owner instances on the server, so remote players
        // are invisible to physics queries. Iterate by cached registry + distance instead.
        //
        // PlayerController.All is populated in Start() and cleared in OnDestroy() —
        // no allocation per tick unlike FindGameObjectsWithTag.
        //
        // The caster is ALWAYS healed regardless of distance: OwnerNetworkTransform is
        // client-authoritative, so the server's copy of the caster's position can lag
        // behind the firePos sent in the RPC, causing a false distance mismatch.
        IReadOnlyList<GameObject> players = PlayerController.All;

        int healedCount = 0;
        int totalHealed  = 0;

        foreach (GameObject playerObj in players)
        {
            NetworkObject netObj = playerObj.GetComponent<NetworkObject>();
            bool isCaster = netObj != null && netObj.NetworkObjectId == casterNetworkObjectId;

            if (!isCaster)
            {
                float dist = Vector3.Distance(transform.position, playerObj.transform.position);
                if (dist > healRadius) continue;
            }

            HealthSystem health = playerObj.GetComponent<HealthSystem>();
            if (health == null) continue;

            if (health.currentHealth >= health.maxHealth) continue;

            health.Heal(amount, suppressPopup: IsSpawned);

            if (IsSpawned && netObj != null)
            {
                PlayerController pc = playerObj.GetComponent<PlayerController>();
                if (pc != null)
                {
                    ClientRpcParams ownerOnly = new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams { TargetClientIds = new[] { netObj.OwnerClientId } }
                    };
                    pc.ApplyHealClientRpc(amount, ownerOnly);
                }
            }

            healedCount++;
            totalHealed += amount;
        }

        DebugLogger.Log(DebugLogger.Category.HealingWave,
            healedCount > 0
                ? $"Healed {healedCount} player(s) for {totalHealed} HP total."
                : "Tick fired but no player needed healing (all full HP or out of range).");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Raycasts downward from the spawn position to find the ground surface and
    /// moves the transform flush to it. Runs on all clients so the VFX sits on
    /// the terrain regardless of the caster's pivot height.
    /// </summary>
    void SnapToGround()
    {
        // Cast from slightly above in case we spawned exactly at ground level.
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundSnapRayLength + 0.5f))
        {
            Vector3 pos = transform.position;
            pos.y = hit.point.y + groundSnapYOffset;
            transform.position = pos;
        }
    }

    void DespawnOrDestroy()
    {
        if (IsSpawned)
            NetworkObject.Despawn(true);
        else
            Destroy(gameObject);
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
        Gizmos.DrawSphere(transform.position, healRadius);
    }
}
