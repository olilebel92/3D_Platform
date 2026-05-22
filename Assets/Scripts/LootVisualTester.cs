using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Drop this on any GameObject in a test scene.
/// Assign the LootPickup prefab and the test ItemData assets,
/// then hit Play — one pickup spawns per item in a row so you can compare rarity visuals.
///
/// Multiplayer: only the server spawns the pickups (via NetworkObject.Spawn) so they
/// replicate to every client and stay collectable. Singleplayer: spawned locally.
/// </summary>
public class LootVisualTester : MonoBehaviour
{
    [Header("Prefab")]
    [Tooltip("Your LootPickup prefab (must have LootPickup + PickupVisual + NetworkObject).")]
    public GameObject lootPickupPrefab;

    [Header("Items (one per rarity)")]
    public ItemData[] items;

    [Header("Layout")]
    [Tooltip("Distance between each pickup.")]
    public float spacing = 2f;

    [Tooltip("How far above the ground the item hovers.")]
    public float hoverHeight = 0.5f;

    void Start()
    {
        if (lootPickupPrefab == null)
        {
            Debug.LogWarning("[LootVisualTester] No prefab assigned.");
            return;
        }

        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        // In multiplayer only the server spawns the pickups — they replicate to all clients.
        // Clients spawning their own copies would duplicate every pickup.
        if (networkActive && !NetworkManager.Singleton.IsServer)
            return;

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] == null) continue;

            float xOffset = (i - (items.Length - 1) * 0.5f) * spacing;
            Vector3 rayOrigin = new Vector3(transform.position.x + xOffset, transform.position.y + 500f, transform.position.z);

            float groundY = transform.position.y;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 1000f))
                groundY = hit.point.y;

            Vector3 pos = new Vector3(rayOrigin.x, groundY + hoverHeight, rayOrigin.z);
            GameObject go = Instantiate(lootPickupPrefab, pos, Quaternion.identity);
            go.name = $"Preview_{items[i].itemName}";

            // Destroy any LootVisualTester on the clone so it doesn't re-spawn infinitely.
            LootVisualTester selfClone = go.GetComponent<LootVisualTester>();
            if (selfClone != null) Destroy(selfClone);

            LootPickup pickup = go.GetComponent<LootPickup>();
            if (pickup == null) continue;

            // Configure as a dropped item — serializes the item into a NetworkVariable so
            // clients reconstruct the same visual, interaction prompt and stats tooltip.
            pickup.PreInitDroppedItem(LootPickup.BuildRolledLootForItem(items[i]), items[i]);
            pickup.lifetime = 0f; // preview pickups never auto-despawn

            NetworkObject net = go.GetComponent<NetworkObject>();
            if (networkActive && net != null)
                net.Spawn();
        }
    }
}
