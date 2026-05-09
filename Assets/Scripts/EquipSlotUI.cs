using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drop target for a specific equipment slot inside the Inventory panel.
/// Accepts DraggableItem drops whose slot type matches, then calls PlayerInventory.Equip().
/// </summary>
public class EquipSlotUI : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Slot Config")]
    [Tooltip("Which equipment slot this UI element represents.")]
    public EquipmentSlot slotType = EquipmentSlot.Boots;

    [Header("Display")]
    [Tooltip("Image that shows the equipped item icon (or empty state).")]
    public Image slotIconImage;

    [Tooltip("Label below the slot icon.")]
    public TextMeshProUGUI slotLabel;

    [Tooltip("Unequip button shown only when something is equipped.")]
    public Button unequipButton;

    [Header("Colors")]
    public Color emptyColor    = new Color(1f, 1f, 1f, 0.2f);
    public Color equippedColor = Color.white;
    public Color hoverColor    = new Color(0.8f, 1f, 0.8f, 1f);

    // ─── Static Registry ──────────────────────────────────────────────────────

    private static readonly Dictionary<EquipmentSlot, EquipSlotUI> _registry = new();

    /// <summary>Highlight (or clear) the equip slot that matches the given slot type.</summary>
    public static void SetHighlight(EquipmentSlot slot, bool on)
    {
        if (_registry.TryGetValue(slot, out var ui))
            ui.SetHighlightInternal(on);
    }

    private void SetHighlightInternal(bool on)
    {
        if (slotIconImage == null) return;
        if (on)
            slotIconImage.color = hoverColor;
        else
            Refresh();  // restores correct empty/equipped colour
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    // Tracks which inventory we are currently subscribed to so we can cleanly unsub.
    private PlayerInventory _subscribedInventory;

    void Start()
    {
        _registry[slotType] = this;

        if (unequipButton != null)
        {
            unequipButton.onClick.AddListener(OnUnequip);
            unequipButton.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        // If Instance is already set (solo, or fast NGO spawn) wire up immediately.
        if (PlayerInventory.Instance != null)
            BindToInventory(PlayerInventory.Instance);
        // Otherwise PlayerInventory.SetAsLocalInstance() will call BindToInventory()
        // via FindObjectsByType — no polling coroutine needed.
    }

    /// <summary>
    /// Called by PlayerInventory.SetAsLocalInstance() so every equip slot wires up
    /// even while the inventory panel is inactive (FindObjectsInactive.Include).
    /// </summary>
    public void BindToInventory(PlayerInventory inventory)
    {
        if (_subscribedInventory == inventory) return;

        if (_subscribedInventory != null)
            _subscribedInventory.OnInventoryChanged -= Refresh;

        _subscribedInventory = inventory;
        inventory.OnInventoryChanged += Refresh;
        Refresh();
        Debug.Log($"[EquipSlotUI] {slotType} bound to {inventory.gameObject.name}.");
    }

    void OnDestroy()
    {
        if (_subscribedInventory != null)
            _subscribedInventory.OnInventoryChanged -= Refresh;

        if (_registry.TryGetValue(slotType, out var registered) && registered == this)
            _registry.Remove(slotType);
    }

    // ─── IDropHandler ─────────────────────────────────────────────────────────

    public void OnDrop(PointerEventData eventData)
    {
        if (PlayerInventory.Instance == null) return;

        DraggableItem dragged = eventData.pointerDrag?.GetComponent<DraggableItem>();
        if (dragged == null || dragged.Item == null) return;

        if (dragged.Item.slot != slotType)
        {
            Debug.Log($"[EquipSlot] Wrong slot type — expected {slotType}, got {dragged.Item.slot}");
            return;
        }

        PlayerInventory.Instance.Equip(dragged.Item, dragged.InventoryIndex);
    }

    // ─── Refresh ──────────────────────────────────────────────────────────────

    public void Refresh()
    {
        // Always re-enable the label regardless of inventory state
        if (slotLabel != null)
            slotLabel.gameObject.SetActive(true);

        ItemData equipped = PlayerInventory.Instance?.GetEquipped(slotType);
        bool hasItem = equipped != null;


        if (slotIconImage != null)
        {
            bool hasIcon = hasItem && equipped != null && equipped.icon != null;
            slotIconImage.sprite = hasIcon ? equipped.icon : null;
            // Equipped but no icon → transparent so no white-square artifact
            // Equipped with icon   → full equippedColor (normally Color.white)
            // Empty slot           → emptyColor (normally dim/translucent)
            slotIconImage.color  = hasIcon  ? equippedColor
                                 : hasItem  ? Color.clear
                                 :            emptyColor;
        }

        if (slotLabel != null)
        {
            string slotName = slotType.ToString();
            if (hasItem)
            {
                string bonuses = BuildBonusString(equipped);
                slotLabel.text = $"{slotName}\n<color=#{equipped.RarityHex}>{equipped.itemName}</color>{bonuses}";
            }
            else
            {
                slotLabel.text = slotName;
            }
        }

        if (unequipButton != null)
            unequipButton.gameObject.SetActive(hasItem);
    }

    // ─── Tooltip ──────────────────────────────────────────────────────────────

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (ItemTooltip.Instance == null) return;
        ItemData equipped = PlayerInventory.Instance?.GetEquipped(slotType);
        if (equipped != null)
            ItemTooltip.Instance.Show(equipped, GetComponent<RectTransform>());
    }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
    {
        ItemTooltip.Instance?.Hide();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private string BuildBonusString(ItemData item)
    {
        if (item.statLines == null || item.statLines.Count == 0) return "";
        // Keep each stat on its own line
        string[] lines = item.BuildStatSummary().Split('\n');
        string result = "";
        foreach (string line in lines)
            if (!string.IsNullOrWhiteSpace(line))
                result += $"\n<size=80%><color=#AAD9FF>{line.Trim()}</color></size>";
        return result;
    }

    // ─── Unequip ──────────────────────────────────────────────────────────────

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (PlayerInventory.Instance?.GetEquipped(slotType) == null) return;
        PlayerInventory.Instance.Unequip(slotType);
    }

    private void OnUnequip() => PlayerInventory.Instance?.Unequip(slotType);
}
