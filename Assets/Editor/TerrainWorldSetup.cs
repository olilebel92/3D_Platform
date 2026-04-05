// Editor-only — must live inside an "Editor" folder.
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates a ready-to-run 1000×1000 starting world with:
///   • Hills biome   — north band, rising terrain (38–70 m)
///   • Forest biome  — west/centre, rolling hills (14–42 m)
///   • Plains biome  — south-east, open flat land (5–14 m)
///   • River canyon  — winds south from the hills through the forest/plains border
///   • Road 1        — Main road E–W through the plains at Z ≈ 700
///   • Road 2        — Forest path running N into the hills (X ≈ 220)
///   • Road 3        — Plains loop road in the safe south-east starting area
///
/// Run via:  Tools > Terrain Generator > Create World (1000×1000 Starter)
/// </summary>
public static class TerrainWorldSetup
{
    // ─── Terrain dimensions ───────────────────────────────────────────────────
    private const float TerrainW  = 1000f;
    private const float TerrainL  = 1000f;
    private const float TerrainH  = 300f;   // max world height (TerrainData.size.y)

    // ─── Output paths ─────────────────────────────────────────────────────────
    private const string OutputDir    = "Assets/ScriptableObjects/World";
    private const string ColorMapPath = "Assets/ScriptableObjects/World/WorldColorMap.png";
    private const string SettingsPath = "Assets/ScriptableObjects/World/WorldSettings.asset";
    private const int    MapRes       = 512;   // color-map resolution (pixels)

    // ─── Biome colors  (must match BiomeLibrarySetup keys exactly) ────────────
    private static readonly Color32 ColHills  = new Color32(100, 135,  60, 255);
    private static readonly Color32 ColForest = new Color32( 30, 120,  40, 255);
    private static readonly Color32 ColPlains = new Color32(180, 210,  90, 255);

    // ─── Menu entry ───────────────────────────────────────────────────────────

    [MenuItem("Tools/Terrain Generator/Create World (1000x1000 Starter)")]
    public static void Create()
    {
        EnsureDir(OutputDir);

        var colorMap = GenerateColorMap();
        var settings = BuildSettings(colorMap);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ConfigureActiveTerrain();

        EditorGUIUtility.PingObject(settings);
        Debug.Log(
            "[TerrainWorldSetup] Done!\n" +
            "Steps:\n" +
            "  1. Open  Tools > Terrain Generator\n" +
            "  2. Assign WorldSettings and your Terrain\n" +
            "  3. Make sure your Biome assets from the Biome Library have TerrainLayers assigned\n" +
            "  4. Slot the Hills / Forest / Plains BiomeDefinition assets into WorldSettings.biomes " +
            "in the order: [0] Hills  [1] Forest  [2] Plains\n" +
            "  5. Hit Regenerate All\n" +
            $"Asset folder: {OutputDir}/"
        );
    }

    // ─── Color map ────────────────────────────────────────────────────────────

