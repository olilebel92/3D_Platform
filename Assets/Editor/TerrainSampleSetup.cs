// Editor-only — must live inside an "Editor" folder.
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click generator that builds a complete 500×500 sample scene:
///   • Forest biome (left ~45%)
///   • Plains biome (right ~55%)
///   • Winding river carved as a CanyonDefinition
///   • A dirt road crossing west-to-east
///
/// Run via:  Tools > Terrain Generator > Create Sample Scene
/// </summary>
public static class TerrainSampleSetup
{
    private const string OutputDir  = "Assets/ScriptableObjects/TerrainSample";
    private const float  TerrainSize = 500f;   // world units X and Z
    private const float  TerrainHeight = 120f; // world units Y (max possible height)

    // ─── Colors used on the color map ────────────────────────────────────────
    private static readonly Color32 ColForest = new Color32(30,  120,  40,  255);
    private static readonly Color32 ColPlains = new Color32(180, 200,  90,  255);

    // ─── Menu entry ──────────────────────────────────────────────────────────

    [MenuItem("Tools/Terrain Generator/Create Sample Scene (Forest + Plains + River + Road)")]
    public static void Create()
    {
        EnsureDir(OutputDir);

        // 1. Assets
        var colorMap = GenerateColorMap();
        var forest   = CreateForestBiome();
        var plains   = CreatePlainsBiome();
        var settings = CreateSettings(colorMap, forest, plains);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 2. Configure terrain size if one is active in the scene
        ConfigureActiveTerrain();

        // 3. Done
        EditorGUIUtility.PingObject(settings);
        Debug.Log(
            "[TerrainSampleSetup] Done!\n" +
            "Next steps:\n" +
            "  1. Open Tools > Terrain Generator\n" +
            "  2. Assign the SampleSettings asset and your Terrain\n" +
            "  3. Assign TerrainLayers to each BiomeDefinition (Forest / Plains)\n" +
            "  4. Hit ⚡ Regenerate All\n" +
            $"Asset location: {OutputDir}/"
        );
    }

    // ─── Color map (512×512) ──────────────────────────────────────────────────

