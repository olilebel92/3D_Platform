using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drop target that permanently deletes any dragged item from PlayerInventory.
/// Place this below the inventory bag UI and assign the component references.
/// </summary>
public class TrashSlot : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Visuals")]
    [Tooltip("Background image of the trash slot — tinted on hover.")]
    public Image backgroundImage;

    [Header("Colors")]
    public Color normalColor = new Color(1f, 1f, 1f, 0.15f);
    public Color hoverColor  = new Color(1f, 0.2f, 0.2f, 0.6f);

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        if (backgroundImage != null)
            backgroundImage.color = normalColor;
    }

    // ─── IDropHandler ─────────────────────────────────────────────────────────

    public void OnDrop(PointerEventData eventData)
    {
        DraggableItem dragged = eventData.pointerDrag?.GetComponent<DraggableItem>();
        if (dragged == null || dragged.Item == null) return;
        if (PlayerInventory.Instance == null) return;

        PlayerInventory.Instance.RemoveItem(dragged.InventoryIndex);
        Debug.Log($"[TrashSlot] Deleted: {dragged.Item.itemName}");

        if (backgroundImage != null)
            backgroundImage.color = normalColor;
    }

    // ─── Hover Highlight ──────────────────────────────────────────────────────

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (backgroundImage != null)
            backgroundImage.color = hoverColor;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (backgroundImage != null)
            backgroundImage.color = normalColor;
    }
}
