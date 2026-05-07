using System.Collections;
using UnityEngine;
using TMPro;
using Unity.Netcode;

// ─── Wave Custom Reward Entry ─────────────────────────────────────────────────
[System.Serializable]
public class WaveRewardEntry
{
    [Tooltip("Wave number on which this reward is granted (checked after wave clear).")]
    public int wave;

    [Tooltip("Specific ItemData assets given to the player on this wave.")]
    public ItemData[] items;

    [Tooltip("When true the normal random item drop is skipped — only these items are given.")]
    public bool replaceRandomReward = false;
}

// ─── Wave Enemy Definition ────────────────────────────────────────────────────
[System.Serializable]
public class WaveEnemyDefinition
{
    [Tooltip("The enemy prefab to spawn.")]
    public GameObject prefab;

    [Tooltip("First wave this enemy type appears on.")]
    public int unlockAtWave = 1;

    [Tooltip("If true, only spawns on boss waves (every bossWaveInterval waves).")]
    public bool isBoss = false;

    [Tooltip("How many of this enemy spawn per wave at unlock. Ignored for bosses — always spawns 1.")]
    public int baseCount = 3;

    [Tooltip("Extra enemies of this type added each wave after unlock. e.g. 1 = +1 each wave.")]
    public int countIncreasePerWave = 1;
}