    /// <summary>
    /// Paints a 512×512 top-down biome layout:
    ///
    ///   U = 0 (west) ──────────────────── U = 1 (east)
    ///   V = 0 (north / Z = 0)
    ///        ┌──────────────────────────────────┐
    ///        │         H I L L S                │  ← hills band ~top 35 %
    ///        │  (organic wavy southern border)  │
    ///        ├─────────────┬────────────────────┤
    ///        │             │                    │
    ///        │  F O R E S T│   P L A I N S      │  ← lower 65 %
    ///        │  (west ~48%)│   (east ~52%)      │
    ///        │             │                    │
    ///        └─────────────┴────────────────────┘
    ///   V = 1 (south / Z = 1000)
    ///
    /// All borders use layered Perlin noise for organic edges.
    /// A hint channel is painted at the river location for visual reference.
    /// </summary>
    private static Texture2D GenerateColorMap()
    {
        var pixels = new Color32[MapRes * MapRes];

        for (int y = 0; y < MapRes; y++)
        {
            float v = (float)y / MapRes;   // V = 0 north, 1 south

            for (int x = 0; x < MapRes; x++)
            {
                float u = (float)x / MapRes;   // U = 0 west, 1 east

                // ── Hills border (varies with noise) ──────────────────────────
                // Base boundary at V = 0.33; noise adds organic shape.
                // Hills also push further south on the east half (higher terrain there).
                float nH1 = Mathf.PerlinNoise(u * 2.4f + 11f, v * 3.1f + 7f);
                float nH2 = Mathf.PerlinNoise(u * 5.8f + 3f,  v * 4.7f + 19f) * 0.35f;
                float hillsSouth = 0.33f + (nH1 + nH2) * 0.10f;

                // East push: hills extend further south on the right side
                float eastPush = Mathf.Clamp01((u - 0.55f) * 2.5f) * 0.12f;
                hillsSouth += eastPush;

                bool isHills = v < hillsSouth;

                if (isHills)
                {
                    pixels[y * MapRes + x] = ColHills;
                    continue;
                }

                // ── Forest / Plains border (organic, roughly at U = 0.47) ─────
                float nF1 = Mathf.PerlinNoise(u * 3.2f + 5f,  v * 2.0f + 13f);
                float nF2 = Mathf.PerlinNoise(u * 6.5f + 17f, v * 5.3f + 2f) * 0.28f;

                // Border creeps east at the top (near hills) and west at the bottom
                float vBias   = Mathf.Lerp(-0.06f, 0.06f, v);       // shifts split east as we go south
                float forestE = 0.47f + vBias + (nF1 + nF2) * 0.10f;

                pixels[y * MapRes + x] = u < forestE ? ColForest : ColPlains;
            }
        }

        // Paint a subtle river hint (cosmetic only — actual river is a CanyonDefinition)
        PaintRiverHint(pixels, MapRes, MapRes);

        // Build & save texture
        var tex = new Texture2D(MapRes, MapRes, TextureFormat.RGB24, false);
        tex.SetPixels32(pixels);
        tex.Apply();

        string absPath = AbsPath(ColorMapPath);
        File.WriteAllBytes(absPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(ColorMapPath, ImportAssetOptions.ForceUpdate);

        var imp = (TextureImporter)AssetImporter.GetAtPath(ColorMapPath);
        imp.isReadable           = true;
        imp.textureCompression   = TextureImporterCompression.Uncompressed;
        imp.mipmapEnabled        = false;
        imp.filterMode           = FilterMode.Bilinear;
        imp.SaveAndReimport();

        Debug.Log($"[TerrainWorldSetup] Color map saved → {ColorMapPath}");
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ColorMapPath);
    }

    /// <summary>
    /// Paints a narrow stripe on the color map to hint at the river's path.
    /// The river itself is carved by the CanyonDefinition, not this color.
    /// </summary>
    private static void PaintRiverHint(Color32[] pixels, int W, int H)
    {
        // River runs at U ≈ 0.46-0.49 (X ≈ 460-490 in world space)
        // with a gentle S-meander
        const int halfW = 4;

        for (int y = 0; y < H; y++)
        {
            float v = (float)y / H;
            // Mirror the world-space river meander in UV space
            float uCenter = 0.47f
                + Mathf.Sin(v * Mathf.PI * 1.6f) * 0.025f
                + Mathf.Sin(v * Mathf.PI * 4.2f) * 0.008f;

            int cx = Mathf.RoundToInt(uCenter * W);
            for (int dx = -halfW; dx <= halfW; dx++)
            {
                int px = cx + dx;
                if (px < 0 || px >= W) continue;
                float t = 1f - (float)Mathf.Abs(dx) / halfW;

                Color32 c = pixels[y * W + px];
                // Blend toward a darker blue-green tint
                c.r = (byte)Mathf.RoundToInt(Mathf.Lerp(c.r, 50,  t * 0.5f));
                c.g = (byte)Mathf.RoundToInt(Mathf.Lerp(c.g, 140, t * 0.5f));
                c.b = (byte)Mathf.RoundToInt(Mathf.Lerp(c.b, 80,  t * 0.5f));
                pixels[y * W + px] = c;
            }
        }
    }

