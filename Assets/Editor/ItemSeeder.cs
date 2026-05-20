using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click seeders for the new SO-based item system.
///
///   Tools → Seed Items → Armor (MainType + 4 SubTypes)
///
/// Idempotent — existing assets are left alone, missing ones are created.
/// After seeding, drag the resulting SubTypeData assets into
/// ItemGenerator.subTypes so the generator can roll armor.
/// </summary>
public static class ItemSeeder
{
    private const string RootFolder       = "Assets/ScriptableObjects/Items";
    private const string MainTypesFolder  = RootFolder + "/MainTypes";
    private const string SubTypesFolder   = RootFolder + "/SubTypes";

    [MenuItem("Tools/Seed Items/Armor (MainType + 4 SubTypes)")]
    public static void SeedArmor()
    {
        EnsureFolder(RootFolder);
        EnsureFolder(MainTypesFolder);
        EnsureFolder(SubTypesFolder);

        // ── MainType ─────────────────────────────────────────────────────────
        MainTypeData armor = LoadOrCreate<MainTypeData>(
            $"{MainTypesFolder}/Armor.asset",
            data =>
            {
                data.displayName = "Armor";
                data.isWeapon    = false;
            });

        // ── SubTypes ─────────────────────────────────────────────────────────
        CreateArmorSubType("Helm",  EquipmentSlot.Helm,  armor);
        CreateArmorSubType("Chest", EquipmentSlot.Chest, armor);
        CreateArmorSubType("Pants", EquipmentSlot.Pants, armor);
        CreateArmorSubType("Boots", EquipmentSlot.Boots, armor);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Item Seeder",
            "Armor MainType + 4 SubType assets are ready under:\n\n" +
            $"  {MainTypesFolder}\n  {SubTypesFolder}\n\n" +
            "Next: drag the 4 SubTypes into ItemGenerator.subTypes in the Inspector. " +
            "Assign a worldModelPrefab per SubType when you have meshes ready.",
            "OK");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static void CreateArmorSubType(string subtypeName, EquipmentSlot slot, MainTypeData mainType)
    {
        string path = $"{SubTypesFolder}/{subtypeName}.asset";
        LoadOrCreate<SubTypeData>(path, data =>
        {
            data.displayName = subtypeName;
            data.mainType    = mainType;
            data.equipSlot   = slot;
            // worldModelPrefab and defaultIcon are left null — designer assigns in Inspector.
            // allowedStats stays empty → all stats permitted on armor.
            data.nameMaterials = new System.Collections.Generic.List<string>
                { "Leather", "Iron", "Steel", "Shadow", "Storm", "Mystic", "Dragon" };
        });
    }

    /// <summary>
    /// Loads the asset at the given path if it exists; otherwise creates a fresh one
    /// and runs the configure callback before saving.
    /// </summary>
    private static T LoadOrCreate<T>(string path, System.Action<T> configure) where T : ScriptableObject
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null)
        {
            Debug.Log($"[ItemSeeder] {path} already exists — left untouched.");
            return existing;
        }

        T asset = ScriptableObject.CreateInstance<T>();
        configure?.Invoke(asset);
        AssetDatabase.CreateAsset(asset, path);
        Debug.Log($"[ItemSeeder] Created {path}.");
        return asset;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf   = Path.GetFileName(folder);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, leaf);
    }
}
