using System.Collections.Generic;
using UnityEngine;

// ─── Loot Entry Type ──────────────────────────────────────────────────────────

/// <summary>
/// Controls how a single loot table entry resolves its drop.
/// </summary>
public enum LootEntryType
{
    /// <summary>Spawns a specific prefab as-is (HP orb, XP orb, pre-built LootPickup, etc.).</summary>
    Prefab,

    /// <summary>
    /// Picks one ItemData at random from the list and spawns it as a ground pickup.
    /// Requires a LootPickup prefab assigned to lootPickupPrefab.
    /// Works in singleplayer and multiplayer: remote clients resolve the synced item
    /// GUID through ItemDataCatalog (see LootPickup.FindItemInPoolByGuid).
    /// </summary>
    ItemPool,

    /// <summary>
    /// Spawns a fully procedural item via ItemGenerator (same as LootType.RandomItem).
    /// Requires a LootPickup prefab with LootType.RandomItem pre-set.
    /// Works in both singleplayer and multiplayer.
    /// </summary>
    RandomItem,
}

// ─── Loot Table Entry ─────────────────────────────────────────────────────────

/// <summary>
/// One entry in a LootTableSO. entryType controls which fields are used.
/// </summary>
[System.Serializable]
public class LootTableEntry
{
    [Tooltip("How this entry drops.\n" +
             "• Prefab     — spawn a specific prefab (HP orb, XP orb, pre-configured LootPickup, etc.).\n" +
             "• ItemPool   — pick one ItemData at random and spawn it as a ground pickup.\n" +
             "• RandomItem — spawn a procedurally generated item via ItemGenerator.")]
    public LootEntryType entryType = LootEntryType.Prefab;

    [Range(0f, 1f)]
    [Tooltip("Probability this entry drops on death. 1 = always, 0 = never.")]
    public float dropChance = 0.5f;

    [Tooltip("Minimum number of objects spawned when this entry rolls.")]
    public int minCount = 1;

    [Tooltip("Maximum number of objects spawned when this entry rolls.")]
    public int maxCount = 1;

    // ─── Prefab ───────────────────────────────────────────────────────────────

    [Header("Prefab Entry")]
    [Tooltip("Prefab to spawn (entryType = Prefab). Any GameObject — LootPickup, orb, FX, etc.")]
    public GameObject prefab;

    // ─── ItemPool / RandomItem ────────────────────────────────────────────────

    [Header("ItemPool / RandomItem Entry")]
    [Tooltip("Base LootPickup prefab instantiated for ItemPool and RandomItem entries.\n" +
             "Must have LootPickup + NetworkObject components.")]
    public GameObject lootPickupPrefab;

    // ─── ItemPool ─────────────────────────────────────────────────────────────

    [Header("ItemPool Entry")]
    [Tooltip("Pool of specific items to choose from (entryType = ItemPool). " +
             "One ItemData is selected at random per spawn.")]
    public List<ItemData> itemPool = new();
}

// ─── Loot Roll Result ─────────────────────────────────────────────────────────

/// <summary>
/// The resolved outcome of one LootTableEntry after Roll() is called.
/// EnemyReward uses this to instantiate and configure the correct pickup.
/// </summary>
public struct LootRollResult
{
    /// <summary>Entry type that produced this result — drives how EnemyReward configures the pickup.</summary>
    public LootEntryType entryType;

    /// <summary>Prefab to instantiate (either entry.prefab or entry.lootPickupPrefab).</summary>
    public GameObject prefab;

    /// <summary>How many of this prefab to spawn.</summary>
    public int count;

    /// <summary>The specific item chosen from the pool. Only set when entryType = ItemPool.</summary>
    public ItemData chosenItem;
}

// ─── Loot Table SO ────────────────────────────────────────────────────────────

/// <summary>
/// Reusable loot table. Assign to EnemyData.lootTable — any enemy sharing
/// the same asset rolls from the same pool.
/// Right-click → Create → Enemies → Loot Table.
/// </summary>
[CreateAssetMenu(fileName = "NewLootTable", menuName = "Enemies/Loot Table")]
public class LootTableSO : ScriptableObject
{
    [Header("Entries")]
    [Tooltip("Each entry is rolled independently on death. Mix Prefab, ItemPool, and RandomItem entries freely.")]
    [SerializeField] private List<LootTableEntry> entries = new();

    [Header("Limits")]
    [Tooltip("Hard cap on the total number of objects spawned per kill. 0 = unlimited.")]
    [SerializeField] private int maxDrops = 0;

    // ─── Roll ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rolls all entries and returns the list of results to spawn.
    /// Each entry is checked independently against its dropChance.
    /// Respects maxDrops when set.
    /// </summary>
    public List<LootRollResult> Roll()
    {
        var results    = new List<LootRollResult>();
        int totalDrops = 0;

        foreach (LootTableEntry entry in entries)
        {
            if (maxDrops > 0 && totalDrops >= maxDrops) break;

            // Validate required references per entry type
            switch (entry.entryType)
            {
                case LootEntryType.Prefab:
                    if (entry.prefab == null)
                    {
                        Debug.LogWarning($"[LootTableSO] '{name}': Prefab entry has no prefab assigned — skipping.");
                        continue;
                    }
                    break;

                case LootEntryType.ItemPool:
                    if (entry.lootPickupPrefab == null)
                    {
                        Debug.LogWarning($"[LootTableSO] '{name}': ItemPool entry has no lootPickupPrefab — skipping.");
                        continue;
                    }
                    if (entry.itemPool == null || entry.itemPool.Count == 0)
                    {
                        Debug.LogWarning($"[LootTableSO] '{name}': ItemPool entry has an empty item pool — skipping.");
                        continue;
                    }
                    break;

                case LootEntryType.RandomItem:
                    if (entry.lootPickupPrefab == null)
                    {
                        Debug.LogWarning($"[LootTableSO] '{name}': RandomItem entry has no lootPickupPrefab — skipping.");
                        continue;
                    }
                    break;
            }

            // Roll maxCount times independently at dropChance, then clamp to minCount as floor.
            // Max Count = number of rolls = maximum possible drops.
            // Min Count = guaranteed minimum (bypasses chance — always drops at least this many).
            // e.g. min 2 / max 6 / 85%: roll 6 times, but always drop at least 2.
            int numRolls = entry.maxCount;
            if (maxDrops > 0) numRolls = Mathf.Min(numRolls, maxDrops - totalDrops);

            int count = 0;
            for (int i = 0; i < numRolls; i++)
                if (Random.value <= entry.dropChance) count++;

            count = Mathf.Max(count, entry.minCount);  // guaranteed floor
            if (maxDrops > 0) count = Mathf.Min(count, maxDrops - totalDrops);

            // Pick one item from the pool (server-side, before spawning the pickup)
            ItemData chosenItem = null;
            if (entry.entryType == LootEntryType.ItemPool)
                chosenItem = entry.itemPool[Random.Range(0, entry.itemPool.Count)];

            results.Add(new LootRollResult
            {
                entryType  = entry.entryType,
                prefab     = entry.entryType == LootEntryType.Prefab ? entry.prefab : entry.lootPickupPrefab,
                count      = count,
                chosenItem = chosenItem,
            });

            totalDrops += count;
        }

        return results;
    }
}
