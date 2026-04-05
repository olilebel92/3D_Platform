// Editor-only — must live inside an "Editor" folder.
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates six fully-configured BiomeDefinition assets:
///   Swamp · Plains · Desert · Forest · Hills · Mountain
///
/// Run via:  Tools > Terrain Generator > Create Biome Library
///
/// After running, assign TerrainLayers to each asset in the Inspector.
/// Suggested layer assignments are printed to the Console.
/// </summary>
public static class BiomeLibrarySetup
{
    private const string OutputDir   = "Assets/ScriptableObjects/Biomes";

    // Terrain height used as the shared amplitude base.
    // Must match TerrainData.size.y in your scene.
    private const float TerrainHeight = 300f;

    // ─── Color map keys ──────────────────────────────────────────────────────
    // Paint these exact colors in your color-map PNG to assign each biome.
    public static readonly Color32 ColSwamp    = new Color32( 60, 100,  50, 255); // dark olive
    public static readonly Color32 ColPlains   = new Color32(180, 210,  90, 255); // golden green
    public static readonly Color32 ColDesert   = new Color32(220, 190, 110, 255); // sandy yellow
    public static readonly Color32 ColForest   = new Color32( 30, 120,  40, 255); // dark green
    public static readonly Color32 ColHills    = new Color32(100, 135,  60, 255); // olive brown
    public static readonly Color32 ColMountain = new Color32(140, 140, 140, 255); // grey rock

    [MenuItem("Tools/Terrain Generator/Create Biome Library (6 Biomes)")]
    public static void Create()
    {
        EnsureDir(OutputDir);

        var swamp    = CreateSwamp();
        var plains   = CreatePlains();
        var desert   = CreateDesert();
        var forest   = CreateForest();
        var hills    = CreateHills();
        var mountain = CreateMountain();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorGUIUtility.PingObject(swamp);

        Debug.Log(
            "[BiomeLibrarySetup] Six biome assets created in " + OutputDir + "\n\n" +
            "━━━ Suggested TerrainLayer assignments ━━━\n" +
            "Swamp    → [0] Muddy ground / wet soil   [1] Dark moss\n" +
            "Plains   → [0] Short grass                [1] Dry grass (slope)\n" +
            "Desert   → [0] Sand                       [1] Cracked sandstone (slope)\n" +
            "Forest   → [0] Forest floor / leaves      [1] Rock (slope)\n" +
            "Hills    → [0] Grass / meadow             [1] Rocky ground (slope)\n" +
            "Mountain → [0] Bare rock                  [1] Snow / ice (slope)\n\n" +
            "━━━ Color-map keys (paint these in your PNG) ━━━\n" +
            $"Swamp    → RGB({ColSwamp.r}, {ColSwamp.g}, {ColSwamp.b})\n" +
            $"Plains   → RGB({ColPlains.r}, {ColPlains.g}, {ColPlains.b})\n" +
            $"Desert   → RGB({ColDesert.r}, {ColDesert.g}, {ColDesert.b})\n" +
            $"Forest   → RGB({ColForest.r}, {ColForest.g}, {ColForest.b})\n" +
            $"Hills    → RGB({ColHills.r}, {ColHills.g}, {ColHills.b})\n" +
            $"Mountain → RGB({ColMountain.r}, {ColMountain.g}, {ColMountain.b})"
        );
    }

    // ─── Swamp ───────────────────────────────────────────────────────────────
    // Near-flat wetland, just above water level. Barely any vertical relief.
    // Feels oppressive and hard to navigate — everything looks the same height.
    private static BiomeDefinition CreateSwamp()
    {
        var b = Make("Swamp", ColSwamp);

        // World height range: ~3 m – 15 m  (nearly at sea level)
        b.heightMin        = 0.01f;
        b.heightMax        = 0.05f;
        b.noiseAmplitude   = TerrainHeight;

        // Low frequency → large patches; few octaves → smooth, no detail (intentionally oppressive)
        b.noiseFrequency   = 0.008f;
        b.noiseOctaves     = 3;
        b.noisePersistence = 0.35f;
        b.noiseLacunarity  = 1.8f;

        b.slopeLayerThreshold = 0.12f;
        b.blendRadius      = 20f;
        b.moisture         = 1.0f;
        b.temperature      = 0.55f;

        b.terrainLayers = new[] { MakeLayer("Swamp", new Color32( 80, 100,  60, 255)) };
        Save(b, "Biome_Swamp");
        return b;
    }

