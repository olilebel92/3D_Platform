using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;                        // TextMeshPro support

/// <summary>
/// SpellSlot represents one cell in the spell bar.
/// Attach this to each slot prefab (a UI Image/Button object).
///
/// Responsibilities:
///   - Display the spell's icon (or an empty state)
///   - Show/hide the selection highlight
///   - Handle click selection
///   - Support drag-and-drop swapping between slots
/// </summary>
public class SpellSlot : MonoBehaviour,
    IPointerClickHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler,
    IDropHandler
{
    // ─── Inspector References ────────────────────────────────────────────────

    [Header("UI References")]
    [Tooltip("Displays the spell icon. Assign the child Image component.")]
    public Image iconImage;

    [Tooltip("Highlight border/overlay that shows when this slot is active.")]
    public GameObject highlightOverlay;

    [Tooltip("Optional label showing the slot number (TextMeshPro).")]
    public TextMeshProUGUI slotNumberText;  // TextMeshPro instead of legacy Text

    // ─── State ───────────────────────────────────────────────────────────────

    /// <summary>The spell currently held in this slot. Null = empty.</summary>
    public SpellData CurrentSpell { get; private set; }

    /// <summary>This slot's index in the bar (0-based, set by SpellBarManager).</summary>
    public int SlotIndex { get; private set; }

    // ─── Drag-and-drop helpers ───────────────────────────────────────────────

    private static SpellSlot s_dragSource = null;
    private static GameObject s_dragGhost = null;

    // ─── Setup ───────────────────────────────────────────────────────────────

    /// <summary>Called by SpellBarManager during initialization.</summary>
    public void Initialize(int index)
    {
        SlotIndex = index;

        if (slotNumberText != null)
            slotNumberText.text = (index + 1).ToString();

        SetHighlight(false);
        RefreshIcon();
    }

    /// <summary>Assign a spell to this slot and refresh the display.</summary>
    public void SetSpell(SpellData spell)
    {
        CurrentSpell = spell;
        RefreshIcon();
    }

    /// <summary>Remove the spell from this slot.</summary>
    public void ClearSpell()
    {
        CurrentSpell = null;
        RefreshIcon();
    }

    // ─── Visual Updates ───────────────────────────────────────────────────────

    public void SetHighlight(bool isActive)
    {
        if (highlightOverlay != null)
            highlightOverlay.SetActive(isActive);
    }

    private void RefreshIcon()
    {
        if (iconImage == null) return;

        if (CurrentSpell != null && CurrentSpell.icon != null)
        {
            iconImage.sprite = CurrentSpell.icon;
            iconImage.color = Color.white;
        }
        else
        {
            iconImage.sprite = null;
            iconImage.color = new Color(1, 1, 1, 0.25f);
        }
    }

    // ─── Click to Select ─────────────────────────────────────────────────────

    public void OnPointerClick(PointerEventData eventData)
    {
        SpellBarManager.Instance?.SelectSlot(SlotIndex);
    }

    // ─── Drag-and-Drop ───────────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (CurrentSpell == null) return;

        s_dragSource = this;

        s_dragGhost = new GameObject("DragGhost");
        s_dragGhost.transform.SetParent(GetComponentInParent<Canvas>().transform, false);
        s_dragGhost.transform.SetAsLastSibling();

        var rt = s_dragGhost.AddComponent<RectTransform>();
        rt.sizeDelta = ((RectTransform)transform).sizeDelta;

        var img = s_dragGhost.AddComponent<Image>();
        img.sprite = CurrentSpell.icon;
        img.raycastTarget = false;

        iconImage.color = new Color(1, 1, 1, 0.3f);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (s_dragGhost == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)s_dragGhost.transform.parent,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 localPoint);

        ((RectTransform)s_dragGhost.transform).anchoredPosition = localPoint;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (s_dragGhost != null)
        {
            Destroy(s_dragGhost);
            s_dragGhost = null;
        }

        RefreshIcon();
        s_dragSource = null;
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (s_dragSource == null || s_dragSource == this) return;
        SpellBarManager.Instance?.SwapSlots(s_dragSource.SlotIndex, this.SlotIndex);
    }
}