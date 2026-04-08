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

        Vector3 rayOrigin = new Vector3(
            transform.position.x + randomX,
            raycastOriginY,
            transform.position.z + randomZ);

        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, raycastOriginY * 2f))
        {
            Debug.LogWarning("[ItemSpawner] Raycast missed terrain — increase Raycast Origin Y.");
            return;
        }

        Vector3 spawnPos = hit.point + Vector3.up * 0.5f;
        GameObject item  = Instantiate(itemPrefab, spawnPos, Quaternion.identity);

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
