using UnityEngine;
using Unity.Netcode;

public class EnemySpawner : MonoBehaviour
{
    // ─── References ───────────────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("Drag your Enemy prefab here. Must have a NetworkObject component.")]
    public GameObject enemyPrefab;

    [Tooltip("Optional. When assigned, applies this data to every enemy spawned by this spawner.")]
    public EnemyData enemyData;

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
    [Tooltip("Random XZ scatter radius around each spawn point (prevents stacking).")]
    [SerializeField] private float _spawnScatterRadius = 1.5f;

    private float _spawnTimer = 0f;
    private int _currentEnemyCount = 0;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    void Update()
    {
        if (!IsServer()) return;

        _spawnTimer += Time.deltaTime;
        if (_spawnTimer >= spawnInterval)
        {
            _spawnTimer = 0f;
            if (_currentEnemyCount < maxEnemies)
                SpawnEnemy();
        }
    }

    // ─── Spawning ─────────────────────────────────────────────────────────────
    void SpawnEnemy()
    {
        GameObject prefabToSpawn = (enemyData != null && enemyData.prefab != null)
            ? enemyData.prefab
            : enemyPrefab;

        if (prefabToSpawn == null)
        {
            Debug.LogWarning("[EnemySpawner] No enemy prefab assigned (set EnemyData.prefab or EnemySpawner.enemyPrefab)!");
            return;
        }

        Vector3 spawnPos = transform.position;
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform randomPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];
            if (randomPoint != null) spawnPos = randomPoint.position;
        }

        Vector2 scatter = Random.insideUnitCircle * _spawnScatterRadius;
        spawnPos += new Vector3(scatter.x, 0f, scatter.y);

        GameObject enemy = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity);

        if (enemyData != null)
        {
            enemy.GetComponent<EnemyAI>()?.SetData(enemyData);
            enemy.GetComponent<EnemyReward>()?.SetData(enemyData);
        }

        EnemyTracker tracker = enemy.GetComponent<EnemyTracker>();
        if (tracker == null) tracker = enemy.AddComponent<EnemyTracker>();
        tracker.spawner = this;

        NetworkObject netObj = enemy.GetComponent<NetworkObject>();
        if (netObj != null && IsNetworkActive())
            netObj.Spawn(destroyWithScene: true);

        _currentEnemyCount++;
        Debug.Log("[EnemySpawner] Spawned enemy. Total: " + _currentEnemyCount);
    }

    // ─── Called by EnemyTracker on death ──────────────────────────────────────
    public void EnemyDestroyed() => _currentEnemyCount--;

    // ─── Network Helpers ──────────────────────────────────────────────────────
    private static bool IsNetworkActive() =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    private bool IsServer() => !IsNetworkActive() || NetworkManager.Singleton.IsServer;

    // ─── Gizmos ───────────────────────────────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        if (spawnPoints != null)
            foreach (Transform point in spawnPoints)
                if (point != null) Gizmos.DrawWireSphere(point.position, 0.5f);

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}
