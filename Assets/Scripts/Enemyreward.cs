using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// ─── Enemy Reward ─────────────────────────────────────────────────────────────

/// <summary>
/// Attach to an enemy prefab to grant XP, HP, and/or world drops on death.
/// All reward logic is server-authoritative — clients never fire rewards.
/// XP is shared across all connected players (co-op style).
/// HP heal goes to the nearest player at time of death.
/// Drops are spawned as NetworkObjects so they replicate to all clients.
/// </summary>
public class EnemyReward : NetworkBehaviour
{
    // ─── Enemy Data ───────────────────────────────────────────────────────────

    [Header("Enemy Data")]
    [Tooltip("Optional ScriptableObject. When assigned, overrides XP and HP reward fields below.")]
    [SerializeField] private EnemyData _data;

    // ─── XP ───────────────────────────────────────────────────────────────────

    [Header("XP Reward")]
    [Tooltip("Uncheck to disable this component's XP grant (e.g. when WaveManager handles XP instead).")]
    public bool enableXPReward = false;

    [Tooltip("Amount of XP awarded to every player when this enemy is destroyed.")]
    public int xpReward = 50;

    // ─── HP ───────────────────────────────────────────────────────────────────

    [Header("HP Reward")]
    [Tooltip("If true, heals the nearest player when this enemy is destroyed.")]
    public bool giveHPOnDeath = false;

    [Tooltip("Amount of HP restored to the nearest player on death.")]
    public int hpReward = 1;

    // ─── Drop Table ───────────────────────────────────────────────────────────

    [Header("Drop Table")]
    [Tooltip("Each entry is rolled independently on death. Multiple entries can drop at once.")]
    public List<DropEntry> dropTable = new();

    [Tooltip("Radius around the death position in which drops are scattered and burst outward. " +
             "Match to the enemy's physical size — 2 m is right for a human-sized enemy.")]
    public float dropScatterRadius = 2f;

    // ─── Lifecycle ────────────────────────────────────────────────────────────
    private HealthSystem _health;

    void Awake()
    {
        _health = GetComponent<HealthSystem>();
        if (_health != null) _health.OnDeath += GrantRewards;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (_health != null) _health.OnDeath -= GrantRewards;
    }

    // ─── Public Method for Spawner ────────────────────────────────────────────

    /// <summary>Assign an EnemyData asset. Reward values will be read from it on death.</summary>
    public void SetData(EnemyData data) => _data = data;

    // ─── Reward Entry Point ───────────────────────────────────────────────────

    /// <summary>
    /// Grants XP, HP heal, and rolls drops. Subscribed to HealthSystem.OnDeath,
    /// which fires at currentHealth ≤ 0f — not from OnDestroy, which can fire
    /// during scene unload and would spawn drops into a dying scene.
    /// </summary>
    public void GrantRewards()
    {
        // Server-authority in MP, always run in solo.
        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networkActive && !IsServer) return;

        // Override reward fields from EnemyData if one is assigned.
        if (_data != null)
        {
            enableXPReward  = true;
            xpReward        = _data.xpReward;
            giveHPOnDeath   = _data.giveHPOnDeath;
            hpReward        = _data.hpRewardOnDeath;
        }

        // ── XP — shared across all connected players ──────────────────────────
        if (enableXPReward && xpReward > 0)
        {
            foreach (GameObject p in PlayerController.All)
            {
                // ExperienceManager is per-player (no global singleton since v0.03)
                ExperienceManager xpManager = p.GetComponent<ExperienceManager>();
                if (xpManager != null)
                    xpManager.GainXP(xpReward);
            }
            DebugLogger.Log(DebugLogger.Category.XP, $"Awarded {xpReward} XP to all players for killing {gameObject.name}");
        }

        // ── HP — heals the nearest player only ───────────────────────────────
        if (giveHPOnDeath && hpReward > 0)
        {
            GameObject nearest = FindNearestPlayer();
            if (nearest != null)
            {
                HealthSystem health = nearest.GetComponent<HealthSystem>();
                if (health != null)
                    health.Heal(hpReward);
            }
        }

