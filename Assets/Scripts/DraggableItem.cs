using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach to an item slot prefab inside the Inventory grid.
/// Handles drag-and-drop onto EquipSlotUI targets.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class DraggableItem : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler,
    IDropHandler,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Display")]
    [Tooltip("Image component that shows the item icon.")]
    public Image iconImage;

    [Tooltip("Label that shows the item name in rarity colour.")]
    public TextMeshProUGUI nameLabel;

    [Tooltip("Label that shows the stat lines (optional, can be small).")]
    public TextMeshProUGUI statsLabel;

    [Tooltip("Background image whose colour is tinted by rarity.")]
    public Image backgroundImage;

    [Tooltip("Border/overlay shown when this item is currently equipped.")]
    public GameObject equippedIndicator;

    [Tooltip("Small square image in the bottom-right of the icon, tinted to match rarity colour.")]
    public Image rarityBadge;

    // ─── Public State ─────────────────────────────────────────────────────────

    public ItemData Item { get; private set; }
    public int InventoryIndex { get; private set; }

    // ─── Private State ────────────────────────────────────────────────────────

    private Canvas        _rootCanvas;
    private CanvasGroup   _canvasGroup;
    private RectTransform _rectTransform;
    private GameObject    _dragVisual;
    private float         _lastClickTime;

    private const float DoubleClickThreshold = 0.3f;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _canvasGroup   = GetComponent<CanvasGroup>();
    }

    void Start()
    {
        // Walk up to find the root canvas
        foreach (var c in GetComponentsInParent<Canvas>())
            if (c.isRootCanvas) { _rootCanvas = c; break; }

        RefreshEquippedVisual();
    }

    void OnDestroy() { }

    // ─── Setup ────────────────────────────────────────────────────────────────

    /// <summary>Populate this slot with an item, or pass null to show an empty slot.</summary>
    public void Setup(ItemData item, int inventoryIndex)
    {
        Item           = item;
        InventoryIndex = inventoryIndex;

        // ── Empty state ───────────────────────────────────────────────────────
        if (item == null)
        {
            if (iconImage != null)      { iconImage.sprite = null; iconImage.color = Color.clear; }
            if (nameLabel != null)        nameLabel.text = "";
            if (statsLabel != null)       statsLabel.gameObject.SetActive(false);
            if (backgroundImage != null)  backgroundImage.color = Color.clear;
            if (rarityBadge != null)      rarityBadge.gameObject.SetActive(false);
            if (equippedIndicator != null) equippedIndicator.SetActive(false);
            return;
        }

        // ── Filled state ──────────────────────────────────────────────────────
        Sprite resolvedIcon = item.ResolvedIcon;
        if (iconImage != null)
        {
            iconImage.sprite = resolvedIcon;
            iconImage.color  = resolvedIcon != null ? Color.white : new Color(1f, 1f, 1f, 0.3f);
        }

        if (nameLabel != null)
        {
            string slotLabel = item.subType != null ? item.subType.displayName : item.EquipSlot.ToString();
            nameLabel.text =
                $"<size=75%><color=#AAAAAA>{slotLabel}</color></size>\n" +
                $"<color=#{item.RarityHex}>{item.itemName}</color>";
        }

        if (statsLabel != null)
            statsLabel.gameObject.SetActive(false);

        if (backgroundImage != null)
        {
            Color tint = item.RarityColor;
            tint.a = 0.15f;
            backgroundImage.color = tint;
        }

        if (rarityBadge != null)
        {
            rarityBadge.gameObject.SetActive(true);
            rarityBadge.color = item.RarityColor;
        }

        if (equippedIndicator != null)
        {
            var img = equippedIndicator.GetComponent<Image>();
            if (img != null) img.color = item.RarityColor;
        }

        RefreshEquippedVisual();
    }

    // ─── Drag Handlers ────────────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_rootCanvas == null || Item == null) return;

        InventoryUI.IsDragging = true;
        ItemTooltip.Instance?.Hide();
        ItemContextMenu.Instance?.Hide();

        // Destroy any leftover visual from a previous drag
        if (_dragVisual != null) Destroy(_dragVisual);

        _dragVisual = new GameObject("DragVisual", typeof(RectTransform));
        _dragVisual.transform.SetParent(_rootCanvas.transform, false);
        _dragVisual.transform.SetAsLastSibling();

        RectTransform rt = _dragVisual.GetComponent<RectTransform>();
        rt.sizeDelta  = _rectTransform.sizeDelta;
        rt.anchorMin  = Vector2.one * 0.5f;
        rt.anchorMax  = Vector2.one * 0.5f;
        rt.pivot      = Vector2.one * 0.5f;

        Image img = _dragVisual.AddComponent<Image>();
        img.sprite        = iconImage != null ? iconImage.sprite : Item.ResolvedIcon;
        img.raycastTarget = false;

        CanvasGroup cg = _dragVisual.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;   // must NOT block raycasts so IDropHandler fires
        cg.alpha          = 0.75f;

        // Dim the original slot
        _canvasGroup.alpha          = 0.4f;
        _canvasGroup.blocksRaycasts = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_dragVisual == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rootCanvas.transform as RectTransform,
            eventData.position,
            _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _rootCanvas.worldCamera,
            out Vector2 localPoint);

        (_dragVisual.transform as RectTransform).anchoredPosition = localPoint;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        InventoryUI.IsDragging = false;

        if (_dragVisual != null)
        {
            Destroy(_dragVisual);
            _dragVisual = null;
        }

        _canvasGroup.alpha          = 1f;
        _canvasGroup.blocksRaycasts = true;

        if (Item != null) EquipSlotUI.SetHighlight(Item.EquipSlot, false);
    }

    // ─── IDropHandler — swap with another inventory slot ─────────────────────

    public void OnDrop(PointerEventData eventData)
    {
        DraggableItem source = eventData.pointerDrag?.GetComponent<DraggableItem>();
        if (source == null || source == this) return;
        if (PlayerInventory.Instance == null) return;

        PlayerInventory.Instance.SwapItems(source.InventoryIndex, InventoryIndex);
    }

    // ─── Double-Click to Equip ────────────────────────────────────────────────

    public void OnPointerClick(PointerEventData eventData)
    {
        if (Item == null || PlayerInventory.Instance == null) return;

        // Right-click: open context menu (Equip / Drop / Delete).
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            ItemContextMenu.Instance?.Show(Item, InventoryIndex, eventData.position);
            return;
        }

        if (eventData.button != PointerEventData.InputButton.Left) return;

        float now = Time.unscaledTime;
        if (now - _lastClickTime < DoubleClickThreshold)
        {
            PlayerInventory.Instance.Equip(Item, InventoryIndex);
            ItemTooltip.Instance?.Hide();
            _lastClickTime = 0f; // reset so a third click doesn't re-trigger
        }
        else
        {
            _lastClickTime = now;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    // ─── Tooltip Hover ────────────────────────────────────────────────────────

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (Item == null || InventoryUI.IsDragging) return;
        ItemTooltip.Instance?.Show(Item, _rectTransform);
        EquipSlotUI.SetHighlight(Item.EquipSlot, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ItemTooltip.Instance?.Hide();
        if (!InventoryUI.IsDragging && Item != null)
            EquipSlotUI.SetHighlight(Item.EquipSlot, false);
    }

    private void RefreshEquippedVisual()
    {
        if (equippedIndicator == null || Item == null) return;

        equippedIndicator.SetActive(
            PlayerInventory.Instance != null &&
            PlayerInventory.Instance.GetEquipped(Item.EquipSlot) == Item);
    }
}
