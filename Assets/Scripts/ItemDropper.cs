using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Handles dropping an item from the player's inventory onto the ground as a LootPickup.
/// Attach to the Player prefab alongside PlayerInventory.
///
/// Singleplayer: instantiates the prefab locally, item is auto-collected by walking over it.
/// Multiplayer : client removes the item from its inventory immediately, then sends the
///               serialized item data to the server which spawns a networked pickup visible
///               to all clients. Any player can then press F to pick it up.
///
/// Prefab requirement: _lootPickupPrefab must have LootPickup + PickupVisual components.
///   In multiplayer it must also be a registered NGO NetworkObject prefab.
///   The prefab's default LootType does not matter — PreInitDroppedItem overrides it at runtime.
/// </summary>
public class ItemDropper : NetworkBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Drop Settings")]
    [Tooltip("LootPickup prefab spawned when an item is dropped. Must be a registered NGO NetworkObject prefab for multiplayer.")]
    [SerializeField] private GameObject _lootPickupPrefab;

    [Tooltip("How far in front of the player the item lands.")]
    [SerializeField] private float _dropForwardOffset = 1.5f;

    [Tooltip("Lifetime (seconds) of a player-dropped pickup. 0 = never expires.")]
    [SerializeField] private float _droppedItemLifetime = 120f;

    // ─── Singleton ────────────────────────────────────────────────────────────

    public static ItemDropper LocalInstance { get; private set; }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    void Start()
    {
        // Singleplayer: NGO never calls OnNetworkSpawn, so set LocalInstance here.
        if (!IsNetworkActive())
            LocalInstance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner) LocalInstance = this;
    }

    public override void OnNetworkDespawn()
    {
        if (LocalInstance == this) LocalInstance = null;
    }

    public override void OnDestroy()
    {
        if (LocalInstance == this) LocalInstance = null;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Drop the item at inventoryIndex onto the ground near the player.
    /// Safe to call from any context — routes to server in multiplayer.
    /// </summary>
    public void DropItem(int inventoryIndex)
    {
        if (PlayerInventory.Instance == null) return;
        ItemData item = PlayerInventory.Instance.GetItemAt(inventoryIndex);
        if (item == null) return;

        // Remove from the local inventory immediately (client-authoritative).
        PlayerInventory.Instance.RemoveItem(inventoryIndex);

        Vector3 dropPos = GetDropPosition();

        if (!IsNetworkActive())
        {
            SpawnPickupLocal(item, dropPos);
        }
        else if (IsServer)
        {
            // Host-server: item reference is available locally — use it as soloFallback so
            // _generatedItem is set on the host and visuals apply correctly.
            SpawnDropNetworked(item, dropPos);
        }
        else
        {
            // Remote client: serialize item data and let the server spawn the networked pickup.
            DropItemServerRpc(LootPickup.BuildRolledLootForItem(item), dropPos);
        }
    }

    // ─── Server RPC ───────────────────────────────────────────────────────────

    [Rpc(SendTo.Server)]
    private void DropItemServerRpc(RolledLoot itemRoll, Vector3 dropPosition)
    {
        if (_lootPickupPrefab == null)
        {
            Debug.LogWarning("[ItemDropper] _lootPickupPrefab is not assigned — dropped item lost.");
            return;
        }

        GameObject go = Instantiate(_lootPickupPrefab, dropPosition, Quaternion.identity);

        LootPickup pickup = go.GetComponent<LootPickup>();
        if (pickup != null)
        {
            pickup.PreInitDroppedItem(itemRoll);
            pickup.lifetime = _droppedItemLifetime;
        }
        else
        {
            Debug.LogWarning("[ItemDropper] _lootPickupPrefab has no LootPickup component.");
        }

        NetworkObject net = go.GetComponent<NetworkObject>();
        if (net == null)
        {
            Debug.LogError("[ItemDropper] DropItemServerRpc: prefab has no NetworkObject — pickup will not network-spawn.");
            return;
        }
        net.Spawn(destroyWithScene: true); // scene-bound so it's cleared on scene reload/restart
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void SpawnDropNetworked(ItemData item, Vector3 pos)
    {
        if (_lootPickupPrefab == null)
        {
            Debug.LogWarning("[ItemDropper] _lootPickupPrefab is not assigned — dropped item lost.");
            return;
        }

        GameObject go = Instantiate(_lootPickupPrefab, pos, Quaternion.identity);
        LootPickup pickup = go.GetComponent<LootPickup>();
        if (pickup != null)
        {
            // Pass item as soloFallback so the host can resolve visuals without the ItemGenerator catalog.
            pickup.PreInitDroppedItem(LootPickup.BuildRolledLootForItem(item), item);
            pickup.lifetime = _droppedItemLifetime;
        }

        NetworkObject net = go.GetComponent<NetworkObject>();
        if (net == null)
        {
            Debug.LogError("[ItemDropper] SpawnDropNetworked: prefab has no NetworkObject — pickup will not network-spawn.");
            return;
        }
        net.Spawn(destroyWithScene: true); // scene-bound so it's cleared on scene reload/restart
    }

    private void SpawnPickupLocal(ItemData item, Vector3 pos)
    {
        if (_lootPickupPrefab == null)
        {
            Debug.LogWarning("[ItemDropper] _lootPickupPrefab is not assigned — dropped item lost.");
            return;
        }

        GameObject go = Instantiate(_lootPickupPrefab, pos, Quaternion.identity);

        LootPickup pickup = go.GetComponent<LootPickup>();
        if (pickup != null)
        {
            // Pass soloFallback so the item is guaranteed recoverable even without ItemGenerator.
            pickup.PreInitDroppedItem(LootPickup.BuildRolledLootForItem(item), item);
            pickup.lifetime = _droppedItemLifetime;
        }
        else
        {
            Debug.LogWarning("[ItemDropper] _lootPickupPrefab has no LootPickup component.");
        }
    }

    private Vector3 GetDropPosition()
    {
        // Bias toward forward but add random arc and lateral spread.
        float angle  = Random.Range(-35f, 35f);
        float radius = Random.Range(_dropForwardOffset * 0.6f, _dropForwardOffset * 1.4f);
        Vector3 dir  = Quaternion.Euler(0f, angle, 0f) * transform.forward;
        Vector3 pos  = transform.position + dir * radius;
        pos.y = SampleGroundHeight(pos);
        return pos;
    }

    private static float SampleGroundHeight(Vector3 pos)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
            return terrain.SampleHeight(pos) + terrain.transform.position.y;

        Vector3 origin = new Vector3(pos.x, pos.y + 500f, pos.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1000f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.point.y;

        return 0f;
    }

    private static bool IsNetworkActive() =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
}
