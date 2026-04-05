using System.Collections;
using UnityEngine;
using TMPro;

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
public class WaveManager : MonoBehaviour
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

    // ─── Spawn Points ─────────────────────────────────────────────────────────
    [Header("Spawn Points")]
    [Tooltip("Enemies spawn at these positions. When empty, spawns in a ring around the player instead.")]
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
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            playerTransform = playerObj.transform;
        else
            Debug.LogWarning("[WaveManager] No Player found! Tag your player as 'Player'.");

        StartCoroutine(RunWaves());
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
            SetStatus(isBossWave ? "BOSS WAVE " + currentWave + " incoming!" : "Wave " + currentWave + " incoming!");
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

            // ── Full heal ─────────────────────────────────────────────────────
            if (playerTransform != null)
            {
                HealthSystem playerHealth = playerTransform.GetComponent<HealthSystem>();
                if (playerHealth != null)
                    playerHealth.Heal(playerHealth.maxHealth);
            }

            // ── Wave reward ───────────────────────────────────────────────────
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
                aliveEnemyCount++;
                totalSpawned++;

                ApplyDifficultyScaling(enemy);

                // Assign player target
                EnemyAI ai = enemy.GetComponent<EnemyAI>();
                if (ai != null && playerTransform != null)
                    ai.SetTarget(playerTransform);

                // Hook into death notification
                EnemyTracker tracker = enemy.GetComponent<EnemyTracker>();
                if (tracker == null)
                    tracker = enemy.AddComponent<EnemyTracker>();
                tracker.waveManager = this;
            }
        }

    }

    int GetEnemyCount(WaveEnemyDefinition def)
    {
        // wavesActive = 0 on the unlock wave, so count starts at baseCount
        int wavesActive = currentWave - def.unlockAtWave;
        return def.baseCount + wavesActive * def.countIncreasePerWave;
    }

    void ApplyDifficultyScaling(GameObject enemy)
    {
        // HP and damage scale exponentially — each wave multiplies the previous value
        // Speed stays linear so enemies don't become impossibly fast
        float hpMult  = Mathf.Pow(1f + hpScalePerWave,     currentWave - 1);
        float dmgMult = Mathf.Pow(1f + damageScalePerWave,  currentWave - 1);
        float spdMult = 1f + (currentWave - 1) * speedScalePerWave;

        // HP — set before HealthSystem.Start() runs so Start() uses the scaled maxHealth
        HealthSystem health = enemy.GetComponent<HealthSystem>();
        if (health != null)
        {
            int scaledMax = Mathf.Max(1, Mathf.RoundToInt(health.maxHealth * hpMult));
            health.maxHealth    = scaledMax;
            health.currentHealth = scaledMax;
        }

        // Damage & Speed — EnemyAI.Start() applies moveSpeed to NavMeshAgent, so set before that
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

        // Ring spawn around the player
        Vector3 origin = playerTransform != null ? playerTransform.position : transform.position;
        float angle    = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float distance = Random.Range(spawnRingMin, spawnRingMax);
        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
        return origin + offset;
    }

    // ─── Wave Reward ──────────────────────────────────────────────────────────
    void GrantWaveReward(int wave)
    {
        if (ItemGenerator.Instance == null)
        {
            Debug.LogWarning("[WaveManager] ItemGenerator not found — no item reward given.");
            return;
        }

        if (PlayerInventory.Instance == null)
        {
            Debug.LogWarning("[WaveManager] PlayerInventory not found — no item reward given.");
            return;
        }

        ItemData reward = ItemGenerator.Instance.GenerateItemForWave(wave);
        PlayerInventory.Instance.AddItem(reward);

    }

    // ─── Public API ───────────────────────────────────────────────────────────
    /// <summary>Called by EnemyTracker when a wave-managed enemy dies.</summary>
    public void EnemyDestroyed()
    {
        aliveEnemyCount = Mathf.Max(0, aliveEnemyCount - 1);

        if (ExperienceManager.Instance != null)
        {
            int xp = Mathf.Max(1, Mathf.RoundToInt(xpBase + currentWave * xpPerWave));
            ExperienceManager.Instance.GainXP(xp);
        }
    }

    /// <summary>Call this when the player dies to end the run.</summary>
    public void OnPlayerDeath()
    {
        if (gameOver) return;
        gameOver = true;
        SetStatus("You survived " + currentWave + " wave" + (currentWave != 1 ? "s" : "") + "!");
;
    }

    // ─── UI Helpers ───────────────────────────────────────────────────────────
    void UpdateWaveLabel()
    {
        if (waveLabelText != null)
            waveLabelText.text = "Wave " + currentWave;
    }

    void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }

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