    // ─── Plains ──────────────────────────────────────────────────────────────
    // Open grassland. Gently rolling terrain with visible bumps at ground level.
    private static BiomeDefinition CreatePlains()
    {
        var b = Make("Plains", ColPlains);

        // World height range: ~6 m – 66 m  (overlaps Forest bottom)
        b.heightMin        = 0.02f;
        b.heightMax        = 0.22f;
        b.noiseAmplitude   = TerrainHeight;

        b.noiseFrequency   = 0.003f;
        b.noiseOctaves     = 3;
        b.noisePersistence = 0.38f;
        b.noiseLacunarity  = 2.0f;

        b.slopeLayerThreshold = 0.22f;
        b.blendRadius      = 42f;
        b.moisture         = 0.45f;
        b.temperature      = 0.55f;

        b.terrainLayers = new[] { MakeLayer("Plains", new Color32(180, 200,  90, 255)) };
        Save(b, "Biome_Plains");
        return b;
    }

    // ─── Desert ──────────────────────────────────────────────────────────────
    // Sandy dunes with regular ridges. Higher frequency than plains gives the
    // characteristic repeating dune pattern; low persistence keeps them smooth.
    private static BiomeDefinition CreateDesert()
    {
        var b = Make("Desert", ColDesert);

        // World height range: ~12 m – 72 m  (overlaps Plains top and Forest bottom)
        b.heightMin        = 0.04f;
        b.heightMax        = 0.24f;
        b.noiseAmplitude   = TerrainHeight;

        b.noiseFrequency   = 0.004f;
        b.noiseOctaves     = 3;
        b.noisePersistence = 0.38f;
        b.noiseLacunarity  = 2.2f;

        b.slopeLayerThreshold = 0.30f;
        b.blendRadius      = 35f;
        b.moisture         = 0.05f;
        b.temperature      = 0.90f;

        b.terrainLayers = new[] { MakeLayer("Desert", new Color32(220, 190, 110, 255)) };
        Save(b, "Biome_Desert");
        return b;
    }

    // ─── Forest ──────────────────────────────────────────────────────────────
    // Rolling wooded hills. Clearly higher than plains.
    // High persistence gives rough ground — roots, embankments, gullies.
    private static BiomeDefinition CreateForest()
    {
        var b = Make("Forest", ColForest);

        // World height range: ~18 m – 108 m  (overlaps Plains top, overlaps Hills bottom)
        b.heightMin        = 0.06f;
        b.heightMax        = 0.36f;
        b.noiseAmplitude   = TerrainHeight;

        b.noiseFrequency   = 0.003f;
        b.noiseOctaves     = 4;
        b.noisePersistence = 0.40f;
        b.noiseLacunarity  = 2.1f;

        b.slopeLayerThreshold = 0.38f;
        b.blendRadius      = 28f;
        b.moisture         = 0.70f;
        b.temperature      = 0.45f;

        b.terrainLayers = new[] { MakeLayer("Forest", new Color32( 40, 120,  40, 255)) };
        Save(b, "Biome_Forest");
        return b;
    }

