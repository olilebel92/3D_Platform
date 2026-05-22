using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Right-click context menu that appears over an inventory item.
/// Shows Equip/Unequip, Drop, and Delete options.
///
/// Setup: add to the root Canvas alongside ItemTooltip.
/// Hierarchy:
///   ItemContextMenu (this script)
///   ├── Overlay      — full-screen transparent Image (raycastTarget=true), Button calls OnOverlayClicked
///   └── MenuPanel    — dark panel, anchored top-left (pivot 0,1)
///       ├── EquipButton  (Button + TextMeshProUGUI child)
///       ├── DropButton
///       └── DeleteButton
/// </summary>
public class ItemContextMenu : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static ItemContextMenu Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Panels")]
    [Tooltip("Full-screen transparent blocker that closes the menu when clicked outside.")]
    public GameObject overlay;

    [Tooltip("The context menu panel itself.")]
    public GameObject menuPanel;

    [Header("Buttons")]
    public Button equipButton;
    public Button dropButton;
    public Button deleteButton;

    [Header("Labels")]
    [Tooltip("TMP label on the Equip button — text changes to 'Unequip' when item is equipped.")]
    public TextMeshProUGUI equipLabel;

    // ─── Private State ────────────────────────────────────────────────────────

    private ItemData _item;
    private int      _inventoryIndex;
    private Canvas   _canvas;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        _canvas = GetComponentInParent<Canvas>();

        equipButton?.onClick.AddListener(OnEquipClicked);
        dropButton?.onClick.AddListener(OnDropClicked);
        deleteButton?.onClick.AddListener(OnDeleteClicked);

        Hide();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Show the context menu for an item, positioned at the given screen-space click position.
    /// </summary>
    public void Show(ItemData item, int inventoryIndex, Vector2 screenPosition)
    {
        if (item == null) return;

        _item           = item;
        _inventoryIndex = inventoryIndex;

        // Equip/Unequip label
        bool isEquipped = PlayerInventory.Instance != null
                          && PlayerInventory.Instance.GetEquipped(item.EquipSlot) == item;
        if (equipLabel != null)
            equipLabel.text = isEquipped ? "Unequip" : "Equip";

        overlay.SetActive(true);
        menuPanel.SetActive(true);
        menuPanel.transform.SetAsLastSibling();

        ItemTooltip.Instance?.Hide();

        PositionAt(screenPosition);
    }

    public void Hide()
    {
        if (overlay   != null) overlay.SetActive(false);
        if (menuPanel != null) menuPanel.SetActive(false);
        _item = null;
    }

    // ─── Button Handlers ──────────────────────────────────────────────────────

    public void OnOverlayClicked() => Hide();

    private void OnEquipClicked()
    {
        if (_item == null || PlayerInventory.Instance == null) { Hide(); return; }

        bool isEquipped = PlayerInventory.Instance.GetEquipped(_item.EquipSlot) == _item;
        if (isEquipped)
            PlayerInventory.Instance.Unequip(_item.EquipSlot);
        else
            PlayerInventory.Instance.Equip(_item, _inventoryIndex);

        Hide();
    }

    private void OnDropClicked()
    {
        if (_item == null) { Hide(); return; }
        ItemDropper.LocalInstance?.DropItem(_inventoryIndex);
        Hide();
    }

    private void OnDeleteClicked()
    {
        if (_item == null || PlayerInventory.Instance == null) { Hide(); return; }
        PlayerInventory.Instance.RemoveItem(_inventoryIndex);
        Hide();
    }

    // ─── Positioning ──────────────────────────────────────────────────────────

    private void PositionAt(Vector2 screenPos)
    {
        if (_canvas == null || menuPanel == null) return;

        RectTransform canvasRect = _canvas.transform as RectTransform;
        RectTransform panelRect  = menuPanel.GetComponent<RectTransform>();

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPos,
            _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera,
            out Vector2 localPos);

        // Clamp so the panel never goes off-canvas
        Vector2 half       = canvasRect.sizeDelta * 0.5f;
        Vector2 panelSize  = panelRect.sizeDelta;

        localPos.x = Mathf.Clamp(localPos.x, -half.x + panelSize.x * 0.5f, half.x - panelSize.x * 0.5f);
        localPos.y = Mathf.Clamp(localPos.y, -half.y + panelSize.y * 0.5f, half.y - panelSize.y * 0.5f);

        panelRect.localPosition = new Vector3(localPos.x, localPos.y, 0f);
    }
}
