using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally generates ItemData (or WeaponItemData) instances at runtime
/// from the catalog of SubTypeData and RarityData assets assigned in the Inspector.
///
/// Add a new rarity / weapon / armor piece by creating an asset and dragging it into
/// the relevant catalog — no code changes required.
///
/// Call ItemGenerator.Instance.GenerateRandomItem() or .GenerateItemForWave(wave)
/// to obtain a fully populated ItemData ready to drop into PlayerInventory.
/// </summary>
public class ItemGenerator : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static ItemGenerator Instance { get; private set; }

    // ─── Inspector — Catalogs ─────────────────────────────────────────────────

    [Header("Catalogs")]
    [Tooltip("All SubTypeData assets the generator can roll. Drag every weapon/armor subtype in here.")]
    public List<SubTypeData> subTypes = new();

    [Tooltip("All RarityData assets in ascending order (lowest sortOrder first).")]
    public List<RarityData> rarities = new();

    [Header("Rarity Boost (optional)")]
    [Tooltip("Per-rarity wave boost — at wave waveAt the rarity weight is multiplied by waveMult. " +
             "Leave empty for flat weighting (Rarity.dropWeight only).")]
    public List<RarityWaveBoost> rarityBoosts = new();

    [System.Serializable]
    public class RarityWaveBoost
    {
        public RarityData rarity;
        [Tooltip("Wave at which the boost reaches waveMult. Earlier waves interpolate from 1.")]
        public int waveAt = 20;
        [Tooltip("Multiplier applied to drop weight at waveAt and beyond.")]
        public float waveMult = 5f;
    }

    [Header("Stat Base Ranges")]
    [Tooltip("Per-stat base min/max BEFORE the rarity multiplier. Auto-seeded with built-in " +
             "defaults the first time you view this component — tweak any entry to override.")]
    public List<StatBaseRange> statBaseRanges = new();

    [System.Serializable]
    public class StatBaseRange
    {
        public StatType stat;
        [Tooltip("Lowest base value (before rarity multiplier).")]
        public float min = 1f;
        [Tooltip("Highest base value (before rarity multiplier).")]
        public float max = 3f;
    }

    // Once true the designer has seen the seeded defaults and may intentionally
    // empty the list to fall back to the inline defaults in RollBaseValueFor.
    [SerializeField, HideInInspector] private bool _hasSeededStatRanges;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    // Editor-only: seed statBaseRanges the FIRST time the component is inspected so the
    // designer sees every StatType with its default min/max. After that the flag pins
    // the list — clearing it intentionally is respected.
    void OnValidate()
    {
        if (_hasSeededStatRanges) return;
        if (statBaseRanges != null && statBaseRanges.Count > 0) { _hasSeededStatRanges = true; return; }
        PopulateDefaultStatRanges();
        _hasSeededStatRanges = true;
    }

    [ContextMenu("Populate Stat Ranges with Defaults")]
    private void PopulateDefaultStatRanges()
    {
        statBaseRanges = new List<StatBaseRange>
        {
            new() { stat = StatType.STR,                min = 1f,   max = 3f  },
            new() { stat = StatType.AGI,                min = 1f,   max = 3f  },
            new() { stat = StatType.INT,                min = 1f,   max = 3f  },
            new() { stat = StatType.AllStats,           min = 1f,   max = 3f  },
            new() { stat = StatType.FlatHP,             min = 5f,   max = 15f },
            new() { stat = StatType.FlatMana,           min = 5f,   max = 15f },
            new() { stat = StatType.HPRegenPerSecond,   min = 0.5f, max = 2f  },
            new() { stat = StatType.ManaRegenPerSecond, min = 0.5f, max = 2f  },
            new() { stat = StatType.CritRate,           min = 2f,   max = 6f  },
            new() { stat = StatType.CritDamage,         min = 4f,   max = 12f },
            new() { stat = StatType.FireAffinity,       min = 2f,   max = 8f  },
            new() { stat = StatType.LightningAffinity,  min = 2f,   max = 8f  },
            new() { stat = StatType.FrostAffinity,      min = 2f,   max = 8f  },
            new() { stat = StatType.MovementSpeed,      min = 2f,   max = 6f  },
            new() { stat = StatType.SpellPower,         min = 2f,   max = 8f  },
        };
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Generate a random item of any subtype at the lowest available rarity.</summary>
    public ItemData GenerateRandomItem()
    {
        if (!HasCatalog()) return null;
        RarityData rarity = rarities[0];
        SubTypeData subType = subTypes[Random.Range(0, subTypes.Count)];
        return GenerateItem(subType, rarity);
    }

    /// <summary>Generate a random item restricted to a specific equipment slot.</summary>
    public ItemData GenerateRandomItem(EquipmentSlot slot)
    {
        if (!HasCatalog()) return null;

        var pool = subTypes.FindAll(s => s != null && s.equipSlot == slot);
        if (pool.Count == 0)
        {
            Debug.LogWarning($"[ItemGenerator] No SubType in catalog matches slot {slot}.");
            return null;
        }

        RarityData rarity = rarities[0];
        SubTypeData subType = pool[Random.Range(0, pool.Count)];
        return GenerateItem(subType, rarity);
    }

    /// <summary>
    /// Generate a wave-reward item. Rarity weights scale with wave number via the
    /// RarityData.waveUnlockThreshold gate and the rarityBoosts curve.
    /// </summary>
    public ItemData GenerateItemForWave(int wave)
    {
        if (!HasCatalog()) return null;

        SubTypeData subType = subTypes[Random.Range(0, subTypes.Count)];
        RarityData rarity   = RollRarityForWave(wave);
        return GenerateItem(subType, rarity);
    }

    /// <summary>
    /// Server-authoritative roll for a wave-scaled random appearance.
    /// Returns the catalog asset GUIDs so the result is stable under inspector reordering
    /// when sync'd via NetworkVariable. Returns false when the catalog isn't populated.
    /// </summary>
    public bool RollWaveAppearance(int wave, out string subTypeGuid, out string rarityGuid)
    {
        subTypeGuid = null;
        rarityGuid  = null;
        if (!HasCatalog()) return false;

        SubTypeData sub = subTypes[Random.Range(0, subTypes.Count)];
        RarityData  rar = RollRarityForWave(wave);
        subTypeGuid = sub != null ? sub.AssetGuid : null;
        rarityGuid  = rar != null ? rar.AssetGuid : null;
        return true;
    }

    /// <summary>Lookup a SubTypeData by its stable asset GUID. Returns null if not in the catalog.</summary>
    public SubTypeData GetSubTypeByGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid) || subTypes == null) return null;
        foreach (var s in subTypes)
            if (s != null && s.AssetGuid == guid) return s;
        return null;
    }

    /// <summary>Lookup a RarityData by its stable asset GUID. Returns null if not in the catalog.</summary>
    public RarityData GetRarityByGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid) || rarities == null) return null;
        foreach (var r in rarities)
            if (r != null && r.AssetGuid == guid) return r;
        return null;
    }

    /// <summary>Generate an item with explicit subtype and rarity.</summary>
    public ItemData GenerateItem(SubTypeData subType, RarityData rarity)
    {
        if (subType == null)
        {
            Debug.LogWarning("[ItemGenerator] GenerateItem called with null SubType.");
            return null;
        }
        if (rarity == null && rarities.Count > 0)
            rarity = rarities[0];

        bool isWeapon = subType.mainType != null && subType.mainType.isWeapon;
        ItemData item = isWeapon
            ? ScriptableObject.CreateInstance<WeaponItemData>()
            : ScriptableObject.CreateInstance<ItemData>();

        item.subType    = subType;
        item.rarity     = rarity;
        item.icon       = subType.PickIcon();
        item.itemName   = BuildName(subType, rarity);
        item.name       = item.itemName;   // ScriptableObject.name — shows in Inspector
        item.statLines  = RollStatLines(subType, rarity);

        Debug.Log($"[ItemGenerator] Generated: {item.itemName} ({(rarity != null ? rarity.displayName : "no-rarity")}) — {item.statLines.Count} stats");
        return item;
    }

    // ─── Rarity Rolling ───────────────────────────────────────────────────────

    private RarityData RollRarityForWave(int wave)
    {
        float total = 0f;
        foreach (var r in rarities)
        {
            if (r == null) continue;
            if (wave < r.waveUnlockThreshold) continue;
            total += r.dropWeight * GetWaveBoost(r, wave);
        }

        if (total <= 0f) return rarities[0];

        float roll = Random.Range(0f, total);
        float acc  = 0f;
        foreach (var r in rarities)
        {
            if (r == null) continue;
            if (wave < r.waveUnlockThreshold) continue;
            acc += r.dropWeight * GetWaveBoost(r, wave);
            if (roll <= acc) return r;
        }
        return rarities[0];
    }

    private float GetWaveBoost(RarityData rarity, int wave)
    {
        foreach (var b in rarityBoosts)
        {
            if (b == null || b.rarity != rarity) continue;
            if (b.waveAt <= 0) return 1f;
            float t = Mathf.Clamp01((wave - rarity.waveUnlockThreshold) / (float)Mathf.Max(1, b.waveAt - rarity.waveUnlockThreshold));
            return Mathf.Lerp(1f, b.waveMult, t);
        }
        return 1f;
    }

    // ─── Stat Rolling ─────────────────────────────────────────────────────────

    private List<StatLine> RollStatLines(SubTypeData subType, RarityData rarity)
    {
        var lines = new List<StatLine>();
        if (rarity == null) return lines;

        // ── Build the stat pool for this subtype ────────────────────────────
        // 1. Start from allowedStats (or every StatType if the subtype has no filter).
        var pool = new HashSet<StatType>();
        if (subType.AllowsAnyStat)
        {
            foreach (StatType s in System.Enum.GetValues(typeof(StatType))) pool.Add(s);
        }
        else
        {
            foreach (var s in subType.allowedStats) pool.Add(s);
        }

        // 2. This subtype's own reservedStats are always rollable (override allowedStats filter).
        if (subType.reservedStats != null)
        {
            foreach (var s in subType.reservedStats) pool.Add(s);
        }

        // 3. Stats reserved by OTHER subtypes are exclusive to those — remove from this pool
        //    (unless this subtype also reserves the same stat, which is unusual but legal).
        foreach (var other in subTypes)
        {
            if (other == null || other == subType || other.reservedStats == null) continue;
            foreach (var s in other.reservedStats)
            {
                if (subType.reservedStats == null || !subType.reservedStats.Contains(s))
                    pool.Remove(s);
            }
        }

        // 4. Rarity-banned stats never roll regardless of subtype.
        if (rarity.bannedStats != null)
        {
            foreach (var s in rarity.bannedStats) pool.Remove(s);
        }

        if (pool.Count == 0) return lines;

        var poolList = new List<StatType>(pool);
        int desired = Random.Range(rarity.statLineCountMin, rarity.statLineCountMax + 1);
        int max     = Mathf.Min(desired, poolList.Count);   // cap so we never request more lines than the pool can supply

        Shuffle(poolList);

        for (int i = 0; i < max; i++)
        {
            StatType t = poolList[i];
            float baseVal = RollBaseValueFor(t);
            float scaled  = baseVal * Mathf.Max(0.01f, rarity.statValueMultiplier);
            lines.Add(new StatLine { type = t, value = Mathf.Max(MinValueFor(t), Mathf.Round(scaled)) });
        }

        return lines;
    }

    // Base value range BEFORE rarity multiplier. Tuned for Normal rarity (multiplier 1).
    // Inspector overrides in `statBaseRanges` win; stats absent there fall back to the defaults below.
    private float RollBaseValueFor(StatType t)
    {
        foreach (var range in statBaseRanges)
        {
            if (range == null || range.stat != t) continue;
            return Random.Range(range.min, range.max);
        }
        return t switch
        {
            StatType.STR or StatType.AGI or StatType.INT   => Random.Range(1f, 3f),
            StatType.FlatHP                                 => Random.Range(5f, 15f),
            StatType.FlatMana                               => Random.Range(5f, 15f),
            StatType.HPRegenPerSecond                       => Random.Range(0.5f, 2f),
            StatType.ManaRegenPerSecond                     => Random.Range(0.5f, 2f),
            StatType.AllStats                               => Random.Range(1f, 3f),
            StatType.CritRate                               => Random.Range(2f, 6f),
            StatType.CritDamage                             => Random.Range(4f, 12f),
            StatType.FireAffinity                           => Random.Range(2f, 8f),
            StatType.LightningAffinity                      => Random.Range(2f, 8f),
            StatType.FrostAffinity                          => Random.Range(2f, 8f),
            StatType.MovementSpeed                          => Random.Range(2f, 6f),
            StatType.SpellPower                             => Random.Range(2f, 8f),
            _                                               => 1f,
        };
    }

    // Floor value so a roll never produces a useless 0.
    private float MinValueFor(StatType t) => t switch
    {
        StatType.HPRegenPerSecond or StatType.ManaRegenPerSecond => 0.5f,
        _                                                          => 1f,
    };

    // ─── Naming ───────────────────────────────────────────────────────────────

    private string BuildName(SubTypeData subType, RarityData rarity)
    {
        string prefix = rarity != null && rarity.sortOrder >= 1
            ? GetPrefixForRarity(rarity)
            : "";

        var pool = subType.nameMaterials;
        string mat = pool != null && pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : "";
        string sName = subType.displayName;

        return $"{prefix}{mat} {sName}".Trim();
    }

    private static string GetPrefixForRarity(RarityData rarity)
    {
        // Light flavour prefixes derived from sortOrder — designer can rename via displayName instead later.
        return rarity.sortOrder switch
        {
            1 => "Sturdy ",
            2 => "Fine ",
            3 => "Exquisite ",
            4 => "Ancient ",
            _ => rarity.sortOrder >= 5 ? "Mythic " : "",
        };
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private bool HasCatalog()
    {
        if (subTypes == null || subTypes.Count == 0)
        {
            Debug.LogWarning("[ItemGenerator] subTypes catalog is empty. Drag SubTypeData assets in via the Inspector.");
            return false;
        }
        if (rarities == null || rarities.Count == 0)
        {
            Debug.LogWarning("[ItemGenerator] rarities catalog is empty. Drag RarityData assets in via the Inspector.");
            return false;
        }
        return true;
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