    // ─── Settings ─────────────────────────────────────────────────────────────

    private static TerrainGeneratorSettings BuildSettings(Texture2D colorMap)
    {
        var s = ScriptableObject.CreateInstance<TerrainGeneratorSettings>();
        s.seed             = 4219;
        s.globalHeightScale = 1f;
        s.globalNoiseOffset = new Vector2(17f, 23f);   // offset to avoid noise origin artefacts
        s.splineSamples    = 500;
        s.colorMap         = colorMap;

        // Auto-assign biomes if the library assets already exist on disk.
        // Run "Create Biome Library" first, then this script, and they will
        // be wired up automatically.
        string biomeDir = "Assets/ScriptableObjects/Biomes";
        foreach (var name in new[] { "Biome_Hills", "Biome_Forest", "Biome_Plains" })
        {
            var biome = AssetDatabase.LoadAssetAtPath<BiomeDefinition>($"{biomeDir}/{name}.asset");
            if (biome != null)
                s.biomes.Add(biome);
            else
                Debug.LogWarning($"[TerrainWorldSetup] Could not find {name}.asset — " +
                                 "run 'Create Biome Library' first, then re-run this setup.");
        }

        // Create the asset first, then add roads/canyons and mark dirty.
        // Unity does not reliably serialize nested [Serializable] lists
        // (containing arrays) if they are populated before CreateAsset.
        AssetDatabase.CreateAsset(s, SettingsPath);

        AddRiver(s);
        AddRoads(s);

        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
        return s;
    }

