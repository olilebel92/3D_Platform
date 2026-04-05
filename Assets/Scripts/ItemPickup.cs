using UnityEngine;

/// <summary>
/// World pickup that adds an item to the player's inventory on contact.
/// Pair with PickupItem on the same GameObject to get the bob + spin visual for free.
/// </summary>
public class ItemPickup : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Item")]
    [Tooltip("The ItemData ScriptableObject this pickup grants.")]
    public ItemData item;

    [Header("FX")]
    [Tooltip("Optional particle/effect prefab spawned on pickup.")]
    public GameObject pickupFX;

    [Tooltip("Optional sound played on pickup.")]
    public AudioClip pickupSound;

    // ─── Private State ────────────────────────────────────────────────────────

    private bool _collected = false;

    // ─── Trigger ──────────────────────────────────────────────────────────────

    void OnTriggerEnter(Collider other)
    {
        if (_collected) return;
        if (!other.CompareTag("Player")) return;

        if (item == null)
        {
            Debug.LogWarning("[ItemPickup] No ItemData assigned — pickup ignored.");
            return;
        }

        if (PlayerInventory.Instance == null)
        {
            Debug.LogWarning("[ItemPickup] PlayerInventory not found in scene.");
            return;
        }

        _collected = true;

        PlayerInventory.Instance.AddItem(item);

        if (pickupFX != null)
            Instantiate(pickupFX, transform.position, Quaternion.identity);

        if (pickupSound != null)
            AudioSource.PlayClipAtPoint(pickupSound, transform.position);

        Destroy(gameObject);
    }
}
