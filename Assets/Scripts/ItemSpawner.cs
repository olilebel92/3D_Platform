using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Periodically spawns item prefabs across a defined area using a downward raycast
/// to place them on terrain. Server-authoritative — only the host spawns items.
/// Pairs with LootPickup (reward logic) and PickupVisual (bob/spin) on the item prefab.
/// </summary>
public class ItemSpawner : MonoBehaviour
{
    [Header("Spawning")]
    [Tooltip("Drag your item prefab here. Must have a NetworkObject component.")]
    public GameObject itemPrefab;

    [Tooltip("Seconds between each spawn attempt.")]
    public float spawnInterval = 4f;

    [Tooltip("Maximum items alive at once.")]
    public int maxItems = 10;

    [Header("Spawn Area")]
    [Tooltip("X range around this spawner: -areaWidth to +areaWidth.")]
    public float areaWidth = 20f;

    [Tooltip("Z range around this spawner: -areaLength to +areaLength.")]
    public float areaLength = 20f;

    [Tooltip("Fixed world-space Y the raycast starts from. Must be above the highest point of your terrain.")]
    public float raycastOriginY = 10000f;

    // ─── Internal ─────────────────────────────────────────────────────────────
    private float _timer = 0f;
    private int _currentItemCount = 0;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    void Update()
    {
        if (!IsServer()) return;

        _timer += Time.deltaTime;
        if (_timer >= spawnInterval)
        {
            _timer = 0f;
            if (_currentItemCount < maxItems)
                SpawnItem();
        }
    }

    // ─── Spawning ─────────────────────────────────────────────────────────────
    void SpawnItem()
    {
        if (itemPrefab == null)
        {
            Debug.LogWarning("[ItemSpawner] No item prefab assigned!");
            return;
        }

        float randomX = Random.Range(-areaWidth,  areaWidth);
        float randomZ = Random.Range(-areaLength, areaLength);

        Vector3 candidate = new Vector3(
            transform.position.x + randomX,
            0f,
            transform.position.z + randomZ);

        // ── Priority 1: Terrain heightmap (fast, trigger-immune) ──────────────
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            candidate.y = terrain.SampleHeight(candidate) + terrain.transform.position.y;
        }
        else
        {
            // ── Priority 2: Downward raycast — ignore triggers so enemies,
            //    spell AOEs, and item SphereColliders don't give a false hit. ──
            Vector3 rayOrigin = new Vector3(candidate.x, raycastOriginY, candidate.z);
            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, raycastOriginY * 2f,
                                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Debug.LogWarning("[ItemSpawner] Raycast missed terrain — increase Raycast Origin Y.");
                return;
            }
            candidate.y = hit.point.y;
        }

        Vector3 spawnPos = candidate + Vector3.up * Mathf.Max(itemPrefab.transform.position.y, 0.05f);
        GameObject item  = Instantiate(itemPrefab, spawnPos, itemPrefab.transform.rotation);

        LootPickup loot = item.GetComponent<LootPickup>();
        if (loot != null) loot.spawner = this;

        NetworkObject netObj = item.GetComponent<NetworkObject>();
        if (netObj != null && IsNetworkActive())
            netObj.Spawn(destroyWithScene: true);

        _currentItemCount++;
        Debug.Log($"[ItemSpawner] Spawned item at {spawnPos}. Total: {_currentItemCount}");
    }

    // ─── Called by LootPickup when collected ──────────────────────────────────
    public void ItemCollected() => _currentItemCount--;

    // ─── Network Helpers ──────────────────────────────────────────────────────
    private static bool IsNetworkActive() =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    private bool IsServer() => !IsNetworkActive() || NetworkManager.Singleton.IsServer;
}