    // ─── Hills ───────────────────────────────────────────────────────────────
    // Significant terrain relief with rugged ridgelines.
    // High frequency + high persistence → sharp, uneven summit profiles.
    private static BiomeDefinition CreateHills()
    {
        var b = Make("Hills", ColHills);

        // World height range: ~60 m – 165 m  (overlaps Forest top, overlaps Mountain bottom)
        b.heightMin        = 0.20f;
        b.heightMax        = 0.55f;
        b.noiseAmplitude   = TerrainHeight;

        b.noiseFrequency   = 0.004f;
        b.noiseOctaves     = 4;
        b.noisePersistence = 0.45f;
        b.noiseLacunarity  = 2.0f;

        b.slopeLayerThreshold = 0.35f;
        b.blendRadius      = 22f;
        b.moisture         = 0.40f;
        b.temperature      = 0.35f;

        b.terrainLayers = new[] { MakeLayer("Hills", new Color32(120, 140,  70, 255)) };
        Save(b, "Biome_Hills");
        return b;
    }

    // ─── Mountain ────────────────────────────────────────────────────────────
    // Impassable alpine peaks. Floor (0.75) is above Hills ceiling (0.75) —
    // always higher than any hill. Six high-persistence octaves → jagged crags.
    private static BiomeDefinition CreateMountain()
    {
        var b = Make("Mountain", ColMountain);

        // World height range: ~135 m – 270 m  (overlaps Hills top → seamless ridge line)
        b.heightMin        = 0.45f;
        b.heightMax        = 0.90f;
        b.noiseAmplitude   = TerrainHeight;

        b.noiseFrequency   = 0.005f;
        b.noiseOctaves     = 5;
        b.noisePersistence = 0.50f;
        b.noiseLacunarity  = 2.3f;

        b.slopeLayerThreshold = 0.18f;
        b.blendRadius      = 16f;
        b.moisture         = 0.20f;
        b.temperature      = 0.05f;

        b.terrainLayers = new[] { MakeLayer("Mountain", new Color32(160, 150, 140, 255)) };
        Save(b, "Biome_Mountain");
        return b;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static BiomeDefinition Make(string name, Color32 col)
    {
        var b = ScriptableObject.CreateInstance<BiomeDefinition>();
        b.biomeName = name;
        b.mapColor  = new Color(col.r / 255f, col.g / 255f, col.b / 255f, 1f);
        return b;
    }

    private static void Save(BiomeDefinition b, string filename)
    {
        AssetDatabase.CreateAsset(b, $"{OutputDir}/{filename}.asset");
    }

    /// <summary>
    /// Creates a TerrainLayer asset with a solid 64×64 colour texture.
    /// The texture is saved alongside the layer in OutputDir/Layers/.
    /// </summary>
    private static TerrainLayer MakeLayer(string layerName, Color32 colour)
    {
        string layerDir = OutputDir + "/Layers";
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "..", layerDir));

        // ── solid-colour texture ──────────────────────────────────────────────
        const int Res = 64;
        var tex = new Texture2D(Res, Res, TextureFormat.RGB24, false);
        var pixels = new Color32[Res * Res];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = colour;
        tex.SetPixels32(pixels);
        tex.Apply();

        string texPath = $"{layerDir}/{layerName}_Diffuse.png";
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", texPath),
                           tex.EncodeToPNG());
        AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);

        // Make the texture readable + uncompressed so Unity doesn't alter colours
        var imp = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (imp != null)
        {
            imp.isReadable         = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.sRGBTexture        = true;
            imp.mipmapEnabled      = false;
            imp.SaveAndReimport();
        }

        var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        // ── TerrainLayer asset ────────────────────────────────────────────────
        var layer = new TerrainLayer
        {
            diffuseTexture = diffuse,
            tileSize       = new Vector2(8f, 8f),   // tile every 8 m for a clean solid look
            tileOffset     = Vector2.zero
        };

        string layerPath = $"{layerDir}/{layerName}.terrainlayer";
        AssetDatabase.CreateAsset(layer, layerPath);
        return layer;
    }

    private static void EnsureDir(string unityPath)
    {
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "..", unityPath));
        AssetDatabase.Refresh();
    }
}
#endif
