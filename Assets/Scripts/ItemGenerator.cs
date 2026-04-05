using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton that procedurally generates ItemData at runtime.
/// Attach to any persistent GameObject (e.g. the Player or a GameManager).
///
/// Call  ItemGenerator.Instance.GenerateRandomItem(slot)
/// or    ItemGenerator.Instance.GenerateItem(slot, rarity)
/// to get a fully populated ItemData instance ready to add to PlayerInventory.
/// </summary>
public class ItemGenerator : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static ItemGenerator Instance { get; private set; }

    // ─── Rarity Config ────────────────────────────────────────────────────────

    [System.Serializable]
    public class RarityConfig
    {
        public ItemRarity rarity;

        [Tooltip("Relative drop weight (higher = more common).")]
        public float weight = 10f;

        [Tooltip("Minimum number of stat lines rolled.")]
        public int minStatLines = 1;

        [Tooltip("Maximum number of stat lines rolled.")]
        public int maxStatLines = 2;

        [Tooltip("Maximum number of those lines that can be rare stats (Crit Rate / Crit Damage).")]
        public int maxRareLines = 0;

        [Tooltip("Min / Max value for common stats (STR, AGI, INT).")]
        public Vector2 commonRange = new(1, 3);

        [Tooltip("Min / Max flat HP value (FlatHP stat).")]
        public Vector2 hpRange     = new(5, 15);

        [Tooltip("Min / Max HP regen per second value (RegenPerSecond stat).")]
        public Vector2 regenRange  = new(0, 0);

        [Tooltip("Min / Max value for AllStats (kept lower than rareRange — it adds to ALL three stats simultaneously).")]
        public Vector2 allStatsRange = new(0, 0);

        [Tooltip("Min / Max value for rare stats: CritRate / CritDamage (percentage points).")]
        public Vector2 rareRange   = new(0, 0);
    }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Rarity Configs")]
    public List<RarityConfig> rarityConfigs = new()
    {
        //                                                                                                                                                                              allStatsRange is intentionally small — it stacks onto all three stats simultaneously
        new() { rarity = ItemRarity.Normal,    weight = 50, minStatLines = 1, maxStatLines = 2, maxRareLines = 0, commonRange = new(1,  3),  hpRange = new(5,  15),  regenRange = new(0,    0),  allStatsRange = new(0, 0),   rareRange = new(0,  0)  },
        new() { rarity = ItemRarity.Uncommon,  weight = 30, minStatLines = 2, maxStatLines = 3, maxRareLines = 1, commonRange = new(2,  5),  hpRange = new(10, 25),  regenRange = new(0.5f, 2),  allStatsRange = new(0, 0),   rareRange = new(2,  6)  },
        new() { rarity = ItemRarity.Rare,      weight = 15, minStatLines = 3, maxStatLines = 3, maxRareLines = 3, commonRange = new(4,  8),  hpRange = new(20, 40),  regenRange = new(1,    3),  allStatsRange = new(1, 3),   rareRange = new(4,  12) },
        new() { rarity = ItemRarity.Epic,      weight = 4,  minStatLines = 4, maxStatLines = 4, maxRareLines = 3, commonRange = new(6,  12), hpRange = new(35, 60),  regenRange = new(2,    5),  allStatsRange = new(2, 5),   rareRange = new(8,  20) },
        new() { rarity = ItemRarity.Legendary, weight = 1,  minStatLines = 4, maxStatLines = 5, maxRareLines = 4, commonRange = new(10, 20), hpRange = new(50, 100), regenRange = new(3,    8),  allStatsRange = new(3, 8),   rareRange = new(15, 35) },
    };

    [Header("Icons — Boots")]
    [Tooltip("Pool of sprites randomly assigned to generated boots.")]
    public List<Sprite> bootsIcons = new();

    [Header("Name Parts — Boots")]
    public List<string> bootsMaterials = new() { "Leather", "Iron", "Shadow", "Storm", "Mystic", "Dragon" };

    [Header("Icons — Helm")]
    [Tooltip("Pool of sprites randomly assigned to generated helms.")]
    public List<Sprite> helmIcons = new();

    [Header("Name Parts — Helm")]
    public List<string> helmMaterials = new() { "Leather", "Iron", "Shadow", "Storm", "Mystic", "Dragon" };

    [Header("Icons — Pants")]
    [Tooltip("Pool of sprites randomly assigned to generated pants.")]
    public List<Sprite> pantsIcons = new();

    [Header("Name Parts — Pants")]
    public List<string> pantsMaterials = new() { "Leather", "Chain", "Shadow", "Storm", "Mystic", "Dragon" };

    [Header("Icons — Chest")]
    [Tooltip("Pool of sprites randomly assigned to generated chest armour.")]
    public List<Sprite> chestIcons = new();

    [Header("Name Parts — Chest")]
    public List<string> chestMaterials = new() { "Leather", "Iron", "Shadow", "Storm", "Mystic", "Dragon" };

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Generate a Normal (white) rarity item.</summary>
    public ItemData GenerateRandomItem(EquipmentSlot slot)
        => GenerateItem(slot, ItemRarity.Normal);

    /// <summary>
    /// Generate a wave-reward item. Rarity pool scales with wave number:
    ///   Waves  1–5  → Normal  – Uncommon
    ///   Waves  5–10 → Uncommon – Epic
    ///   Wave  10+   → Rare    – Legendary
    /// </summary>
    public ItemData GenerateItemForWave(int wave)
    {
        ItemRarity minRarity, maxRarity;

        if (wave <= 5)
        {
            minRarity = ItemRarity.Normal;
            maxRarity = ItemRarity.Rare;        // Rare at ~1–5 % via weight suppression
        }
        else if (wave <= 10)
        {
            minRarity = ItemRarity.Uncommon;
            maxRarity = ItemRarity.Epic;        // Epic at ~1–5 % via weight suppression
        }
        else
        {
            minRarity = ItemRarity.Rare;
            maxRarity = ItemRarity.Legendary;   // Legendary at ~1–5 % until wave 15, then ramps
        }

        // Pick a random equipment slot
        var slotValues = System.Enum.GetValues(typeof(EquipmentSlot));
        EquipmentSlot slot = (EquipmentSlot)slotValues.GetValue(Random.Range(0, slotValues.Length));

        ItemRarity rarity = RollRarityForWave(minRarity, maxRarity, wave);
        return GenerateItem(slot, rarity);
    }

    /// <summary>Generate an item with a specific rarity.</summary>
    public ItemData GenerateItem(EquipmentSlot slot, ItemRarity rarity)
    {
        RarityConfig cfg = rarityConfigs.Find(r => r.rarity == rarity) ?? rarityConfigs[0];

        ItemData item   = ScriptableObject.CreateInstance<ItemData>();
        item.slot       = slot;
        item.rarity     = rarity;
        item.icon       = PickIcon(slot);
        item.itemName   = BuildName(slot, rarity);
        item.name       = item.itemName;   // ScriptableObject.name (shows in Inspector)
        item.statLines  = RollStatLines(cfg);

        Debug.Log($"[ItemGenerator] Generated: {item.itemName} ({rarity}) — {item.statLines.Count} stats");
        return item;
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    private ItemRarity RollRarity()
    {
        float total = 0f;
        foreach (var c in rarityConfigs) total += c.weight;

        float roll = Random.Range(0f, total);
        float acc  = 0f;
        foreach (var c in rarityConfigs)
        {
            acc += c.weight;
            if (roll <= acc) return c.rarity;
        }
        return ItemRarity.Normal;
    }

    /// <summary>
    /// Weighted rarity roll restricted to [minRarity, maxRarity].
    /// Preserves the relative weights defined in rarityConfigs.
    /// </summary>
    private ItemRarity RollRarityInRange(ItemRarity minRarity, ItemRarity maxRarity)
    {
        float total = 0f;
        foreach (var c in rarityConfigs)
            if (c.rarity >= minRarity && c.rarity <= maxRarity)
                total += c.weight;

        if (total <= 0f) return minRarity;

        float roll = Random.Range(0f, total);
        float acc  = 0f;
        foreach (var c in rarityConfigs)
        {
            if (c.rarity < minRarity || c.rarity > maxRarity) continue;
            acc += c.weight;
            if (roll <= acc) return c.rarity;
        }
        return minRarity;
    }

    /// <summary>
    /// Weighted rarity roll that progressively boosts Epic and Legendary odds as the
    /// wave number increases, making late-game drops feel increasingly rewarding.
    ///
    /// Wave 10 → Epic ×1 / Legendary ×1  (base weights)
    /// Wave 15 → Epic ×3.5 / Legendary ×5.5
    /// Wave 20 → Epic ×6   / Legendary ×10
    /// </summary>
    private ItemRarity RollRarityForWave(ItemRarity minRarity, ItemRarity maxRarity, int wave)
    {
        float total = 0f;
        foreach (var c in rarityConfigs)
            if (c.rarity >= minRarity && c.rarity <= maxRarity)
                total += GetWaveAdjustedWeight(c, wave);

        if (total <= 0f) return minRarity;

        float roll = Random.Range(0f, total);
        float acc  = 0f;
        foreach (var c in rarityConfigs)
        {
            if (c.rarity < minRarity || c.rarity > maxRarity) continue;
            acc += GetWaveAdjustedWeight(c, wave);
            if (roll <= acc) return c.rarity;
        }
        return minRarity;
    }

    /// <summary>
    /// Returns the effective drop weight for a rarity at a given wave.
    ///
    /// Suppression math — each "surprise" tier is tuned to land at exactly 1–5 % of its pool:
    ///   Rare     in waves 1–5   (pool ≈ Normal 50 + Uncommon 30 = 80)
    ///   Epic     in waves 5–10  (pool ≈ Uncommon 30 + Rare 15  = 45)
    ///   Legendary in waves 10–15 (pool ≈ Rare 15 + Epic 4      = 19)
    ///
    /// After the suppression window each rarity rises to full weight, then Epic and
    /// Legendary are further boosted so wave 30+ is flooded with orange drops.
    /// </summary>
    private float GetWaveAdjustedWeight(RarityConfig cfg, int wave)
    {
        switch (cfg.rarity)
        {
            case ItemRarity.Rare:
                // Suppressed in waves 1–5 → 1 % at wave 1, 5 % at wave 5
                // factor derived from: x / (80 + x) = target%
                if (wave <= 5)
                    return cfg.weight * Mathf.Lerp(0.054f, 0.28f, (wave - 1f) / 4f);
                return cfg.weight;

            case ItemRarity.Epic:
                // Suppressed in waves 5–10 → 1 % at wave 5, 5 % at wave 10
                // factor derived from: x / (45 + x) = target%
                if (wave < 5)   return cfg.weight * 0.01f;
                if (wave <= 10) return cfg.weight * Mathf.Lerp(0.114f, 0.592f, (wave - 5f) / 5f);
                // Boosted from wave 10 → peak ×6 at wave 30
                return cfg.weight * Mathf.Lerp(1f, 6f, Mathf.Clamp01((wave - 10f) / 20f));

            case ItemRarity.Legendary:
                // Suppressed in waves 10–15 → 1 % at wave 10, 5 % at wave 15
                // factor derived from: x / (19 + x) = target%
                if (wave < 10)  return cfg.weight * 0.19f;
                if (wave <= 15) return cfg.weight * Mathf.Lerp(0.19f, 1f, (wave - 10f) / 5f);
                // Boosted from wave 15 → peak ×15 at wave 35 (swim in orange)
                return cfg.weight * Mathf.Lerp(1f, 15f, Mathf.Clamp01((wave - 15f) / 20f));

            default:
                return cfg.weight;
        }
    }

    private List<StatLine> RollStatLines(RarityConfig cfg)
    {
        int total       = Random.Range(cfg.minStatLines, cfg.maxStatLines + 1);
        int rareCount   = Mathf.Min(Random.Range(0, cfg.maxRareLines + 1), total);
        int commonCount = total - rareCount;

        // FlatHP available from Normal; STR/AGI/INT share commonRange
        var commonPool = new List<StatType> { StatType.STR, StatType.AGI, StatType.INT, StatType.FlatHP };

        // Regen unlocked at Uncommon; AllStats only at Rare and above
        var rarePool = new List<StatType> { StatType.CritRate, StatType.CritDamage };
        if (cfg.rarity >= ItemRarity.Uncommon)
            rarePool.Add(StatType.RegenPerSecond);
        if (cfg.rarity >= ItemRarity.Rare)
            rarePool.Add(StatType.AllStats);

        Shuffle(commonPool);
        Shuffle(rarePool);

        var lines = new List<StatLine>();

        for (int i = 0; i < commonCount && i < commonPool.Count; i++)
        {
            StatType t     = commonPool[i];
            Vector2  range = t == StatType.FlatHP ? cfg.hpRange : cfg.commonRange;
            if (IsRangeEmpty(range)) continue;
            lines.Add(Roll(t, range));
        }

        for (int i = 0; i < rareCount && i < rarePool.Count; i++)
        {
            StatType t = rarePool[i];
            Vector2 range = t switch
            {
                StatType.RegenPerSecond => cfg.regenRange,
                StatType.AllStats       => cfg.allStatsRange,
                _                       => cfg.rareRange,
            };
            if (IsRangeEmpty(range)) continue;
            lines.Add(Roll(t, range));
        }

        return lines;
    }

    private StatLine Roll(StatType type, Vector2 range)
    {
        float value = Mathf.Round(Random.Range(range.x, range.y));

        // Clamp to a sensible minimum so a stat can never be 0
        bool isDecimal  = type == StatType.RegenPerSecond;
        bool isPercent  = type == StatType.CritRate || type == StatType.CritDamage;
        float minValue  = isDecimal ? 0.5f : isPercent ? 1f : 1f;
        value = Mathf.Max(minValue, value);

        return new StatLine { type = type, value = value };
    }

    /// <summary>Returns true when a range is unconfigured (both values are 0).</summary>
    private bool IsRangeEmpty(Vector2 range) => range.x == 0f && range.y == 0f;

    private Sprite PickIcon(EquipmentSlot slot)
    {
        var pool = slot switch
        {
            EquipmentSlot.Boots => bootsIcons,
            EquipmentSlot.Helm  => helmIcons,
            EquipmentSlot.Pants => pantsIcons,
            EquipmentSlot.Chest => chestIcons,
            _                   => null,
        };
        if (pool == null || pool.Count == 0) return null;
        return pool[Random.Range(0, pool.Count)];
    }

    private string BuildName(EquipmentSlot slot, ItemRarity rarity)
    {
        string prefix = rarity switch
        {
            ItemRarity.Uncommon  => "Sturdy ",
            ItemRarity.Rare      => "Fine ",
            ItemRarity.Epic      => "Exquisite ",
            ItemRarity.Legendary => "Ancient ",
            _                    => "",
        };

        var pool = slot switch
        {
            EquipmentSlot.Boots => bootsMaterials,
            EquipmentSlot.Helm  => helmMaterials,
            EquipmentSlot.Pants => pantsMaterials,
            EquipmentSlot.Chest => chestMaterials,
            _                   => new List<string> { slot.ToString() },
        };
        string mat   = pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : "";
        string sName = slot.ToString();

        return $"{prefix}{mat} {sName}".Trim();
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
