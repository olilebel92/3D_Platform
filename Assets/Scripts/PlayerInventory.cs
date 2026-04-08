using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Singleton that tracks every item the player owns and what is currently equipped
/// in each slot.  Attach to the Player GameObject alongside ExperienceManager.
/// </summary>
public class PlayerInventory : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static PlayerInventory Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Starting Items (testing / design)")]
    [Tooltip("Items added to the inventory at game start. Useful for testing.")]
    [SerializeField] private List<ItemData> startingItems = new();

    [Tooltip("Generate this many random boots on start (for testing ItemGenerator).")]
    [SerializeField] private int generateRandomItemsOnStart = 0;

    // ─── Private State ────────────────────────────────────────────────────────

    private readonly List<ItemData> _items    = new();
    private readonly Dictionary<EquipmentSlot, ItemData> _equipped = new();
    private readonly Dictionary<EquipmentSlot, int> _equippedIndex = new();

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fired whenever the inventory or equipment changes.</summary>
    public event Action OnInventoryChanged;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Called by PlayerController after spawning the local player.
    /// Sets this component as the global Instance so UI and scene scripts
    /// can always reach the local player's inventory.
    /// </summary>
    public void SetAsLocalInstance()
    {
        Instance = this;
        Debug.Log("[PlayerInventory] Set as local instance.");

        // Notify InventoryUI (bag grid subscription).
        InventoryUI inventoryUI = UnityEngine.Object.FindFirstObjectByType<InventoryUI>();
        if (inventoryUI != null) inventoryUI.SubscribeToInventoryIfNeeded();

        // Notify every EquipSlotUI — FindObjectsByType with FindObjectsInactive.Include
        // reaches slots inside the inventory panel even while it is hidden.
        foreach (EquipSlotUI slot in UnityEngine.Object.FindObjectsByType<EquipSlotUI>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            slot.BindToInventory(this);
        }
    }

    void Start()
    {
        foreach (var item in startingItems)
            AddItem(item);

        if (generateRandomItemsOnStart > 0 && ItemGenerator.Instance != null)
        {
            // Build a shuffled copy of every slot so the first N items are guaranteed
            // one-per-slot with no duplicates. Items beyond that fall back to random.
            var slots = new System.Collections.Generic.List<EquipmentSlot>(
                (EquipmentSlot[])System.Enum.GetValues(typeof(EquipmentSlot)));

            for (int i = slots.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (slots[i], slots[j]) = (slots[j], slots[i]);
            }

            for (int i = 0; i < generateRandomItemsOnStart; i++)
            {
                EquipmentSlot slot = i < slots.Count
                    ? slots[i]
                    : slots[UnityEngine.Random.Range(0, slots.Count)];
                AddItem(ItemGenerator.Instance.GenerateRandomItem(slot));
            }
        }
    }

    // ─── Inventory API ────────────────────────────────────────────────────────

    /// <summary>Add an item to the player's inventory, filling the first empty slot.</summary>
    public void AddItem(ItemData item)
    {
        if (item == null) return;

        int emptyIndex = _items.IndexOf(null);
        if (emptyIndex >= 0)
            _items[emptyIndex] = item;
        else
            _items.Add(item);

        Debug.Log($"[Inventory] Added: {item.itemName}");
        OnInventoryChanged?.Invoke();
    }

    /// <summary>Remove an item by inventory index. Unequips it first if equipped. Leaves a null gap to preserve other items' positions.</summary>
    public void RemoveItem(int inventoryIndex)
    {
        if (inventoryIndex < 0 || inventoryIndex >= _items.Count) return;

        ItemData item = _items[inventoryIndex];
        if (item == null) return;

        // Unequip first if this specific index is currently equipped
        if (_equippedIndex.TryGetValue(item.slot, out int equippedIdx) && equippedIdx == inventoryIndex)
        {
            _equipped.Remove(item.slot);
            _equippedIndex.Remove(item.slot);
        }

        Debug.Log($"[Inventory] Removed: {item.itemName}");
        _items[inventoryIndex] = null;   // null instead of RemoveAt — keeps all other indices stable
        OnInventoryChanged?.Invoke();
    }

    /// <summary>Returns a copy of all owned items.</summary>
    public List<ItemData> GetAllItems() => new(_items);

    /// <summary>Returns owned items that fit a specific slot.</summary>
    public List<ItemData> GetItemsForSlot(EquipmentSlot slot)
        => _items.Where(i => i != null && i.slot == slot).ToList();

    /// <summary>Swap two items by index. Handles equipped index updates automatically.</summary>
    public void SwapItems(int indexA, int indexB)
    {
        if (indexA < 0 || indexA >= _items.Count) return;
        if (indexB < 0 || indexB >= _items.Count) return;
        if (indexA == indexB) return;

        ItemData itemA = _items[indexA];
        ItemData itemB = _items[indexB];

        // If itemA was equipped at indexA, point it to indexB after the swap
        if (itemA != null && _equippedIndex.TryGetValue(itemA.slot, out int eA) && eA == indexA)
            _equippedIndex[itemA.slot] = indexB;

        // If itemB was equipped at indexB, point it to indexA after the swap
        if (itemB != null && _equippedIndex.TryGetValue(itemB.slot, out int eB) && eB == indexB)
            _equippedIndex[itemB.slot] = indexA;

        (_items[indexA], _items[indexB]) = (_items[indexB], _items[indexA]);
        Debug.Log($"[Inventory] Swapped index {indexA} ↔ {indexB}");
        OnInventoryChanged?.Invoke();
    }

    // ─── Equipment API ────────────────────────────────────────────────────────

    /// <summary>Returns the item currently equipped in a slot, or null.</summary>
    public ItemData GetEquipped(EquipmentSlot slot)
    {
        _equipped.TryGetValue(slot, out var item);
        return item;
    }

    /// <summary>Equip an item by its inventory index (replaces whatever was in that slot).</summary>
    public void Equip(ItemData item, int inventoryIndex)
    {
        if (item == null) return;
        _equipped[item.slot]      = item;
        _equippedIndex[item.slot] = inventoryIndex;
        Debug.Log($"[Inventory] Equipped: {item.itemName} (index {inventoryIndex})");
        OnInventoryChanged?.Invoke();
    }

    /// <summary>Unequip the item in the given slot.</summary>
    public void Unequip(EquipmentSlot slot)
    {
        if (!_equipped.ContainsKey(slot)) return;
        Debug.Log($"[Inventory] Unequipped: {_equipped[slot].itemName}");
        _equipped.Remove(slot);
        _equippedIndex.Remove(slot);
        OnInventoryChanged?.Invoke();
    }

    /// <summary>Returns true if the item at this specific inventory index is equipped.</summary>
    public bool IsEquippedAtIndex(EquipmentSlot slot, int index)
        => _equippedIndex.TryGetValue(slot, out int i) && i == index;

    /// <summary>
    /// Returns the inventory index of the first unequipped item for the given slot.
    /// Returns -1 if none available.
    /// </summary>
    public int FirstUnequippedIndex(EquipmentSlot slot)
    {
        for (int i = 0; i < _items.Count; i++)
            if (_items[i] != null && _items[i].slot == slot && !IsEquippedAtIndex(slot, i))
                return i;
        return -1;
    }

    // ─── Equipment Bonus Aggregation ──────────────────────────────────────────

    /// <summary>Total STR bonus from all equipped items.</summary>
    public int   TotalBonusSTR        => _equipped.Values.Sum(i => i.BonusSTR);

    /// <summary>Total AGI bonus from all equipped items.</summary>
    public int   TotalBonusAGI        => _equipped.Values.Sum(i => i.BonusAGI);

    /// <summary>Total INT bonus from all equipped items.</summary>
    public int   TotalBonusINT        => _equipped.Values.Sum(i => i.BonusINT);

    /// <summary>Total flat HP bonus from all equipped items.</summary>
    public int   TotalBonusHP         => _equipped.Values.Sum(i => i.BonusHP);

    /// <summary>Total HP regeneration per second from all equipped items.</summary>
    public float TotalBonusRegen      => _equipped.Values.Sum(i => i.BonusRegenPerSecond);

    /// <summary>Total Crit Rate bonus (0-1) from all equipped items.</summary>
    public float TotalBonusCritRate   => _equipped.Values.Sum(i => i.BonusCritRate);

    /// <summary>Total Crit Damage bonus (0-1) from all equipped items.</summary>
    public float TotalBonusCritDamage => _equipped.Values.Sum(i => i.BonusCritDamage);
}
