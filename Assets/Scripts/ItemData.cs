using System.Collections.Generic;
using UnityEngine;

// ─── Enums ────────────────────────────────────────────────────────────────────
// EquipmentSlot lives in Items/EquipmentSlot.cs.
// ItemRarity is gone — rarity is now a RarityData ScriptableObject reference.

/// <summary>All stat types a generated item can roll.</summary>
public enum StatType
{
    STR,
    AGI,
    INT,
    FlatHP,          // flat bonus hit points
    FlatMana,        // flat bonus mana added to max mana pool
    HPRegenPerSecond,
    ManaRegenPerSecond,
    AllStats,        // adds to STR, AGI, and INT simultaneously
    CritRate,    // percentage points (5 → +5 % crit rate)
    CritDamage,  // percentage points (25 → +25 % crit dmg)
    FireAffinity,  // 1 point = +1% fire damage dealt AND +1% fire damage reduction (defensive capped at 80%).
                   // Inherits the old "FireDamage" slot (enum index 10) so existing .asset stat lines auto-migrate.
    MovementSpeed, // percentage points (20 → +20 % move speed)
    SpellPower,
    Armor,             // 1 point = 1% physical damage reduction (capped at 90% in HealthSystem)
    LightningAffinity, // 1 point = +1% lightning damage dealt AND +1% lightning damage reduction (defensive capped at 80%)
    FrostAffinity,     // 1 point = +1% frost damage dealt AND +1% frost damage reduction (defensive capped at 80%)
}

// ─── Stat Line ────────────────────────────────────────────────────────────────

[System.Serializable]
public struct StatLine
{
    public StatType type;
    [Tooltip("Raw value. For CritRate/CritDamage/MovementSpeed this is percentage points (e.g. 5 = +5%).")]
    public float value;
}

// ─── Item Data ────────────────────────────────────────────────────────────────

/// <summary>
/// Describes one equippable item. Slot, rarity colour, and 3D model are owned by
/// the referenced SubTypeData / RarityData ScriptableObjects — no hardcoded tables here.
/// Create as a ScriptableObject asset (Create → RPG → Items → Item Data) or generated
/// at runtime by ItemGenerator.
/// </summary>
[CreateAssetMenu(fileName = "NewItem", menuName = "RPG/Items/Item Data")]
public class ItemData : ScriptableObject
{
    // ─── Identity ─────────────────────────────────────────────────────────────

    [Header("Identity")]
    public string itemName = "New Item";
    [TextArea(2, 4)] public string description;
    [Tooltip("Optional green flavour text shown at the bottom of the tooltip — use for set names, unique bonuses, or lore. Leave empty for standard items.")]
    [TextArea(1, 3)] public string greenText;
    [Tooltip("Per-item icon override. If null, falls back to SubType.defaultIcon / iconPool.")]
    public Sprite icon;

    [Header("Categorization")]
    [Tooltip("SubType drives equip slot, 3D model, and allowed stats.")]
    public SubTypeData subType;

    [Tooltip("Rarity drives drop weight, glow color, stat-line count, and value multiplier.")]
    public RarityData rarity;

    // ─── Identity (network-stable) ────────────────────────────────────────────

