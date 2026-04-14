using System.Collections.Generic;
using UnityEngine;

// ─── Enums ────────────────────────────────────────────────────────────────────

/// <summary>Equipment slots available. Add new entries here as slots are added.</summary>
public enum EquipmentSlot { Boots, Helm, Pants, Chest }

/// <summary>Item rarity tier — controls stat count, values, and UI colour.</summary>
public enum ItemRarity { Normal, Uncommon, Rare, Epic, Legendary, Godly }

/// <summary>All stat types a generated item can roll.</summary>
public enum StatType
{
    STR,
    AGI,
    INT,
    FlatHP,          // flat bonus hit points (available from Normal rarity)
    RegenPerSecond,  // HP regeneration per second (Uncommon+ only)
    AllStats,        // adds to STR, AGI, and INT simultaneously (Rare+ only)
    CritRate,    // stored as whole percentage points  (5 → +5 % crit rate)
    CritDamage,  // stored as whole percentage points  (25 → +25 % crit dmg)
    FireDamage,  // flat bonus fire damage added to fire spells
    MovementSpeed, // stored as whole percentage points  (20 → +20 % move speed)
    SpellPower,  // flat bonus added to all spell damage
}

// ─── Stat Line ────────────────────────────────────────────────────────────────

[System.Serializable]
public struct StatLine
{
    public StatType type;
    [Tooltip("Raw value. For CritRate/CritDamage this is percentage points (e.g. 5 = +5%).")]
    public float value;
}

// ─── Item Data ────────────────────────────────────────────────────────────────

/// <summary>
/// Describes one equippable item. Can be created as a ScriptableObject asset
/// (right-click → Create → RPG → Item Data) or generated at runtime by ItemGenerator.
/// </summary>
[CreateAssetMenu(fileName = "NewItem", menuName = "RPG/Item Data")]
public class ItemData : ScriptableObject
{
    // ─── Identity ─────────────────────────────────────────────────────────────

    [Header("Identity")]
    public string itemName = "New Item";
    [TextArea(2, 4)] public string description;
    public Sprite icon;
    public EquipmentSlot slot   = EquipmentSlot.Boots;
    public ItemRarity    rarity = ItemRarity.Normal;

    // ─── Stat Lines ───────────────────────────────────────────────────────────

    [Header("Stat Lines")]
    public List<StatLine> statLines = new();

    // ─── Computed Bonuses ─────────────────────────────────────────────────────

    /// <summary>AllStats contributes to each of the three primary stats.</summary>
    public int   BonusSTR        => GetInt(StatType.STR) + GetInt(StatType.AllStats);
    public int   BonusAGI        => GetInt(StatType.AGI) + GetInt(StatType.AllStats);
    public int   BonusINT        => GetInt(StatType.INT) + GetInt(StatType.AllStats);
    /// <summary>Flat bonus hit points added to max HP.</summary>
    public int   BonusHP              => GetInt(StatType.FlatHP);
    /// <summary>HP regenerated per second from equipment.</summary>
    public float BonusRegenPerSecond  => GetFloat(StatType.RegenPerSecond);
    /// <summary>Crit rate bonus as a 0-1 fraction (e.g. 0.05 = +5%).</summary>
    public float BonusCritRate   => GetFloat(StatType.CritRate)   / 100f;
    /// <summary>Crit damage bonus as a 0-1 fraction (e.g. 0.25 = +25%).</summary>
    public float BonusCritDamage   => GetFloat(StatType.CritDamage)   / 100f;
    /// <summary>Flat fire damage bonus added to fire spells.</summary>
    public float BonusFireDamage   => GetFloat(StatType.FireDamage);
    /// <summary>Movement speed bonus as a 0-1 fraction (e.g. 0.20 = +20%).</summary>
    public float BonusMovementSpeed => GetFloat(StatType.MovementSpeed) / 100f;
    /// <summary>Flat spell power bonus added to all spell damage.</summary>
    public float BonusSpellPower   => GetFloat(StatType.SpellPower);

    // ─── Rarity Colours ───────────────────────────────────────────────────────

    /// <summary>Unity Color matching this item's rarity.</summary>
    public Color RarityColor => rarity switch
    {
        ItemRarity.Uncommon  => new Color(0.12f, 1.00f, 0.00f),   // Green
        ItemRarity.Rare      => new Color(0.00f, 0.44f, 0.87f),   // Blue
        ItemRarity.Epic      => new Color(0.64f, 0.21f, 0.93f),   // Purple
        ItemRarity.Legendary => new Color(1.00f, 0.50f, 0.00f),   // Orange
        ItemRarity.Godly     => new Color(1.00f, 0.10f, 0.10f),   // Red
        _                    => Color.white,                        // Normal = White
    };

    /// <summary>Hex colour string for use inside TMP rich-text tags.</summary>
    public string RarityHex => rarity switch
    {
        ItemRarity.Uncommon  => "1EFF00",
        ItemRarity.Rare      => "0070DD",
        ItemRarity.Epic      => "A335EE",
        ItemRarity.Legendary => "FF8000",
        ItemRarity.Godly     => "FF1A1A",
        _                    => "FFFFFF",
    };

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
            bool isDecimal  = line.type == StatType.RegenPerSecond;
            string label = line.type switch
            {
                StatType.STR        => "Strength",
                StatType.AGI        => "Agility",
                StatType.INT        => "Intelligence",
                StatType.FlatHP     => "HP",
                StatType.RegenPerSecond => "HP Regen/s",
                StatType.AllStats       => "All Stats",
                StatType.CritRate     => "Crit Rate",
                StatType.CritDamage   => "Crit Damage",
                StatType.FireDamage   => "Fire Damage",
                StatType.MovementSpeed => "Movement Speed",
                StatType.SpellPower   => "Spell Power",
                _                     => line.type.ToString(),
            };
            string valStr = isPercent ? $"+{line.value:F0}%" : isDecimal ? $"+{line.value:F1}" : $"+{line.value:F0}";
            sb.AppendLine($"{valStr} {label}");
        }
        return sb.ToString().TrimEnd();
    }
}