    /// <summary>
    /// Generates a 512×512 color map texture:
    ///   • Left  ~45% → Forest  (dark green, organic wavy border)
    ///   • Right ~55% → Plains  (golden green)
    /// The border uses layered Perlin noise for a natural edge.
    /// </summary>
    private static Texture2D GenerateColorMap()
    {
        const int W = 512, H = 512;
        var pixels = new Color32[W * H];

        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float u = (float)x / W;
                float v = (float)y / H;

                // Organic split boundary using two octaves of Perlin noise
                float n1 = Mathf.PerlinNoise(u * 2.1f + 7f, v * 2.8f + 3f);       // large waves
                float n2 = Mathf.PerlinNoise(u * 5.3f + 11f, v * 6.1f + 17f) * 0.35f; // detail
                float split = 0.38f + (n1 + n2) * 0.14f;

                pixels[y * W + x] = u < split ? ColForest : ColPlains;
            }
        }

        // Carve a subtle river-hint channel (purely cosmetic on the map)
        // The real river is carved by the CanyonDefinition in the settings.
        PaintRiverChannel(pixels, W, H);

        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.SetPixels32(pixels);
        tex.Apply();

        // Save PNG
        string pngPath = $"{OutputDir}/SampleColorMap.png";
        File.WriteAllBytes(AbsPath(pngPath), tex.EncodeToPNG());
        AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);

        // Make it readable & uncompressed so GetPixels32() works at generation time
        var imp = (TextureImporter)AssetImporter.GetAtPath(pngPath);
        imp.isReadable           = true;
        imp.textureCompression   = TextureImporterCompression.Uncompressed;
        imp.mipmapEnabled        = false;
        imp.filterMode           = FilterMode.Bilinear;
        imp.SaveAndReimport();

        Debug.Log($"[TerrainSampleSetup] Color map saved → {pngPath}");
        return AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
    }

    /// <summary>Paints a narrow plains-colored stripe to hint at the river channel on the map.</summary>
    private static void PaintRiverChannel(Color32[] pixels, int W, int H)
    {
        // River follows a gentle S-curve at roughly x = 50-55% of the terrain width
        // expressed in color-map pixel space
        const int riverWidthPx = 6;

        for (int y = 0; y < H; y++)
        {
            float v     = (float)y / H;
            float rxNorm = 0.51f
                + Mathf.Sin(v * Mathf.PI * 1.8f) * 0.04f   // large S-bend
                + Mathf.Sin(v * Mathf.PI * 4.5f) * 0.015f; // minor meander
            int rx = Mathf.RoundToInt(rxNorm * W);

            for (int dx = -riverWidthPx; dx <= riverWidthPx; dx++)
            {
                int px = rx + dx;
                if (px < 0 || px >= W) continue;
                // soft edge
                float t = 1f - (float)Mathf.Abs(dx) / riverWidthPx;
                Color32 cur = pixels[y * W + px];
                // blend toward a slightly different plains tint to hint river
                cur.r = (byte)Mathf.RoundToInt(Mathf.Lerp(cur.r, 160, t * 0.4f));
                cur.g = (byte)Mathf.RoundToInt(Mathf.Lerp(cur.g, 195, t * 0.4f));
                cur.b = (byte)Mathf.RoundToInt(Mathf.Lerp(cur.b, 100, t * 0.4f));
                pixels[y * W + px] = cur;
            }
        }
    }

    // ─── Biome definitions ────────────────────────────────────────────────────

    private static BiomeDefinition CreateForestBiome()
    {
        var b = ScriptableObject.CreateInstance<BiomeDefinition>();
        b.biomeName         = "Forest";
        b.mapColor          = ToColor(ColForest);

        // Height: rolling forested hills — 18m to 48m above terrain origin
        b.heightMin         = 0.15f;
        b.heightMax         = 0.40f;
        b.noiseAmplitude    = TerrainHeight;    // amplitude is world-space, normalize happens in generator
        b.noiseFrequency    = 0.004f;
        b.noiseOctaves      = 5;
        b.noisePersistence  = 0.52f;
        b.noiseLacunarity   = 2.1f;

        b.blendRadius       = 28f;              // wide transition into plains
        b.slopeLayerThreshold = 0.40f;

        AssetDatabase.CreateAsset(b, $"{OutputDir}/Biome_Forest.asset");
        return b;
    }

    private static BiomeDefinition CreatePlainsBiome()
    {
        var b = ScriptableObject.CreateInstance<BiomeDefinition>();
        b.biomeName         = "Plains";
        b.mapColor          = ToColor(ColPlains);

        // Height: gentle open plain — 6m to 14m above terrain origin
        b.heightMin         = 0.05f;
        b.heightMax         = 0.12f;
        b.noiseAmplitude    = TerrainHeight;
        b.noiseFrequency    = 0.003f;
        b.noiseOctaves      = 3;
        b.noisePersistence  = 0.38f;
        b.noiseLacunarity   = 2.0f;

        b.blendRadius       = 35f;
        b.slopeLayerThreshold = 0.25f;

        AssetDatabase.CreateAsset(b, $"{OutputDir}/Biome_Plains.asset");
        return b;
    }

    // ─── Settings ─────────────────────────────────────────────────────────────

    private static TerrainGeneratorSettings CreateSettings(
        Texture2D colorMap, BiomeDefinition forest, BiomeDefinition plains)
    {
        var s = ScriptableObject.CreateInstance<TerrainGeneratorSettings>();

        s.seed             = 1337;
        s.globalHeightScale = 1f;
        s.colorMap         = colorMap;
        s.splineSamples    = 400;

        s.biomes.Add(forest);
        s.biomes.Add(plains);

        // ── River ────────────────────────────────────────────────────────────
        // A shallow, wide canyon with a flat floor and erosion detail.
        // Follows a gentle S-curve from north edge to south edge at ~X=255.
        // Depth 8m, width 24m — clearly visible, not a dramatic gorge.
        s.canyons.Add(new CanyonDefinition
        {
            canyonName           = "River",
            depth                = 8f,
            width                = 24f,
            wallProfile          = 0.65f,      // moderate slope (not a cliff)
            wallCurve            = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f),
            smoothness           = 0.6f,
            flatFloor            = true,
            floorFraction        = 0.35f,      // flat 35% center = river bed
            erosionNoiseFrequency = 0.07f,
            erosionNoiseAmplitude = 1.2f,
            controlPoints        = new Vector3[]
            {
                new Vector3(258f, 0f,   0f),   // north entry
                new Vector3(242f, 0f,  70f),   // first bend west
                new Vector3(268f, 0f, 150f),   // swing east
                new Vector3(248f, 0f, 230f),   // road crossing — narrows slightly
                new Vector3(232f, 0f, 310f),   // swing west
                new Vector3(258f, 0f, 400f),   // swing back east
                new Vector3(244f, 0f, 500f),   // south exit
            }
        });

        // ── Road ─────────────────────────────────────────────────────────────
        // Dirt road running west-to-east at Z≈195, bridging the river at Z=230.
        // Y set to match expected plains height (~10 m) so flatness anchors correctly.
        // textureLayerIndex = 0 → assign your road/dirt TerrainLayer as index 0
        //   in both biomes' terrainLayers list (or override here).
        s.roads.Add(new RoadDefinition
        {
            roadName         = "Main Road",
            width            = 10f,
            flatness         = 0.88f,
            smoothingFalloff = 14f,
            textureLayerIndex = 0,            // assign road terrain layer to slot 0
            camber           = 0.007f,
            controlPoints    = new Vector3[]
            {
                new Vector3(  0f, 10f, 198f),
                new Vector3( 65f, 10f, 196f),
                new Vector3(130f, 10f, 195f),
                new Vector3(200f, 10f, 197f), // approach river
                new Vector3(248f, 10f, 195f), // cross river (bridge point)
                new Vector3(300f, 10f, 193f),
                new Vector3(380f, 10f, 196f),
                new Vector3(460f, 10f, 198f),
                new Vector3(500f, 10f, 197f),
            }
        });

        AssetDatabase.CreateAsset(s, $"{OutputDir}/SampleSettings.asset");
        return s;
    }

    // ─── Terrain size setup ───────────────────────────────────────────────────

    private static void ConfigureActiveTerrain()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            Debug.LogWarning(
                "[TerrainSampleSetup] No active Terrain found in the scene. " +
                $"Please set Terrain size to ({TerrainSize}, {TerrainHeight}, {TerrainSize}) manually.");
            return;
        }

        Undo.RecordObject(terrain.terrainData, "Configure Terrain Size for Sample");
        terrain.terrainData.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
        EditorUtility.SetDirty(terrain.terrainData);
        Debug.Log($"[TerrainSampleSetup] Active terrain resized to {TerrainSize}×{TerrainHeight}×{TerrainSize}.");
    }

    // ─── Utility ─────────────────────────────────────────────────────────────

    private static void EnsureDir(string unityPath)
    {
        Directory.CreateDirectory(AbsPath(unityPath));
        AssetDatabase.Refresh();
    }

    private static string AbsPath(string unityPath) =>
        Path.Combine(Application.dataPath, "..", unityPath);

    private static Color ToColor(Color32 c) =>
        new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
}
#endif