    // ─── River ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A wide, shallow river canyon flowing south from the hills.
    /// Flat floor + erosion noise gives it a believable riverbed.
    ///
    /// World path (XZ):
    ///   Hills entry  → (482, Z=  80)
    ///   Forest       → winds between X 450–510
    ///   Plains exit  → (472, Z=1000)
    ///
    /// Y = 0 on all points — the canyon carver uses relative depth (10 m),
    /// so the absolute Y of control points doesn't affect the result.
    /// </summary>
    private static void AddRiver(TerrainGeneratorSettings s)
    {
        s.canyons.Add(new CanyonDefinition
        {
            canyonName            = "River",
            depth                 = 10f,
            width                 = 28f,
            wallProfile           = 0.60f,
            wallCurve             = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f),
            smoothness            = 0.55f,
            flatFloor             = true,
            floorFraction         = 0.30f,
            erosionNoiseFrequency = 0.06f,
            erosionNoiseAmplitude = 1.8f,
            controlPoints         = new Vector3[]
            {
                new Vector3(480f, 0f,   80f),  // exits hills
                new Vector3(458f, 0f,  180f),  // swing west
                new Vector3(505f, 0f,  300f),  // swing east
                new Vector3(468f, 0f,  420f),  // swing west
                new Vector3(500f, 0f,  540f),  // crosses E-W road zone
                new Vector3(472f, 0f,  660f),  // swing west
                new Vector3(496f, 0f,  780f),  // swing east
                new Vector3(464f, 0f,  900f),  // swing west into plains
                new Vector3(478f, 0f, 1000f),  // south exit
            }
        });
    }

    // ─── Roads ────────────────────────────────────────────────────────────────

    private static void AddRoads(TerrainGeneratorSettings s)
    {
        // ── Road 1 — Main E-W Road ────────────────────────────────────────────
        // Crosses the full 1000 m width at Z ≈ 700 (through plains + forest).
        // It bridges the river at X ≈ 480.
        // Y ramps slightly: plains side is lower (~12 m), forest side higher (~22 m).
        s.roads.Add(new RoadDefinition
        {
            roadName         = "Main Road (E-W)",
            width            = 12f,
            flatness         = 0.88f,
            smoothingFalloff = 18f,
            textureLayerIndex = 0,           // assign your road/dirt layer here
            camber           = 0.006f,
            controlPoints    = new Vector3[]
            {
                new Vector3(   0f, 22f, 705f),
                new Vector3( 120f, 21f, 703f),
                new Vector3( 240f, 20f, 700f),
                new Vector3( 360f, 19f, 698f),
                new Vector3( 470f, 18f, 701f),  // approach river (west bank)
                new Vector3( 490f, 18f, 701f),  // bridge midpoint
                new Vector3( 510f, 17f, 700f),  // east bank
                new Vector3( 630f, 15f, 699f),
                new Vector3( 760f, 13f, 700f),
                new Vector3( 880f, 12f, 702f),
                new Vector3(1000f, 12f, 703f),
            }
        });

        // ── Road 2 — Forest Path N (into the Hills) ────────────────────────────
        // A narrower, rougher track starting from the main road,
        // climbing north-west through the forest into the hills zone.
        // Y rises steadily from plains/forest level (~18 m) to hills level (~58 m).
        s.roads.Add(new RoadDefinition
        {
            roadName         = "Forest Path (N)",
            width            = 7f,
            flatness         = 0.72f,          // less flat = terrain still shows through
            smoothingFalloff = 10f,
            textureLayerIndex = 0,
            camber           = 0.012f,
            controlPoints    = new Vector3[]
            {
                new Vector3(220f, 20f, 703f),   // joins main road
                new Vector3(215f, 26f, 610f),
                new Vector3(208f, 34f, 510f),
                new Vector3(200f, 43f, 410f),   // entering hills transition
                new Vector3(192f, 52f, 310f),
                new Vector3(185f, 58f, 210f),   // deep in hills
                new Vector3(180f, 62f, 130f),   // near hills peak area
            }
        });

        // ── Road 3 — Plains Loop (south-east safe zone) ────────────────────────
        // A wide, well-maintained road looping through the flat plains starting
        // area. This is where the player begins — it connects key locations.
        // Stays flat at ~12 m throughout (low plains terrain).
        s.roads.Add(new RoadDefinition
        {
            roadName         = "Plains Loop",
            width            = 10f,
            flatness         = 0.90f,
            smoothingFalloff = 14f,
            textureLayerIndex = 0,
            camber           = 0.005f,
            controlPoints    = new Vector3[]
            {
                new Vector3( 760f, 12f,  703f),   // west end on main road
                new Vector3( 790f, 12f,  780f),
                new Vector3( 810f, 12f,  870f),
                new Vector3( 820f, 12f,  950f),
                new Vector3( 820f, 12f, 1000f),   // south edge (map boundary)
            }
        });
    }

    // ─── Terrain size ─────────────────────────────────────────────────────────

    private static void ConfigureActiveTerrain()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            Debug.LogWarning(
                "[TerrainWorldSetup] No active Terrain found. " +
                $"Manually set Terrain size to ({TerrainW}, {TerrainH}, {TerrainL}).");
            return;
        }

        Undo.RecordObject(terrain.terrainData, "Configure Terrain for 1000×1000 World");
        // 1025 gives ~1 m/texel on a 1000 m terrain — enough resolution to show
        // fine noise detail (4–6 m features) without excessive memory use.
        terrain.terrainData.heightmapResolution = 1025;
        terrain.terrainData.size = new Vector3(TerrainW, TerrainH, TerrainL);
        EditorUtility.SetDirty(terrain.terrainData);
        Debug.Log($"[TerrainWorldSetup] Terrain resized to {TerrainW}×{TerrainH}×{TerrainL}, heightmap 1025×1025.");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static void EnsureDir(string unityPath)
    {
        Directory.CreateDirectory(AbsPath(unityPath));
        AssetDatabase.Refresh();
    }

    private static string AbsPath(string unityPath) =>
        Path.Combine(Application.dataPath, "..", unityPath);
}
#endif
