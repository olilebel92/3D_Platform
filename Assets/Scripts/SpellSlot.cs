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

    [Header("Cooldown")]
    [Tooltip("Grey radial overlay that drains counterclockwise during cooldown. Set Image type to Filled/Radial360 in the Inspector.")]
    [SerializeField] private Image cooldownOverlay;

    // ─── State ───────────────────────────────────────────────────────────────

    /// <summary>The spell currently held in this slot. Null = empty.</summary>
    public SpellData CurrentSpell { get; private set; }

    /// <summary>This slot's index in the bar (0-based, set by SpellBarManager).</summary>
    public int SlotIndex { get; private set; }

    // ─── Cooldown state ──────────────────────────────────────────────────────

    private SpellCaster _spellCaster;

    // ─── Drag-and-drop helpers ───────────────────────────────────────────────

    private static SpellSlot s_dragSource = null;
    private static GameObject s_dragGhost = null;

    // ─── Unity Lifecycle ─────────────────────────────────────────────────────

    private void Start()
    {
        if (cooldownOverlay == null)
            cooldownOverlay = transform.Find("CooldownOverlay")?.GetComponent<Image>();

        if (cooldownOverlay != null)
        {
            cooldownOverlay.type          = Image.Type.Filled;
            cooldownOverlay.fillMethod    = Image.FillMethod.Radial360;
            cooldownOverlay.fillOrigin    = (int)Image.Origin360.Top;
            cooldownOverlay.fillClockwise = false;
            cooldownOverlay.color         = new Color(0f, 0f, 0f, 0.6f);
            cooldownOverlay.fillAmount    = 1f;
            cooldownOverlay.enabled       = false;
        }
    }

    private void Update()
    {
        // Lazy-cache the local SpellCaster — player may spawn after this UI Start().
        // Use PlayerController.All instead of FindGameObjectWithTag so this remains
        // an O(1) list lookup per frame when the cache isn't populated yet.
        if (_spellCaster == null && PlayerController.All.Count > 0)
        {
            _spellCaster = PlayerController.All[0].GetComponent<SpellCaster>();
        }

        if (cooldownOverlay == null || CurrentSpell == null || _spellCaster == null)
        {
            if (cooldownOverlay != null) cooldownOverlay.fillAmount = 0f;
            return;
        }

        float remaining = _spellCaster.GetCooldownRemaining(CurrentSpell);
        if (remaining <= 0f)
        {
            cooldownOverlay.enabled = false;
            return;
        }

        cooldownOverlay.enabled    = true;
        cooldownOverlay.fillAmount = CurrentSpell.cooldown > 0f
            ? remaining / CurrentSpell.cooldown
            : 0f;
    }

    // ─── Setup ───────────────────────────────────────────────────────────────

    /// <summary>Called by SpellBarManager during initialization.</summary>
    public void Initialize(int index)
    {
        SlotIndex = index;

        if (slotNumberText != null)
            slotNumberText.text = index switch
            {
                0 => "(1) / (R1)",
                1 => "(2) / (L1)",
                _ => (index + 1).ToString()
            };

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