        // ── Drops ─────────────────────────────────────────────────────────────
        RollDrops();
    }

    // ─── Player Lookup Helper ─────────────────────────────────────────────────
    // Finds a player GameObject by NGO OwnerClientId.
    // Safer than client.PlayerObject which requires SpawnAsPlayerObject (not used here).
    private GameObject FindPlayerByClientId(ulong clientId)
    {
        foreach (GameObject p in PlayerController.All)
        {
            NetworkObject net = p.GetComponent<NetworkObject>();
            if (net != null && net.OwnerClientId == clientId)
                return p;
        }
        return null;
    }

    // ─── Nearest Player Helper ────────────────────────────────────────────────
    // Returns the closest "Player" tagged GameObject, or null if none exist.
    private GameObject FindNearestPlayer()
    {
        var players = PlayerController.All;
        if (players == null || players.Count == 0) return null;

        GameObject nearest     = null;
        float      nearestDist = float.MaxValue;

        foreach (GameObject p in players)
        {
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < nearestDist)
            {
                nearestDist = d;
                nearest     = p;
            }
        }

        return nearest;
    }

    // ─── Drop Helpers ─────────────────────────────────────────────────────────
    // Runs on server only. If the drop prefab has a NetworkObject component,
    // it is spawned via NGO so it replicates to all clients automatically.
    // Prefabs without NetworkObject are instantiated server-side only (local debug/FX).
    private void RollDrops()
    {
        // ── Gather all results first so we know the total drop count ──────────
        var allResults = new System.Collections.Generic.List<LootRollResult>();

        // SO loot table (EnemyData) and inline dropTable are both processed.
        if (_data != null && _data.lootTable != null)
            allResults.AddRange(_data.lootTable.Roll());

        if (dropTable != null)
        {
            foreach (DropEntry entry in dropTable)
            {
                if (entry.prefab == null) continue;
                if (Random.value > entry.dropChance) continue;
                allResults.Add(new LootRollResult
                {
                    entryType = LootEntryType.Prefab,
                    prefab    = entry.prefab,
                    count     = Random.Range(entry.minCount, entry.maxCount + 1),
                });
            }
        }

        if (allResults.Count == 0) return;

        // Count total individual drops so we can spread angles evenly.
        int total = 0;
        foreach (var r in allResults) total += r.count;

        // Random starting angle so the ring isn't always axis-aligned.
        float startAngle = Random.Range(0f, 360f);
        int   dropIndex  = 0;

        foreach (var result in allResults)
            for (int i = 0; i < result.count; i++)
                SpawnDrop(result, dropIndex++, total, startAngle);
    }

    private void SpawnDrop(LootRollResult result, int dropIndex, int totalDrops, float startAngle)
    {
        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        // ── Angular dispersion ────────────────────────────────────────────────
        // With 2+ drops, divide the circle into equal sectors and place each
        // item in its own sector so they never pile on top of each other.
        // With only 1 drop, use a plain random position (no sector needed).
        Vector3 targetPos;
        if (totalDrops <= 1)
        {
            Vector3 rand = Random.insideUnitSphere * dropScatterRadius;
            rand.y    = 0f;
            targetPos = transform.position + rand;
        }
        else
        {
            float sectorSize = 360f / totalDrops;
            float baseAngle  = startAngle + dropIndex * sectorSize;
            // ±25 % jitter within the sector so the ring looks organic, not mechanical.
            float angle      = baseAngle + Random.Range(-sectorSize * 0.25f, sectorSize * 0.25f);
            // Minimum 60 % of radius so items don't clump at the centre.
            float radius     = Random.Range(dropScatterRadius * 0.6f, dropScatterRadius);
            float rad        = angle * Mathf.Deg2Rad;
            targetPos = transform.position + new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
        }

        // ── Snap target to ground surface ─────────────────────────────────────
        // Items have no Rigidbody, so they won't fall on their own.
        // Sample the terrain height (or raycast for non-terrain scenes) so the
        // item always lands at ground level regardless of the enemy's centre Y.
        targetPos.y = SampleGroundHeight(targetPos);

        // Spawn at the enemy's centre — LootDropAnimation flies it to targetPos.
        GameObject dropped = Instantiate(result.prefab, transform.position, Quaternion.identity);

        LootDropAnimation       dropAnim   = dropped.GetComponent<LootDropAnimation>();
        SimpleLootDropAnimation simpleAnim = dropped.GetComponent<SimpleLootDropAnimation>();

        if (dropAnim != null)
            dropAnim.PreInit(targetPos);
        else if (simpleAnim != null)
            simpleAnim.PreInit(targetPos);
        else
            dropped.transform.position = targetPos;

            // Configure ItemPool pickups before NGO spawns them so the server-side
            // roll and visual are both applied in OnNetworkSpawn / Start.
            // NOTE: In multiplayer, remote (non-host) clients will not have itemPool
            // injected — they'll log a warning and miss the visual. Full MP support
            // requires a global ItemDataCatalog (future work). Host-client always works.
            if (result.entryType == LootEntryType.ItemPool && result.chosenItem != null)
            {
                LootPickup pickup = dropped.GetComponent<LootPickup>();
                if (pickup != null)
                {
                    pickup.lootType = LootType.Items;
                    pickup.itemPool = new System.Collections.Generic.List<ItemData> { result.chosenItem };
                }
                else
                {
                    Debug.LogWarning($"[EnemyReward] ItemPool drop prefab '{result.prefab.name}' has no LootPickup component.");
                }

                if (networkActive)
                    Debug.LogWarning("[EnemyReward] ItemPool drops in multiplayer only work correctly for the host-client. " +
                                     "Remote clients require a global ItemDataCatalog (future work).");
            }

            NetworkObject netObj = dropped.GetComponent<NetworkObject>();
            if (netObj != null && networkActive)
                netObj.Spawn();
    }

    // ─── Ground Height Helper ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the Y position of the ground at the given XZ location.
    /// Mirrors PlayerSpawner.GetSpawnPosition: prefers Terrain.SampleHeight,
    /// falls back to a downward raycast, then uses Y=0 as a last resort.
    /// </summary>
    private static float SampleGroundHeight(Vector3 pos)
    {
        // Priority 1: Unity Terrain heightmap — fast, no physics required.
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
            return terrain.SampleHeight(pos) + terrain.transform.position.y;

        // Priority 2: Physics raycast for non-Terrain scenes.
        Vector3 origin = new Vector3(pos.x, pos.y + 500f, pos.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1000f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.point.y;

        // Fallback: assume flat ground at Y=0.
        return 0f;
    }
}
