using System.Collections;
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

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        if (unequipButton != null)
        {
            unequipButton.onClick.AddListener(OnUnequip);
            unequipButton.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        if (PlayerInventory.Instance != null)
        {
            PlayerInventory.Instance.OnInventoryChanged += Refresh;
            Refresh();
        }
        else
        {
            // PlayerInventory.SetAsLocalInstance() fires after scene Start() in multiplayer.
            // Wait for it to become available before subscribing and refreshing.
            StartCoroutine(WaitForInventory());
        }
    }

    private IEnumerator WaitForInventory()
    {
        while (PlayerInventory.Instance == null)
            yield return null;

        PlayerInventory.Instance.OnInventoryChanged += Refresh;
        Refresh();
        Debug.Log($"[EquipSlotUI] {slotType} subscribed to PlayerInventory.");
    }

    void OnDestroy()
    {
        if (PlayerInventory.Instance != null)
            PlayerInventory.Instance.OnInventoryChanged -= Refresh;
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
            slotIconImage.sprite = (hasItem && equipped.icon != null) ? equipped.icon : null;
            slotIconImage.color  = hasItem ? equippedColor : emptyColor;
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
