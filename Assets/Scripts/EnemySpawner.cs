using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    // ─── References ───────────────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("Drag your Enemy prefab here.")]
    public GameObject enemyPrefab;
    [Tooltip("Leave blank to auto-find by tag.")]
    public Transform playerTransform;

    // ─── Spawn Points ─────────────────────────────────────────────────────────
    [Header("Spawn Points")]
    [Tooltip("Add empty GameObjects as spawn points. Falls back to spawner position if empty.")]
    public Transform[] spawnPoints;

    // ─── Settings ─────────────────────────────────────────────────────────────
    [Header("Spawn Settings")]
    [Tooltip("Seconds between each spawn.")]
    public float spawnInterval = 4f;
    [Tooltip("Maximum enemies alive at once.")]
    public int maxEnemies = 5;

    private float spawnTimer = 0f;
    private int currentEnemyCount = 0;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    void Start()
    {
        // Auto-find player if not assigned
        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                playerTransform = playerObj.transform;
            else
                Debug.LogWarning("[EnemySpawner] No player found! Tag your player as 'Player'.");
        }
    }

    void Update()
    {
        spawnTimer += Time.deltaTime;

        if (spawnTimer >= spawnInterval)
        {
            spawnTimer = 0f;

            if (currentEnemyCount < maxEnemies)
                SpawnEnemy();
        }
    }

    // ─── Spawning ─────────────────────────────────────────────────────────────
    void SpawnEnemy()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("[EnemySpawner] No enemy prefab assigned!");
            return;
        }

        // Pick a random spawn point, or use this object's position as fallback
        Vector3 spawnPos = transform.position;
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform randomPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];
            if (randomPoint != null)
                spawnPos = randomPoint.position;
        }

        // Spawn the enemy
        GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
        currentEnemyCount++;

        // Assign player target automatically
        EnemyAI ai = enemy.GetComponent<EnemyAI>();
        if (ai != null && playerTransform != null)
            ai.SetTarget(playerTransform);

        // Track when this enemy is destroyed
        EnemyTracker tracker = enemy.AddComponent<EnemyTracker>();
        tracker.spawner = this;

        Debug.Log("[EnemySpawner] Spawned enemy. Total: " + currentEnemyCount);
    }

    // ─── Called by EnemyTracker when an enemy dies/despawns ──────────────────
    public void EnemyDestroyed()
    {
        currentEnemyCount--;
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        // Blue spheres at each spawn point
        Gizmos.color = Color.blue;
        if (spawnPoints != null)
            foreach (Transform point in spawnPoints)
                if (point != null)
                    Gizmos.DrawWireSphere(point.position, 0.5f);

        // White sphere at spawner position fallback
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}