    [SerializeField, HideInInspector] private string assetGuid;
    /// <summary>Stable asset GUID populated from the .meta file in Editor. Empty for runtime-generated items. Use to identify pool entries over the network.</summary>
    public string AssetGuid => assetGuid;

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        string path = UnityEditor.AssetDatabase.GetAssetPath(this);
        if (string.IsNullOrEmpty(path)) return;
        string guid = UnityEditor.AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid) || guid == assetGuid) return;
        assetGuid = guid;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    // ─── Stat Lines ───────────────────────────────────────────────────────────

    [Header("Stat Lines")]
    public List<StatLine> statLines = new();

    // ─── Computed Accessors ───────────────────────────────────────────────────

    /// <summary>EquipmentSlot resolved through SubType. Falls back to Boots if SubType is missing.</summary>
    public EquipmentSlot EquipSlot => subType != null ? subType.equipSlot : EquipmentSlot.Boots;

    /// <summary>Parent MainType resolved through SubType, or null if not configured.</summary>
    public MainTypeData MainType => subType != null ? subType.mainType : null;

    /// <summary>Resolved icon — per-item override, then SubType pool/default, then null.</summary>
    public Sprite ResolvedIcon
    {
        get
        {
            if (icon != null) return icon;
            if (subType != null && subType.defaultIcon != null) return subType.defaultIcon;
            return null;
        }
    }

    // ─── Computed Bonuses ─────────────────────────────────────────────────────

    public int   BonusSTR        => GetInt(StatType.STR) + GetInt(StatType.AllStats);
    public int   BonusAGI        => GetInt(StatType.AGI) + GetInt(StatType.AllStats);
    public int   BonusINT        => GetInt(StatType.INT) + GetInt(StatType.AllStats);
    public int   BonusHP              => GetInt(StatType.FlatHP);
    public int   BonusMana            => GetInt(StatType.FlatMana);
    public float BonusHPRegenPerSecond   => GetFloat(StatType.HPRegenPerSecond);
    public float BonusManaRegenPerSecond => GetFloat(StatType.ManaRegenPerSecond);
    public float BonusCritRate   => GetFloat(StatType.CritRate)   / 100f;
    public float BonusCritDamage   => GetFloat(StatType.CritDamage)   / 100f;
    public int   BonusFireAffinity      => GetInt(StatType.FireAffinity);
    public int   BonusLightningAffinity => GetInt(StatType.LightningAffinity);
    public int   BonusFrostAffinity     => GetInt(StatType.FrostAffinity);
    public float BonusMovementSpeed => GetFloat(StatType.MovementSpeed) / 100f;
    public float BonusSpellPower   => GetFloat(StatType.SpellPower);
    public int   BonusArmor        => GetInt(StatType.Armor);

    // ─── Rarity Colours ───────────────────────────────────────────────────────

    /// <summary>UI color for this item's rarity. White when no rarity is assigned.</summary>
    public Color RarityColor => rarity != null ? rarity.color : Color.white;

    /// <summary>Hex string for use inside TMP rich-text tags. White when no rarity is assigned.</summary>
    public string RarityHex => rarity != null ? rarity.Hex : "FFFFFF";

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private int   GetInt(StatType t)   => Mathf.RoundToInt(GetFloat(t));
    private float GetFloat(StatType t)
    {
        foreach (var line in statLines)
            if (line.type == t) return line.value;
        return 0f;
    }

    /// <summary>Builds a multi-line stat summary for tooltips / UI labels.</summary>
    public string BuildStatSummary()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in statLines)
        {
            bool isPercent  = line.type == StatType.CritRate
                           || line.type == StatType.CritDamage
                           || line.type == StatType.MovementSpeed;
            bool isDecimal  = line.type == StatType.HPRegenPerSecond
                           || line.type == StatType.ManaRegenPerSecond;
            string label = line.type switch
            {
                StatType.STR        => "Strength",
                StatType.AGI        => "Agility",
                StatType.INT        => "Intelligence",
                StatType.FlatHP     => "HP",
                StatType.FlatMana   => "Mana",
                StatType.HPRegenPerSecond   => "HP Regen/s",
                StatType.ManaRegenPerSecond => "Mana Regen/s",
                StatType.AllStats       => "All Stats",
                StatType.CritRate     => "Crit Rate",
                StatType.CritDamage   => "Crit Damage",
                StatType.FireAffinity      => "Fire Affinity",
                StatType.LightningAffinity => "Lightning Affinity",
                StatType.FrostAffinity     => "Frost Affinity",
                StatType.MovementSpeed => "Movement Speed",
                StatType.SpellPower   => "Spell Power",
                StatType.Armor        => "Armor",
                _                     => line.type.ToString(),
            };
            string valStr = isPercent ? $"+{line.value:F0}%" : isDecimal ? $"+{line.value:F1}" : $"+{line.value:F0}";
            sb.AppendLine($"{valStr} {label}");
        }
        return sb.ToString().TrimEnd();
    }
}
