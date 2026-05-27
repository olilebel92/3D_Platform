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

    [Tooltip("Generate this many random items on start (for testing ItemGenerator).")]
    [SerializeField] private int generateRandomItemsOnStart = 0;

    // ─── Private State ────────────────────────────────────────────────────────

    private readonly List<ItemData> _items    = new();
    private readonly Dictionary<EquipmentSlot, ItemData> _equipped = new();
    private readonly Dictionary<EquipmentSlot, int> _equippedIndex = new();
    private bool _suppressEvents = false;

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

        InventoryUI inventoryUI = UnityEngine.Object.FindFirstObjectByType<InventoryUI>();
        if (inventoryUI != null) inventoryUI.SubscribeToInventoryIfNeeded();

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
            // Shuffle every EquipmentSlot once so the first N items are guaranteed
            // one-per-slot with no duplicates. Items beyond that pick a random slot.
            var slots = new List<EquipmentSlot>((EquipmentSlot[])System.Enum.GetValues(typeof(EquipmentSlot)));
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

    // ─── Constants ────────────────────────────────────────────────────────────

    public const int MaxItems = 50;

    // ─── Inventory API ────────────────────────────────────────────────────────

    /// <summary>Add an item to the player's inventory, filling the first empty slot. Destroys the item if inventory is full.</summary>
    public void AddItem(ItemData item)
    {
        if (item == null) return;

        int filledCount = _items.Count(i => i != null);
        if (filledCount >= MaxItems)
        {
            Debug.Log($"[Inventory] Full ({MaxItems} items) — {item.itemName} destroyed.");
            return;
        }

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

        EquipmentSlot slot = item.EquipSlot;
        if (_equippedIndex.TryGetValue(slot, out int equippedIdx) && equippedIdx == inventoryIndex)
        {
            _equipped.Remove(slot);
            _equippedIndex.Remove(slot);
        }

        Debug.Log($"[Inventory] Removed: {item.itemName}");
        _items[inventoryIndex] = null;
        if (!_suppressEvents) OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// Returns the number of non-equipped items whose rarity sortOrder is ≤ maxRarity.sortOrder.
    /// Items with no rarity are always counted as deletable.
    /// </summary>
    public int CountDeletable(RarityData maxRarity)
    {
        int max = maxRarity != null ? maxRarity.sortOrder : int.MaxValue;
        int count = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            ItemData item = _items[i];
            if (item == null) continue;
            int itemSort = item.rarity != null ? item.rarity.sortOrder : -1;
            if (itemSort > max) continue;
            if (IsEquippedAtIndex(item.EquipSlot, i)) continue;
            count++;
        }
        return count;
    }

    /// <summary>
    /// Deletes all non-equipped items whose rarity sortOrder is ≤ maxRarity.sortOrder.
    /// Fires OnInventoryChanged once after all deletions. Returns the number deleted.
    /// </summary>
    public int DeleteByMaxRarity(RarityData maxRarity)
    {
        int max = maxRarity != null ? maxRarity.sortOrder : int.MaxValue;
        int deleted = 0;
        _suppressEvents = true;
        try
        {
            for (int i = 0; i < _items.Count; i++)
            {
                ItemData item = _items[i];
                if (item == null) continue;
                int itemSort = item.rarity != null ? item.rarity.sortOrder : -1;
                if (itemSort > max) continue;
                if (IsEquippedAtIndex(item.EquipSlot, i)) continue;
                string rarityName = item.rarity != null ? item.rarity.displayName : "no-rarity";
                _items[i] = null;
                deleted++;
                Debug.Log($"[Inventory] Batch deleted: {item.itemName} ({rarityName})");
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        if (deleted > 0)
            OnInventoryChanged?.Invoke();

        string maxName = maxRarity != null ? maxRarity.displayName : "any";
        Debug.Log($"[Inventory] Batch delete complete — {deleted} item(s) removed (≤ {maxName}).");
        return deleted;
    }

    /// <summary>Returns the item at a specific inventory index, or null if the slot is empty or out of range.</summary>
    public ItemData GetItemAt(int index)
        => (index >= 0 && index < _items.Count) ? _items[index] : null;

    /// <summary>Returns a copy of all owned items.</summary>
    public List<ItemData> GetAllItems() => new(_items);

    /// <summary>Returns owned items that fit a specific slot.</summary>
    public List<ItemData> GetItemsForSlot(EquipmentSlot slot)
        => _items.Where(i => i != null && i.EquipSlot == slot).ToList();

    /// <summary>Swap two items by index. Handles equipped index updates automatically.</summary>
    public void SwapItems(int indexA, int indexB)
    {
        if (indexA < 0 || indexA >= _items.Count) return;
        if (indexB < 0 || indexB >= _items.Count) return;
        if (indexA == indexB) return;

        ItemData itemA = _items[indexA];
        ItemData itemB = _items[indexB];

        if (itemA != null && _equippedIndex.TryGetValue(itemA.EquipSlot, out int eA) && eA == indexA)
            _equippedIndex[itemA.EquipSlot] = indexB;

        if (itemB != null && _equippedIndex.TryGetValue(itemB.EquipSlot, out int eB) && eB == indexB)
            _equippedIndex[itemB.EquipSlot] = indexA;

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
        EquipmentSlot slot = item.EquipSlot;
        _equipped[slot]      = item;
        _equippedIndex[slot] = inventoryIndex;
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
            if (_items[i] != null && _items[i].EquipSlot == slot && !IsEquippedAtIndex(slot, i))
                return i;
        return -1;
    }

    // ─── Equipment Bonus Aggregation ──────────────────────────────────────────

    public int   TotalBonusSTR        => _equipped.Values.Sum(i => i.BonusSTR);
    public int   TotalBonusAGI        => _equipped.Values.Sum(i => i.BonusAGI);
    public int   TotalBonusINT        => _equipped.Values.Sum(i => i.BonusINT);
    public int   TotalBonusHP         => _equipped.Values.Sum(i => i.BonusHP);
    public int   TotalBonusMana        => _equipped.Values.Sum(i => i.BonusMana);
    public float TotalBonusHPRegen     => _equipped.Values.Sum(i => i.BonusHPRegenPerSecond);
    public float TotalBonusManaRegen   => _equipped.Values.Sum(i => i.BonusManaRegenPerSecond);
    public float TotalBonusCritRate   => _equipped.Values.Sum(i => i.BonusCritRate);
    public float TotalBonusCritDamage   => _equipped.Values.Sum(i => i.BonusCritDamage);
    public float TotalBonusMovementSpeed => _equipped.Values.Sum(i => i.BonusMovementSpeed);
    public int   TotalBonusFireAffinity      => _equipped.Values.Sum(i => i.BonusFireAffinity);
    public int   TotalBonusLightningAffinity => _equipped.Values.Sum(i => i.BonusLightningAffinity);
    public int   TotalBonusFrostAffinity     => _equipped.Values.Sum(i => i.BonusFrostAffinity);
    public float TotalBonusSpellPower    => _equipped.Values.Sum(i => i.BonusSpellPower);
    public int   TotalBonusArmor         => _equipped.Values.Sum(i => i.BonusArmor);

    /// <summary>
    /// Total Affinity points the player has for the given school (equipment + skill tree).
    /// Returns 0 for Arcane / Healing — those schools are not affinity-eligible.
    /// 1 point = +1% damage dealt with that school, AND +1% damage taken reduction from that school
    /// (defensive cap of 80% is applied at the damage-resolution site, not here).
    /// </summary>
    public int GetAffinity(SpellSchool school)
    {
        int items = school switch
        {
            SpellSchool.Fire      => TotalBonusFireAffinity,
            SpellSchool.Lightning => TotalBonusLightningAffinity,
            SpellSchool.Frost     => TotalBonusFrostAffinity,
            _                     => 0,
        };
        int skillTree = 0;
        if (SkillTreeManager.Instance != null)
        {
            skillTree = school switch
            {
                SpellSchool.Fire      => SkillTreeManager.Instance.TotalFireAffinity,
                SpellSchool.Lightning => SkillTreeManager.Instance.TotalLightningAffinity,
                SpellSchool.Frost     => SkillTreeManager.Instance.TotalFrostAffinity,
                _                     => 0,
            };
        }
        return items + skillTree;
    }
}