// ─── Wave Manager ─────────────────────────────────────────────────────────────
/// <summary>
/// Server-authoritative wave spawner.
/// Made NetworkBehaviour so it can push XP, heals, and UI updates to all clients
/// via ClientRpc — HealthSystem / ExperienceManager have no NetworkVariables so
/// server-side calls alone would not update non-host players.
///
/// IMPORTANT: Add a NetworkObject component to this GameObject in the Inspector.
/// Scene-placed NetworkObjects are auto-spawned by NGO when the scene loads.
/// </summary>
public class WaveManager : NetworkBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    public static WaveManager Instance { get; private set; }

    // ─── Enemy Definitions ────────────────────────────────────────────────────
    [Header("Enemy Definitions")]
    [Tooltip("All enemy types. Each entry can be unlocked at a specific wave and optionally flagged as a boss.")]
    public WaveEnemyDefinition[] enemyDefinitions;

    // ─── Wave Settings ────────────────────────────────────────────────────────
    [Header("Wave Settings")]
    [Tooltip("Every N waves, boss enemies are also spawned alongside regular ones. Set 0 to disable boss waves.")]
    public int bossWaveInterval = 5;

    [Tooltip("Seconds between the end of a wave and the start of the next.")]
    public float timeBetweenWaves = 5f;

    [Tooltip("Seconds of warning before the first enemy spawns each wave.")]
    public float waveAnnounceDuration = 2f;

    // ─── Difficulty Scaling ───────────────────────────────────────────────────
    [Header("XP Rewards")]
    [Tooltip("Base XP awarded to the player per enemy kill.")]
    public float xpBase = 3f;

    [Tooltip("Bonus XP added per wave number. e.g. 0.25 = +0.25 XP per wave above wave 1.")]
    public float xpPerWave = 0.25f;

    [Header("Difficulty Scaling (applied per wave after wave 1)")]
    [Tooltip("Exponential HP growth per wave. e.g. 0.10 = ×1.10 each wave. Wave 20 = ×6.1, Wave 30 = ×15.9, Wave 35 = ×20.4.")]
    public float hpScalePerWave = 0.10f;

    [Tooltip("Exponential damage growth per wave. e.g. 0.08 = ×1.08 each wave. Wave 20 = ×3.95, Wave 30 = ×8.6.")]
    public float damageScalePerWave = 0.08f;

    [Tooltip("Linear speed growth per wave (kept linear so enemies don't feel teleport-fast). Wave 20 = ×1.95.")]
    public float speedScalePerWave = 0.05f;

    // ─── Wave Clear Settings ──────────────────────────────────────────────────
    [Header("Wave Clear Settings")]
    [Tooltip("When enabled, all players are fully healed at the end of each wave.")]
    public bool fullHealAfterWave = true;

    // ─── Custom Wave Rewards ──────────────────────────────────────────────────
    [Header("Custom Wave Rewards")]
    [Tooltip("Specific items granted on set wave numbers. Drag any ItemData asset here and set the wave. " +
             "Enable 'Replace Random Reward' to skip the normal random drop on that wave.")]
    public WaveRewardEntry[] customRewards;

    // ─── Spawn Points ─────────────────────────────────────────────────────────
    [Header("Spawn Points")]
    [Tooltip("Enemies spawn at these positions. When empty, spawns in a ring around each player instead.")]
    public Transform[] spawnPoints;

    [Header("Player Ring Spawn (used when Spawn Points is empty)")]
    [Tooltip("Minimum distance from the player when spawning in a ring.")]
    public float spawnRingMin = 5f;

    [Tooltip("Maximum distance from the player when spawning in a ring.")]
    public float spawnRingMax = 10f;

    // ─── UI ───────────────────────────────────────────────────────────────────
    [Header("UI (optional — assign TMP Text objects)")]
    [Tooltip("Persistent label, e.g. 'Wave 3'.")]
    public TMP_Text waveLabelText;

    [Tooltip("Status text, e.g. 'Enemies remaining: 5' or 'Next wave in 3s...'.")]
    public TMP_Text statusText;

    // ─── Private State ────────────────────────────────────────────────────────
    private int  currentWave      = 0;
    private int  aliveEnemyCount  = 0;
    private bool gameOver         = false;
    public Transform playerTransform;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Only the server runs wave logic.
        // In solo (no NetworkManager), ShouldRunWaves() returns true.
        if (!ShouldRunWaves()) return;

        // playerTransform is set via RegisterPlayer() called from PlayerController.
        // FindGameObjectWithTag is intentionally removed: players may not exist yet
        // when WaveManager.Start() fires (spawn-order race in multiplayer).
        if (PlayerController.All.Count > 0)
            playerTransform = PlayerController.All[0].transform;
        else
            Debug.LogWarning("[WaveManager] No Player registered at Start — ring spawn will use spawner position until RegisterPlayer() is called.");

        StartCoroutine(RunWaves());
    }

    /// <summary>
    /// Called by PlayerController.Start() so WaveManager always has a valid target
    /// even when players spawn after WaveManager.Start() completes (common in MP).
    /// The first registered player becomes the ring-spawn origin; subsequent calls
    /// are stored for future multi-player ring distribution.
    /// </summary>
    public void RegisterPlayer(Transform t)
    {
        if (t == null) return;
        if (playerTransform == null)
            playerTransform = t;
        Debug.Log($"[WaveManager] RegisterPlayer: {t.name} (playerTransform={playerTransform.name})");
    }

    // ─── Wave Loop ────────────────────────────────────────────────────────────
    IEnumerator RunWaves()
    {
        while (!gameOver)
        {
            currentWave++;
            bool isBossWave = bossWaveInterval > 0 && currentWave % bossWaveInterval == 0;

            // Announce wave
            UpdateWaveLabel();
            SetStatus(isBossWave
                ? "BOSS WAVE " + currentWave + " incoming!"
                : "Wave " + currentWave + " incoming!");
            yield return new WaitForSeconds(waveAnnounceDuration);

            if (gameOver) break;

            // Spawn all enemies for this wave
            SpawnWave(isBossWave);

            // Wait until every spawned enemy is dead
            while (aliveEnemyCount > 0 && !gameOver)
            {
                SetStatus("Enemies remaining: " + aliveEnemyCount);
                yield return null;
            }

            if (gameOver) break;

            SetStatus("Wave " + currentWave + " cleared!");

            // ── Full heal — send to every player's owning client ──────────────
            if (fullHealAfterWave)
            {
                if (!IsNetworkActive())
                {
                    foreach (GameObject p in PlayerController.All)
                    {
                        HealthSystem h = p.GetComponent<HealthSystem>();
                        if (h != null) h.Heal(h.maxHealth);
                    }
                }
                else
                {
                    // ClientRpc with no params → runs on ALL clients.
                    // Each client finds its own owned player and heals locally.
                    HealAllPlayersClientRpc();
                }
            }

            // ── Wave item reward ──────────────────────────────────────────────
            GrantWaveReward(currentWave);

            // Countdown to next wave
            float countdown = timeBetweenWaves;
            while (countdown > 0f && !gameOver)
            {
                SetStatus("Next wave in " + Mathf.CeilToInt(countdown) + "s...");
                countdown -= Time.deltaTime;
                yield return null;
            }
        }
    }

    // ─── Spawning ─────────────────────────────────────────────────────────────
    void SpawnWave(bool isBossWave)
    {
        if (enemyDefinitions == null || enemyDefinitions.Length == 0)
        {
            Debug.LogWarning("[WaveManager] No enemy definitions assigned!");
            return;
        }

        int totalSpawned = 0;

        foreach (WaveEnemyDefinition def in enemyDefinitions)
        {
            if (def.prefab == null) continue;
            if (def.unlockAtWave > currentWave) continue;

            // Bosses only appear on boss waves; regular enemies appear on every wave
            if (def.isBoss && !isBossWave) continue;

            int count = def.isBoss ? 1 : GetEnemyCount(def);

            for (int i = 0; i < count; i++)
            {
                Vector3 spawnPos = GetSpawnPosition();
                GameObject enemy = Instantiate(def.prefab, spawnPos, Quaternion.identity);

                ApplyDifficultyScaling(enemy);

                // Hook into death notification before spawning
                EnemyTracker tracker = enemy.GetComponent<EnemyTracker>();
                if (tracker == null) tracker = enemy.AddComponent<EnemyTracker>();
                tracker.waveManager = this;

                // Only spawn via NGO when networking is active.
                // In solo mode, plain Instantiate is enough — EnemyAI handles solo fallback.
                NetworkObject netObj = enemy.GetComponent<NetworkObject>();
                if (netObj != null && IsNetworkActive())
                    netObj.Spawn(destroyWithScene: true);

                aliveEnemyCount++;
                totalSpawned++;
            }
        }
    }

    int GetEnemyCount(WaveEnemyDefinition def)
    {
        int wavesActive = currentWave - def.unlockAtWave;
        return def.baseCount + wavesActive * def.countIncreasePerWave;
    }

    void ApplyDifficultyScaling(GameObject enemy)
    {
        float hpMult  = Mathf.Pow(1f + hpScalePerWave,     currentWave - 1);
        float dmgMult = Mathf.Pow(1f + damageScalePerWave,  currentWave - 1);
        float spdMult = 1f + (currentWave - 1) * speedScalePerWave;

        HealthSystem health = enemy.GetComponent<HealthSystem>();
        if (health != null)
        {
            int scaledMax = Mathf.Max(1, Mathf.RoundToInt(health.maxHealth * hpMult));
            health.maxHealth     = scaledMax;
            health.currentHealth = scaledMax;
        }

        EnemyAI ai = enemy.GetComponent<EnemyAI>();
        if (ai != null)
        {
            ai.attackDamage = Mathf.Max(1, Mathf.RoundToInt(ai.attackDamage * dmgMult));
            ai.moveSpeed    = ai.moveSpeed * spdMult;
        }
    }

    Vector3 GetSpawnPosition()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform pt = spawnPoints[Random.Range(0, spawnPoints.Length)];
            if (pt != null) return pt.position;
        }

        Vector3 origin = playerTransform != null ? playerTransform.position : transform.position;
        float angle    = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float distance = Random.Range(spawnRingMin, spawnRingMax);
        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
        return origin + offset;
    }

    // ─── Wave Reward ──────────────────────────────────────────────────────────

    /// <summary>
    /// Grants rewards after a wave clears.
    /// First checks for any custom entries matching this wave number, then
    /// falls through to a random item drop unless the entry suppresses it.
    /// </summary>
    void GrantWaveReward(int wave)
    {
        // ── Find a matching custom reward entry ───────────────────────────────
        int customIndex     = -1;
        bool skipRandom     = false;
        if (customRewards != null)
        {
            for (int i = 0; i < customRewards.Length; i++)
            {
                if (customRewards[i].wave == wave && customRewards[i].items != null
                    && customRewards[i].items.Length > 0)
                {
                    customIndex = i;
                    skipRandom  = customRewards[i].replaceRandomReward;
                    break;
                }
            }
        }

        // ── Grant custom items ────────────────────────────────────────────────
        if (customIndex >= 0)
        {
            if (!IsNetworkActive())
                GrantCustomItemsSolo(customIndex);
            else
                GrantCustomItemsClientRpc(customIndex);
        }

        // ── Grant random item (skipped if the custom entry replaces it) ───────
        if (skipRandom) return;

        if (!IsNetworkActive())
        {
            if (ItemGenerator.Instance == null)
            {
                Debug.LogWarning("[WaveManager] ItemGenerator not found — no random item reward given.");
                return;
            }
            ItemData reward = ItemGenerator.Instance.GenerateItemForWave(wave);
            foreach (GameObject p in PlayerController.All)
            {
                PlayerInventory inv = p.GetComponent<PlayerInventory>();
                if (inv != null) inv.AddItem(reward);
            }
        }
        else
        {
            // MP: ItemData is a ScriptableObject and cannot be sent over RPC directly.
            // Broadcast the wave number; each client rolls and applies its own item.
            GrantWaveItemClientRpc(wave);
        }
    }

    /// <summary>Solo path — grants every item in the custom entry to all players.</summary>
    void GrantCustomItemsSolo(int entryIndex)
    {
        WaveRewardEntry entry = customRewards[entryIndex];
        foreach (ItemData item in entry.items)
        {
            if (item == null) continue;
            foreach (GameObject p in PlayerController.All)
            {
                PlayerInventory inv = p.GetComponent<PlayerInventory>();
                if (inv != null)
                {
                    inv.AddItem(item);
                    Debug.Log($"[WaveManager] Wave {entry.wave} custom reward: {item.itemName}");
                }
            }
        }
    }

    /// <summary>
    /// MP path — sends the entry index so every client looks up the same
    /// ScriptableObject assets from its local customRewards array.
    /// </summary>
    [ClientRpc]
    private void GrantCustomItemsClientRpc(int entryIndex)
    {
        if (customRewards == null || entryIndex >= customRewards.Length) return;
        WaveRewardEntry entry = customRewards[entryIndex];

        foreach (GameObject p in PlayerController.All)
        {
            NetworkObject net = p.GetComponent<NetworkObject>();
            if (net == null || !net.IsOwner) continue;

            PlayerInventory inv = p.GetComponent<PlayerInventory>();
            if (inv == null) continue;

            foreach (ItemData item in entry.items)
            {
                if (item == null) continue;
                inv.AddItem(item);
                Debug.Log($"[WaveManager] Wave {entry.wave} custom reward: {item.itemName}");
            }
            return; // only the local-owned player receives rewards — stop scanning
        }
    }

    /// <summary>
    /// Sent to ALL clients on wave clear. Each client generates its own wave-reward
    /// item locally and adds it to the locally-owned player's inventory.
    /// </summary>
    [ClientRpc]
    private void GrantWaveItemClientRpc(int wave)
    {
        if (ItemGenerator.Instance == null)
        {
            Debug.LogWarning("[WaveManager] ItemGenerator not found on this client — no item reward.");
            return;
        }

        foreach (GameObject p in PlayerController.All)
        {
            NetworkObject net = p.GetComponent<NetworkObject>();
            if (net != null && net.IsOwner)
            {
                PlayerInventory inv = p.GetComponent<PlayerInventory>();
                if (inv != null)
                {
                    ItemData reward = ItemGenerator.Instance.GenerateItemForWave(wave);
                    inv.AddItem(reward);
                    Debug.Log($"[WaveManager] Wave {wave} random item reward granted: {reward.itemName}");
                }
                return;
            }
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Called by EnemyTracker when a wave-managed enemy dies.</summary>
    public void EnemyDestroyed()
    {
        aliveEnemyCount = Mathf.Max(0, aliveEnemyCount - 1);

        int xp = Mathf.Max(1, Mathf.RoundToInt(xpBase + currentWave * xpPerWave));

        if (!IsNetworkActive())
        {
            // Solo — apply directly
            foreach (GameObject p in PlayerController.All)
            {
                ExperienceManager xpManager = p.GetComponent<ExperienceManager>();
                if (xpManager != null) xpManager.GainXP(xp);
            }
        }
        else
        {
            // MP — send XP to each player's owning client.
            // ExperienceManager has no NetworkVariable so server-side GainXP()
            // would not update the client's XP bar or trigger level-up events.
            foreach (GameObject p in PlayerController.All)
            {
                NetworkObject net = p.GetComponent<NetworkObject>();
                if (net == null) continue;
                ClientRpcParams ownerOnly = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { net.OwnerClientId } }
                };
                GrantXPClientRpc(xp, ownerOnly);
            }
        }
    }

    /// <summary>
    /// Called by HealthSystem.Die() when a player dies.
    /// In MP the call originates on the owning client — route to server so the
    /// wave loop (which runs server-side) actually stops.
    /// </summary>
    public void OnPlayerDeath()
    {
        if (gameOver) return;

        if (IsNetworkActive() && !IsServer)
        {
            // Owning client notifies the server
            NotifyPlayerDeathServerRpc();
        }
        else
        {
            // Solo, or already on the server
            TriggerGameOver();
        }
    }

    // ─── Server RPCs ──────────────────────────────────────────────────────────

    [Rpc(SendTo.Server)]
    private void NotifyPlayerDeathServerRpc()
    {
        if (gameOver) return;
        TriggerGameOver();
    }

    // ─── Client RPCs ──────────────────────────────────────────────────────────

    /// <summary>
    /// Grants XP to the targeted client's locally-owned player.
    /// Sent with ClientRpcParams so only the correct player's machine receives it.
    /// </summary>
    [ClientRpc]
    private void GrantXPClientRpc(int xp, ClientRpcParams clientRpcParams = default)
    {
        foreach (GameObject p in PlayerController.All)
        {
            NetworkObject net = p.GetComponent<NetworkObject>();
            if (net != null && net.IsOwner)
            {
                ExperienceManager xpManager = p.GetComponent<ExperienceManager>();
                if (xpManager != null) xpManager.GainXP(xp);
                return;
            }
        }
    }

    /// <summary>
    /// Sent to ALL clients on wave clear. Each client heals its own locally-owned player.
    /// </summary>
    [ClientRpc]
    private void HealAllPlayersClientRpc()
    {
        foreach (GameObject p in PlayerController.All)
        {
            NetworkObject net = p.GetComponent<NetworkObject>();
            if (net != null && net.IsOwner)
            {
                HealthSystem h = p.GetComponent<HealthSystem>();
                if (h != null) h.Heal(h.maxHealth);
                return;
            }
        }
    }

    /// <summary>Syncs status text to all clients so Player 2 sees wave info.</summary>
    [ClientRpc]
    private void SyncStatusClientRpc(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    /// <summary>Syncs wave label to all clients.</summary>
    [ClientRpc]
    private void SyncWaveLabelClientRpc(string label)
    {
        if (waveLabelText != null) waveLabelText.text = label;
    }

    // ─── UI Helpers ───────────────────────────────────────────────────────────

    void UpdateWaveLabel()
    {
        string label = "Wave " + currentWave;
        if (waveLabelText != null) waveLabelText.text = label;
        // Push to all clients so every player sees the wave number
        if (IsNetworkActive() && IsServer) SyncWaveLabelClientRpc(label);
    }

    void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        // Push to all clients so every player sees enemy count, countdown, etc.
        if (IsNetworkActive() && IsServer) SyncStatusClientRpc(msg);
    }

    // ─── Internal ─────────────────────────────────────────────────────────────

    void TriggerGameOver()
    {
        gameOver = true;
        SetStatus("You survived " + currentWave + " wave" + (currentWave != 1 ? "s" : "") + "!");
    }

    // ─── Network Helpers ──────────────────────────────────────────────────────

    private static bool IsNetworkActive() =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    /// <summary>
    /// Returns true when this machine should run wave logic (server or solo).
    /// Named ShouldRunWaves to avoid collision with NetworkBehaviour.IsServer property.
    /// </summary>
    private bool ShouldRunWaves() =>
        !IsNetworkActive() || NetworkManager.Singleton.IsServer;

    // ─── Gizmos ───────────────────────────────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (spawnPoints != null)
            foreach (Transform pt in spawnPoints)
                if (pt != null)
                    Gizmos.DrawWireSphere(pt.position, 0.6f);
    }
}
