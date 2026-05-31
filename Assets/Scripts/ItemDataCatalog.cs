using System.Collections.Generic;
using UnityEngine;

// ─── Item Data Catalog ──────────────────────────────────────────────────────

/// <summary>
/// Build-time registry of every authored <see cref="ItemData"/> (and WeaponItemData)
/// asset, indexed by <see cref="ItemData.AssetGuid"/>. Lets any client resolve a curated
/// item from its GUID alone — no scene or prefab reference required.
///
/// Why this exists: enemy "ItemPool" drops inject the chosen item into LootPickup.itemPool
/// on the server, but itemPool is a plain field and does NOT replicate. Remote clients
/// therefore arrive with an empty pool; they read the server-synced item GUID and look the
/// real asset up here (see LootPickup.FindItemInPoolByGuid). Because the catalog holds direct
/// asset references, the resolved item keeps every authored field (WeaponItemData.baseDamage,
/// icon, description, …) — unlike the RandomItem serialization path, which only carries
/// subtype / rarity / name / stats and would zero a curated weapon's damage.
///
/// Setup (one click): run menu "RPG/Items/Rebuild Item Catalog". It creates
/// Assets/Resources/ItemDataCatalog.asset if missing and fills it from the project.
/// Re-run it after adding or removing curated item assets.
/// </summary>
[CreateAssetMenu(fileName = "ItemDataCatalog", menuName = "RPG/Items/Item Data Catalog")]
public class ItemDataCatalog : ScriptableObject
{
    [Tooltip("Every authored ItemData / WeaponItemData asset. Populate via 'RPG/Items/Rebuild Item Catalog'.")]
    public List<ItemData> items = new();

    // ─── Runtime Singleton ────────────────────────────────────────────────────
    // Loaded from a Resources folder so it's available on every client in a build,
    // including remote (non-host) clients that have no AssetDatabase.
    private static ItemDataCatalog _instance;
    private static bool _loadAttempted;

    public static ItemDataCatalog Instance
    {
        get
        {
            if (_instance == null && !_loadAttempted)
            {
                _loadAttempted = true;
                _instance = Resources.Load<ItemDataCatalog>("ItemDataCatalog");
                if (_instance == null)
                    Debug.LogWarning("[ItemDataCatalog] No 'ItemDataCatalog' asset found under a Resources folder. " +
                                     "Run menu 'RPG/Items/Rebuild Item Catalog' to create it — curated ItemPool drops " +
                                     "won't resolve on remote clients until then.");
            }
            return _instance;
        }
    }

    // ─── GUID Index ───────────────────────────────────────────────────────────
    private Dictionary<string, ItemData> _byGuid;

    /// <summary>Returns the authored item with this AssetGuid, or null if not catalogued.</summary>
    public ItemData GetByGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        if (_byGuid == null) BuildIndex();
        return _byGuid.TryGetValue(guid, out ItemData item) ? item : null;
    }

    private void BuildIndex()
    {
        _byGuid = new Dictionary<string, ItemData>(items.Count);
        foreach (ItemData item in items)
        {
            if (item == null || string.IsNullOrEmpty(item.AssetGuid)) continue;
            _byGuid[item.AssetGuid] = item; // last wins; AssetGuids are unique in practice
        }
    }

#if UNITY_EDITOR
    // ─── Editor Population ──────────────────────────────────────────────────────

    public const string AssetPath = "Assets/Resources/ItemDataCatalog.asset";

    /// <summary>
    /// Rescans the project for ItemData (incl. WeaponItemData) and replaces the list.
    /// Returns true (and marks the asset dirty) only when the set actually changed, so
    /// no-op imports don't churn git. Does NOT save — the caller decides when.
    /// </summary>
    public bool RebuildFromProject()
    {
        var found = new List<ItemData>();
        foreach (string g in UnityEditor.AssetDatabase.FindAssets("t:ItemData"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
            ItemData item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item != null) found.Add(item);
        }

        if (SameSet(found, items)) return false;

        items   = found;
        _byGuid = null; // force index rebuild on next lookup
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[ItemDataCatalog] Rebuilt with {items.Count} item(s).");
        return true;
    }

    // Order-insensitive reference comparison — avoids rewriting the asset when only the
    // FindAssets ordering differs but the item set is unchanged.
    private static bool SameSet(List<ItemData> a, List<ItemData> b)
    {
        if (a.Count != b.Count) return false;
        var set = new HashSet<ItemData>(b);
        foreach (ItemData x in a)
            if (!set.Contains(x)) return false;
        return true;
    }

    /// <summary>Loads the catalog from its canonical Resources path, creating it if absent.</summary>
    public static ItemDataCatalog GetOrCreateAsset()
    {
        if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Resources"))
            UnityEditor.AssetDatabase.CreateFolder("Assets", "Resources");

        ItemDataCatalog catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDataCatalog>(AssetPath);
        if (catalog == null)
        {
            catalog = CreateInstance<ItemDataCatalog>();
            UnityEditor.AssetDatabase.CreateAsset(catalog, AssetPath);
        }
        return catalog;
    }

    [ContextMenu("Rebuild From Project")]
    private void RebuildFromProjectContext() => RebuildFromProject();

    /// <summary>Manual force-rebuild: finds-or-creates the catalog and repopulates it.</summary>
    [UnityEditor.MenuItem("RPG/Items/Rebuild Item Catalog")]
    private static void RebuildMenu()
    {
        ItemDataCatalog catalog = GetOrCreateAsset();
        catalog.RebuildFromProject();
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.EditorUtility.FocusProjectWindow();
        UnityEditor.Selection.activeObject = catalog;
    }
#endif
}

#if UNITY_EDITOR
// ─── Auto-Rebuild ───────────────────────────────────────────────────────────

/// <summary>
/// Keeps ItemDataCatalog in sync automatically: whenever an ItemData asset is imported,
/// moved, or deleted, the catalog is rebuilt (deferred via delayCall to avoid import
/// reentrancy). It only writes to disk when the item set actually changes, so unrelated
/// imports cause no git churn. The catalog's own re-import never retriggers a rebuild —
/// it is an ItemDataCatalog, not an ItemData, so the type-check below ignores it.
/// </summary>
class ItemDataCatalogPostprocessor : UnityEditor.AssetPostprocessor
{
    private const string ItemDir = "Assets/ScriptableObjects/Item";
    private static bool _pending;

    private static void OnPostprocessAllAssets(
        string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (_pending || !IsRelevant(imported, deleted, moved)) return;
        _pending = true;
        UnityEditor.EditorApplication.delayCall += DoRebuild;
    }

    private static bool IsRelevant(string[] imported, string[] deleted, string[] moved)
    {
        // Imported / moved: type-check so items anywhere in the project are caught.
        foreach (string p in imported)
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(p) != null) return true;
        foreach (string p in moved)
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(p) != null) return true;

        // Deleted: can't load to type-check, so react to .asset removals under the items
        // folder. A missed deletion elsewhere only leaves a harmless null slot that the
        // next rebuild clears.
        foreach (string p in deleted)
            if (p.EndsWith(".asset") && p.Replace('\\', '/').StartsWith(ItemDir)) return true;

        return false;
    }

    private static void DoRebuild()
    {
        _pending = false;
        try
        {
            ItemDataCatalog catalog = ItemDataCatalog.GetOrCreateAsset();
            if (catalog.RebuildFromProject())
                UnityEditor.AssetDatabase.SaveAssets();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ItemDataCatalog] Auto-rebuild failed: {e}");
        }
    }
}
#endif
