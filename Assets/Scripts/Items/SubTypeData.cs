using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A specific item kind under a MainType (Helm, Chest, Sword, Bow, …).
/// Owns the 3D world model, the equip slot, and the stat pool the generator may roll.
/// Create assets in Assets/ScriptableObjects/Items/SubTypes/.
/// </summary>
[CreateAssetMenu(fileName = "NewSubType", menuName = "RPG/Items/Sub Type Data")]
public class SubTypeData : ScriptableObject
{
    // ─── Identity ─────────────────────────────────────────────────────────────

    [Header("Identity")]
    [Tooltip("Display name shown in UI (e.g. \"Helm\", \"Sword\").")]
    public string displayName = "Helm";

    [Tooltip("Parent MainType (Weapon, Armor, …). Required.")]
    public MainTypeData mainType;

    // ─── Equip Behavior ───────────────────────────────────────────────────────

    [Header("Equip Behavior")]
    [Tooltip("Which equipment slot this subtype occupies.")]
    public EquipmentSlot equipSlot = EquipmentSlot.Helm;

    [Tooltip("Optional bone/attach-point name for player rig (e.g. \"RightHand\", \"Head\"). Empty = use slot default.")]
    public string attachBone = "";

    // ─── Visuals ──────────────────────────────────────────────────────────────

    [Header("Visuals")]
    [Tooltip("3D model prefab shown on ground pickups and (eventually) attached to the player on equip.")]
    public GameObject worldModelPrefab;

    [Tooltip("Default 2D icon used by inventory UI when no per-item icon is set.")]
    public Sprite defaultIcon;

    [Tooltip("Pool of icons the generator may pick from. Falls back to defaultIcon if empty.")]
    public List<Sprite> iconPool = new();

    // ─── Stat Pool ────────────────────────────────────────────────────────────

    [Header("Stat Pool")]
    [Tooltip("Stat types the generator is allowed to roll on items of this subtype. " +
             "Empty = all stats allowed (e.g. armor accepts everything).")]
    public List<StatType> allowedStats = new();

    [Tooltip("Stats exclusive to this subtype. Other subtypes cannot roll them, " +
             "and this subtype can roll them even if not in allowedStats. " +
             "Example: put MovementSpeed in Boots.reservedStats so only Boots roll it.")]
    public List<StatType> reservedStats = new();

    [Tooltip("Optional name prefixes/material parts the generator may use when building item names.")]
    public List<string> nameMaterials = new() { "Iron", "Steel", "Shadow", "Storm", "Mystic", "Dragon" };

    // ─── Identity (network-stable) ────────────────────────────────────────────

    [SerializeField, HideInInspector] private string assetGuid;
    /// <summary>Stable asset GUID populated from the .meta file in Editor. Survives inspector reordering — use to identify this SO over the network.</summary>
    public string AssetGuid => assetGuid;

#if UNITY_EDITOR
    private void OnValidate()
    {
        string path = UnityEditor.AssetDatabase.GetAssetPath(this);
        if (string.IsNullOrEmpty(path)) return;
        string guid = UnityEditor.AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid) || guid == assetGuid) return;
        assetGuid = guid;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>True when this subtype has no stat filter (everything is allowed).</summary>
    public bool AllowsAnyStat => allowedStats == null || allowedStats.Count == 0;

    /// <summary>True when this subtype permits the given stat (or has no filter at all).</summary>
    public bool Allows(StatType stat) => AllowsAnyStat || allowedStats.Contains(stat);

    /// <summary>Picks a random icon from iconPool, or returns defaultIcon when the pool is empty.</summary>
    public Sprite PickIcon()
    {
        if (iconPool != null && iconPool.Count > 0)
            return iconPool[Random.Range(0, iconPool.Count)];
        return defaultIcon;
    }
}
