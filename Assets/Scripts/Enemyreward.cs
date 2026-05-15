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

    [Tooltip("Radius around the death position in which drops are scattered.")]
    public float dropScatterRadius = 0.8f;

    // ─── Public Method for Spawner ────────────────────────────────────────────

    /// <summary>Assign an EnemyData asset. Reward values will be read from it on death.</summary>
    public void SetData(EnemyData data) => _data = data;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    // True from OnApplicationQuit until the next play session — prevents
    // OnDestroy from instantiating drops during Editor stop-play teardown,
    // where Application.isPlaying is still true when objects are destroyed.
    private static bool _appIsQuitting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetQuitFlag() => _appIsQuitting = false;

    void OnApplicationQuit() => _appIsQuitting = true;

    public override void OnDestroy()
    {
        base.OnDestroy();

        // Skip rewards when quitting, scene unloading, or play mode stopping
        if (_appIsQuitting) return;
        if (!gameObject.scene.isLoaded) return;
        if (!Application.isPlaying) return;

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
        if (dropTable == null || dropTable.Count == 0) return;

        foreach (DropEntry entry in dropTable)
        {
            if (entry.prefab == null) continue;
            if (Random.value > entry.dropChance) continue;

            int count = Random.Range(entry.minCount, entry.maxCount + 1);
            for (int i = 0; i < count; i++)
            {
                Vector3 scatter = Random.insideUnitSphere * dropScatterRadius;
                scatter.y = 0f;

                GameObject dropped = Instantiate(entry.prefab, transform.position + scatter, Quaternion.identity);

                // Spawn via NGO in multiplayer so drops appear on all clients.
                // In solo mode, plain Instantiate is enough.
                NetworkObject netObj = dropped.GetComponent<NetworkObject>();
                bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
                if (netObj != null && networkActive)
                    netObj.Spawn();
            }
        }
    }
}
