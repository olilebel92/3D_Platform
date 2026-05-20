using UnityEngine;

/// <summary>
/// Defines one rarity tier — color, glow visuals, drop weight, and stat-roll ranges.
/// Create assets in Assets/ScriptableObjects/Items/Rarities/.
/// Add a new rarity by creating a new asset; no code changes required.
/// </summary>
[CreateAssetMenu(fileName = "NewRarity", menuName = "RPG/Items/Rarity Data")]
public class RarityData : ScriptableObject
{
    // ─── Identity ─────────────────────────────────────────────────────────────

    [Header("Identity")]
    [Tooltip("Display name shown in UI (e.g. \"Legendary\").")]
    public string displayName = "Normal";

    [Tooltip("Sort order — lower = more common. Used by filter dropdowns and 'is higher than' comparisons.")]
    public int sortOrder = 0;

    // ─── Visuals ──────────────────────────────────────────────────────────────

    [Header("Colors")]
    [Tooltip("Primary color for nameplate / rarity badge / UI tint.")]
    public Color color = Color.white;

    [Tooltip("Glow color applied to world pickups (emissive + particles). Often same as color.")]
    [ColorUsage(true, true)]
    public Color glowColor = Color.white;

    [Tooltip("Emissive multiplier for the world pickup glow. 1 = same as glowColor, higher = HDR bloom.")]
    public float glowIntensity = 1f;

    [Tooltip("Optional particle prefab spawned on top of pickups of this rarity. Leave null to skip.")]
    public GameObject particlePrefab;

    // ─── Drop Tuning ──────────────────────────────────────────────────────────

    [Header("Drop Tuning")]
    [Tooltip("Relative drop weight (higher = more common).")]
    public float dropWeight = 10f;

    [Tooltip("Earliest wave this rarity can drop. Anything before this is treated as weight 0.")]
    public int waveUnlockThreshold = 1;

    // ─── Stat Rolls ───────────────────────────────────────────────────────────

    [Header("Stat Rolls")]
    [Tooltip("Minimum number of stat lines rolled on items of this rarity.")]
    public int statLineCountMin = 1;

    [Tooltip("Maximum number of stat lines rolled on items of this rarity.")]
    public int statLineCountMax = 2;

    [Tooltip("Multiplier applied to all rolled stat values. Higher rarities should use 1.5x–3x.")]
    public float statValueMultiplier = 1f;

    [Tooltip("Stats that should NEVER roll on items of this rarity. Empty = no exclusions.")]
    public System.Collections.Generic.List<StatType> bannedStats = new();

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

    // ─── Comparison ───────────────────────────────────────────────────────────

    public int CompareTo(RarityData other) => other == null ? 1 : sortOrder.CompareTo(other.sortOrder);

    public string Hex => ColorUtility.ToHtmlStringRGB(color);
}
