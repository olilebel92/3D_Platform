using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reads Assets/Data/Items.csv and creates or updates ItemData ScriptableObject assets
/// under Assets/ScriptableObjects/Item/.
/// Usage: Tools > Sync Items From CSV
/// </summary>
public static class ItemCsvImporter
{
    private const string CsvPath       = "Assets/Data/Items.csv";
    private const string OutputFolder  = "Assets/ScriptableObjects/Item";

    [MenuItem("Tools/Sync Items From CSV")]
    public static void SyncItemsFromCsv()
    {
        if (!File.Exists(CsvPath))
        {
            EditorUtility.DisplayDialog("Item CSV Importer",
                $"CSV not found at {CsvPath}.\nExport the Items sheet from Items.xlsx as CSV there first.", "OK");
            return;
        }

        string[] lines = File.ReadAllLines(CsvPath);
        if (lines.Length < 2)
        {
            Debug.LogWarning("[ItemCsvImporter] CSV has no data rows.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder("Assets/ScriptableObjects", "Item");

        int created = 0, updated = 0, skipped = 0;

        // Skip header (row 0), skip blank / note rows
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] cols = ParseCsvLine(line);
            if (cols.Length < 5 || string.IsNullOrEmpty(cols[0])) continue;

            // ── Column mapping ────────────────────────────────────────────
            // 0  AssetFileName
            // 1  itemName
            // 2  description
            // 3  slot
            // 4  rarity
            // 5  STR  6 AGI  7 INT  8 FlatHP  9 HPRegenPerSecond  10 AllStats
            // 11 CritRate  12 CritDamage  13 FireDamage  14 MovementSpeed  15 SpellPower

            string assetFileName = cols[0].Trim();
            string assetPath     = $"{OutputFolder}/{assetFileName}.asset";

            ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(assetPath);
            bool isNew = item == null;
            if (isNew)
            {
                item = ScriptableObject.CreateInstance<ItemData>();
                created++;
            }
            else
            {
                updated++;
            }

            item.itemName    = GetString(cols, 1);
            item.description = GetString(cols, 2);
            item.subType     = LookupSubType(GetString(cols, 3), assetFileName);
            item.rarity      = LookupRarity(GetString(cols, 4), assetFileName);

            item.statLines.Clear();
            TryAddStat(item, StatType.STR,            cols, 5);
            TryAddStat(item, StatType.AGI,            cols, 6);
            TryAddStat(item, StatType.INT,            cols, 7);
            TryAddStat(item, StatType.FlatHP,         cols, 8);
            TryAddStat(item, StatType.HPRegenPerSecond, cols, 9);
            TryAddStat(item, StatType.AllStats,       cols, 10);
            TryAddStat(item, StatType.CritRate,       cols, 11);
            TryAddStat(item, StatType.CritDamage,     cols, 12);
            TryAddStat(item, StatType.FireDamage,     cols, 13);
            TryAddStat(item, StatType.MovementSpeed,  cols, 14);
            TryAddStat(item, StatType.SpellPower,     cols, 15);

            if (isNew)
                AssetDatabase.CreateAsset(item, assetPath);
            else
                EditorUtility.SetDirty(item);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ItemCsvImporter] Done — {created} created, {updated} updated, {skipped} skipped.");
        EditorUtility.DisplayDialog("Item CSV Importer",
            $"Sync complete.\n\nCreated: {created}\nUpdated: {updated}", "OK");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static void TryAddStat(ItemData item, StatType type, string[] cols, int colIndex)
    {
        if (colIndex >= cols.Length) return;
        string raw = cols[colIndex].Trim();
        if (string.IsNullOrEmpty(raw)) return;
        if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float val)) return;
        item.statLines.Add(new StatLine { type = type, value = val });
    }

    private static string GetString(string[] cols, int index)
        => index < cols.Length ? cols[index].Trim() : string.Empty;

    private static SubTypeData LookupSubType(string s, string assetName)
    {
        if (string.IsNullOrEmpty(s)) return null;
        string[] guids = AssetDatabase.FindAssets("t:SubTypeData");
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            SubTypeData st = AssetDatabase.LoadAssetAtPath<SubTypeData>(path);
            if (st == null) continue;
            if (string.Equals(st.displayName, s, StringComparison.OrdinalIgnoreCase)) return st;
            if (string.Equals(st.name, s, StringComparison.OrdinalIgnoreCase)) return st;
        }
        Debug.LogWarning($"[ItemCsvImporter] '{assetName}' — no SubTypeData named '{s}', leaving subType null.");
        return null;
    }

    private static RarityData LookupRarity(string s, string assetName)
    {
        if (string.IsNullOrEmpty(s)) return null;
        string[] guids = AssetDatabase.FindAssets("t:RarityData");
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            RarityData r = AssetDatabase.LoadAssetAtPath<RarityData>(path);
            if (r == null) continue;
            if (string.Equals(r.displayName, s, StringComparison.OrdinalIgnoreCase)) return r;
            if (string.Equals(r.name, s, StringComparison.OrdinalIgnoreCase)) return r;
        }
        Debug.LogWarning($"[ItemCsvImporter] '{assetName}' — no RarityData named '{s}', leaving rarity null.");
        return null;
    }

    /// <summary>Handles quoted fields with commas and escaped quotes.</summary>
    private static string[] ParseCsvLine(string line)
    {
        var fields = new System.Collections.Generic.List<string>();
        int pos = 0;
        while (pos <= line.Length)
        {
            if (pos < line.Length && line[pos] == '"')
            {
                pos++;
                int start = pos;
                var sb = new System.Text.StringBuilder();
                while (pos < line.Length)
                {
                    if (line[pos] == '"')
                    {
                        if (pos + 1 < line.Length && line[pos + 1] == '"') { sb.Append('"'); pos += 2; }
                        else { pos++; break; }
                    }
                    else { sb.Append(line[pos++]); }
                }
                fields.Add(sb.ToString());
                if (pos < line.Length && line[pos] == ',') pos++;
            }
            else
            {
                int comma = line.IndexOf(',', pos);
                if (comma == -1) { fields.Add(line.Substring(pos)); break; }
                fields.Add(line.Substring(pos, comma - pos));
                pos = comma + 1;
            }
        }
        return fields.ToArray();
    }
}
