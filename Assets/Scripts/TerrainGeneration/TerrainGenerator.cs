using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Core procedural terrain generation engine.
/// All heavy computation runs on a background thread; Unity API calls are
/// marshalled to the main thread before and after.
///
/// Usage (Editor or Runtime):
///   await TerrainGenerator.GenerateAsync(settings, terrain, progress);
///   await TerrainGenerator.GenerateHeightmapAsync(settings, terrain, progress);
///   await TerrainGenerator.GenerateSplatmapAsync(settings, terrain, progress);
/// </summary>
public static class TerrainGenerator
{
    // ─── Thread-safe POD cache structs ───────────────────────────────────────

    private struct BiomeCacheEntry
    {
        public byte mapR, mapG, mapB;           // color key
        public float heightMin, heightMax;
        public float noiseFrequency, noiseAmplitude;
        public int   noiseOctaves;
        public float noisePersistence, noiseLacunarity;
        public float blendRadius;               // world units
        public float slopeLayerThreshold;
        public int[] layerGlobalIndices;        // indices into terrainData.terrainLayers
    }

    private struct RoadCacheEntry
    {
        public Vector3[] controlPoints;
        public float width;
        public float flatness;
        public float smoothingFalloff;
        public int   textureLayerIndex;
        public float camber;
    }

    private struct CanyonCacheEntry
    {
        public Vector3[] controlPoints;
        public float depth, width;
        public float wallProfile;
        public float smoothness;
        public bool  flatFloor;
        public float floorFraction;
        public float erosionFrequency, erosionAmplitude;
        public float[] wallCurveSamples;        // 128 baked samples, x=0..1
        public bool    useCustomCurve;
    }

    /// <summary>All data needed by the background thread — no Unity Object refs.</summary>
    private class PreparedData
    {
        public int     hmRes;           // heightmap resolution
        public int     alphaRes;        // alphamap resolution
        public float   terrainW, terrainH, terrainL;  // size.x, size.y, size.z
        public float   terrainOffX, terrainOffZ;       // world position offsets

        public Color32[] cmPixels;      // color map pixels (null = no map)
        public int       cmW, cmH;

        public Color32[] maskPixels;    // mask pixels (null = no mask)
        public int       maskW, maskH;

        public BiomeCacheEntry[] biomes;
        public int totalLayers;         // terrainData.terrainLayers.Length after setup

        public RoadCacheEntry[]   roads;
        public CanyonCacheEntry[] canyons;

        public int     seed;
        public float   globalHeightScale;
        public Vector2 globalNoiseOffset;
        public int     splineSamples;
        public int     maskForcedBiomeIndex;
        public float   maskHeightClamp;
    }

    // ─── Public async API ────────────────────────────────────────────────────

    /// <summary>Regenerates the full terrain (heightmap + splatmap).</summary>
    public static async Task GenerateAsync(
        TerrainGeneratorSettings settings,
        Terrain terrain,
        IProgress<(string message, float fraction)> progress = null)
    {
        if (!ValidateInputs(settings, terrain)) return;

        progress?.Report(("Preparing data...", 0.02f));
        var data = PrepareData(settings, terrain);

        progress?.Report(("Computing heightmap...", 0.05f));
        float[,] heights = null;
        float[,,] splatmap = null;

        await Task.Run(() =>
        {
            heights = ComputeHeightmap(data, progress, 0.05f, 0.45f);
            ApplyCanyons(data, heights, progress, 0.45f, 0.60f);
            ApplyRoads(data, heights, progress, 0.60f, 0.70f);
            ApplyMask(data, heights);
            progress?.Report(("Building splatmap...", 0.72f));
            splatmap = ComputeSplatmap(data, heights, progress, 0.72f, 0.95f);
        });

        progress?.Report(("Applying to terrain...", 0.97f));
        terrain.terrainData.SetHeights(0, 0, heights);
        terrain.terrainData.SetAlphamaps(0, 0, splatmap);
        terrain.Flush();

        progress?.Report(("Done.", 1f));
        Debug.Log("[TerrainGenerator] Full generation complete.");
    }

    /// <summary>Regenerates heightmap only (canyons + roads + mask included).</summary>
    public static async Task GenerateHeightmapAsync(
        TerrainGeneratorSettings settings,
        Terrain terrain,
        IProgress<(string, float)> progress = null)
    {
        if (!ValidateInputs(settings, terrain)) return;

        progress?.Report(("Preparing data...", 0.02f));
        var data = PrepareData(settings, terrain);

        float[,] heights = null;
        await Task.Run(() =>
        {
            heights = ComputeHeightmap(data, progress, 0.05f, 0.60f);
            ApplyCanyons(data, heights, progress, 0.60f, 0.78f);
            ApplyRoads(data, heights, progress, 0.78f, 0.92f);
            ApplyMask(data, heights);
        });

        progress?.Report(("Applying heightmap...", 0.97f));
        terrain.terrainData.SetHeights(0, 0, heights);
        terrain.Flush();
        progress?.Report(("Done.", 1f));
        Debug.Log("[TerrainGenerator] Heightmap complete.");
    }

    /// <summary>Regenerates splatmap only using the existing heightmap.</summary>
    public static async Task GenerateSplatmapAsync(
        TerrainGeneratorSettings settings,
        Terrain terrain,
        IProgress<(string, float)> progress = null)
    {
        if (!ValidateInputs(settings, terrain)) return;

        progress?.Report(("Preparing data...", 0.02f));
        var data = PrepareData(settings, terrain);

        // Read existing heights on main thread
        var td = terrain.terrainData;
        var heights = td.GetHeights(0, 0, td.heightmapResolution, td.heightmapResolution);

        float[,,] splatmap = null;
        await Task.Run(() =>
        {
            splatmap = ComputeSplatmap(data, heights, progress, 0.05f, 0.95f);
        });

        progress?.Report(("Applying splatmap...", 0.97f));
        terrain.terrainData.SetAlphamaps(0, 0, splatmap);
        terrain.Flush();
        progress?.Report(("Done.", 1f));
        Debug.Log("[TerrainGenerator] Splatmap complete.");
    }

    // ─── Data preparation (main thread) ──────────────────────────────────────

    private static bool ValidateInputs(TerrainGeneratorSettings settings, Terrain terrain)
    {
        if (settings == null) { Debug.LogError("[TerrainGenerator] Settings is null."); return false; }
        if (terrain == null)  { Debug.LogError("[TerrainGenerator] Terrain is null.");  return false; }
        if (terrain.terrainData == null) { Debug.LogError("[TerrainGenerator] TerrainData is null."); return false; }
        return true;
    }

    private static PreparedData PrepareData(TerrainGeneratorSettings settings, Terrain terrain)
    {
        var td  = terrain.terrainData;

        // Auto-set terrain height to the largest noiseAmplitude across all biomes
        // so the user never has to change Terrain Height manually in the Inspector.
        float requiredHeight = 1f;
        foreach (var b in settings.biomes)
            if (b != null && b.noiseAmplitude * settings.globalHeightScale > requiredHeight)
                requiredHeight = b.noiseAmplitude * settings.globalHeightScale;
        if (requiredHeight > td.size.y + 0.01f || td.size.y < 2f)
        {
            td.size = new Vector3(td.size.x, requiredHeight, td.size.z);
            Debug.Log($"[TerrainGenerator] Terrain height auto-set to {requiredHeight} m.");
        }

        var pos = terrain.transform.position;
        var data = new PreparedData
        {
            hmRes           = td.heightmapResolution,
            alphaRes        = td.alphamapResolution,
            terrainW        = td.size.x,
            terrainH        = td.size.y,
            terrainL        = td.size.z,
            terrainOffX     = pos.x,
            terrainOffZ     = pos.z,
            seed            = settings.seed,
            globalHeightScale = settings.globalHeightScale,
            globalNoiseOffset = settings.globalNoiseOffset,
            splineSamples   = Mathf.Max(10, settings.splineSamples),
            maskForcedBiomeIndex = settings.maskForcedBiomeIndex,
            maskHeightClamp = settings.maskHeightClamp,
        };

        // Color map
        if (settings.colorMap != null)
        {
            data.cmPixels = settings.colorMap.GetPixels32();
            data.cmW      = settings.colorMap.width;
            data.cmH      = settings.colorMap.height;
        }

        // Mask
        if (settings.terrainMask != null)
        {
            data.maskPixels = settings.terrainMask.GetPixels32();
            data.maskW      = settings.terrainMask.width;
            data.maskH      = settings.terrainMask.height;
        }

        // Build terrain layer list and biome cache
        SetupTerrainLayers(settings, td, out var layerMap);
        data.totalLayers = td.terrainLayers.Length;
        data.biomes      = BuildBiomeCache(settings, layerMap);

        // Roads
        data.roads = BuildRoadCache(settings);

        // Canyons
        data.canyons = BuildCanyonCache(settings);

        return data;
    }

    // Collect all terrain layers from biomes, deduplicate, assign to TerrainData.
    // layerMap[biomeIndex][layerInBiome] = globalLayerIndex
    private static void SetupTerrainLayers(
        TerrainGeneratorSettings settings,
        TerrainData td,
        out int[][] layerMap)
    {
        var allLayers = new List<TerrainLayer>();
        var indices   = new List<int[]>();

        foreach (var biome in settings.biomes)
        {
            var biomeIndices = new List<int>();
            if (biome != null && biome.terrainLayers != null)
            {
                foreach (var layer in biome.terrainLayers)
                {
                    if (layer == null) continue;
                    int idx = allLayers.IndexOf(layer);
                    if (idx < 0) { idx = allLayers.Count; allLayers.Add(layer); }
                    biomeIndices.Add(idx);
                }
            }
            indices.Add(biomeIndices.ToArray());
        }

        if (allLayers.Count > 0)
            td.terrainLayers = allLayers.ToArray();

        layerMap = indices.ToArray();
    }

    private static BiomeCacheEntry[] BuildBiomeCache(TerrainGeneratorSettings settings, int[][] layerMap)
    {
        var result = new BiomeCacheEntry[settings.biomes.Count];
        for (int i = 0; i < settings.biomes.Count; i++)
        {
            var b = settings.biomes[i];
            if (b == null)
            {
                result[i] = new BiomeCacheEntry { layerGlobalIndices = Array.Empty<int>() };
                continue;
            }

            Color32 c32 = b.mapColor;
            result[i] = new BiomeCacheEntry
            {
                mapR               = c32.r,
                mapG               = c32.g,
                mapB               = c32.b,
                heightMin          = b.heightMin,
                heightMax          = b.heightMax,
                noiseFrequency     = b.noiseFrequency,
                noiseAmplitude     = b.noiseAmplitude,
                noiseOctaves       = b.noiseOctaves,
                noisePersistence   = b.noisePersistence,
                noiseLacunarity    = b.noiseLacunarity,
                blendRadius        = b.blendRadius,
                slopeLayerThreshold = b.slopeLayerThreshold,
                layerGlobalIndices = i < layerMap.Length ? layerMap[i] : Array.Empty<int>(),
            };
        }
        return result;
    }

    private static RoadCacheEntry[] BuildRoadCache(TerrainGeneratorSettings settings)
    {
        var result = new RoadCacheEntry[settings.roads.Count];
        for (int i = 0; i < settings.roads.Count; i++)
        {
            var r = settings.roads[i];
            if (r == null) continue;
            result[i] = new RoadCacheEntry
            {
                controlPoints    = r.controlPoints,
                width            = r.width,
                flatness         = r.flatness,
                smoothingFalloff = r.smoothingFalloff,
                textureLayerIndex = r.textureLayerIndex,
                camber           = r.camber,
            };
        }
        return result;
    }

    private static CanyonCacheEntry[] BuildCanyonCache(TerrainGeneratorSettings settings)
    {
        var result = new CanyonCacheEntry[settings.canyons.Count];
        for (int i = 0; i < settings.canyons.Count; i++)
        {
            var c = settings.canyons[i];
            if (c == null) continue;

            // Bake AnimationCurve to float[] — curve is not thread-safe
            const int curveSamples = 128;
            var curveSampled = new float[curveSamples];
            bool customCurve = c.wallCurve != null && c.wallCurve.length > 2;
            if (customCurve)
            {
                for (int s = 0; s < curveSamples; s++)
                    curveSampled[s] = c.wallCurve.Evaluate((float)s / (curveSamples - 1));
            }

            result[i] = new CanyonCacheEntry
            {
                controlPoints    = c.controlPoints,
                depth            = c.depth,
                width            = c.width,
                wallProfile      = c.wallProfile,
                smoothness       = c.smoothness,
                flatFloor        = c.flatFloor,
                floorFraction    = c.floorFraction,
                erosionFrequency = c.erosionNoiseFrequency,
                erosionAmplitude = c.erosionNoiseAmplitude,
                wallCurveSamples = curveSampled,
                useCustomCurve   = customCurve,
            };
        }
        return result;
    }

    // ─── Heightmap computation (background thread) ───────────────────────────

    private static float[,] ComputeHeightmap(
        PreparedData data,
        IProgress<(string, float)> progress,
        float pStart, float pEnd)
    {
        int res = data.hmRes;
        float[,] heights = new float[res, res];

        int numBiomes = data.biomes.Length;
        bool hasCM    = data.cmPixels != null && numBiomes > 0;

        // Pre-compute max blend radius for the biome weight map
        float maxBlendRadius = 0f;
        for (int b = 0; b < numBiomes; b++)
            maxBlendRadius = Mathf.Max(maxBlendRadius, data.biomes[b].blendRadius);

        // Build blurred biome weight map at color-map resolution (or 1×1 fallback)
        float[] biomeWeightMap;
        int bmW, bmH;
        if (hasCM)
        {
            bmW = data.cmW; bmH = data.cmH;
            biomeWeightMap = BuildBlurredBiomeWeightMap(
                data.cmPixels, bmW, bmH, data.biomes, maxBlendRadius, data.terrainW);
        }
        else
        {
            bmW = 1; bmH = 1;
            biomeWeightMap = new float[numBiomes > 0 ? numBiomes : 1];
            if (numBiomes > 0) biomeWeightMap[0] = 1f;
        }

        // Keep offsets in float32-safe range (< 100 000).
        // Large seeds (e.g. 1 567 547 894) would otherwise produce offsets of
        // ~2.7 billion where float32 precision (~256 units) is coarser than the
        // noise step per texel (~0.02), making every sample identical → flat terrain.
        int   safeSeed  = System.Math.Abs(data.seed % 65521); // 65521 = largest prime < 2^16
        float seedOffX  = safeSeed * 1.7391f;   // max ≈ 113 900, well within float32 precision
        float seedOffY  = safeSeed * 2.3517f;   // max ≈ 154 100
        var   weights  = new float[numBiomes > 0 ? numBiomes : 1];

        for (int z = 0; z < res; z++)
        {
            float v = (float)z / (res - 1);

            for (int x = 0; x < res; x++)
            {
                float u = (float)x / (res - 1);

                float wx = u * data.terrainW + data.globalNoiseOffset.x;
                float wz = v * data.terrainL + data.globalNoiseOffset.y;

                // Sample biome weights from the pre-blurred map
                if (hasCM)
                    SampleBiomeWeightMap(biomeWeightMap, bmW, bmH, numBiomes, u, v, weights);
                else if (numBiomes > 0)
                    weights[0] = 1f;

                float height = 0f;
                if (numBiomes > 0)
                {
                    for (int b = 0; b < numBiomes; b++)
                    {
                        if (weights[b] < 0.0001f) continue;
                        var bm = data.biomes[b];

                        float noise = FractalNoise(
                            wx, wz,
                            bm.noiseOctaves, bm.noiseFrequency,
                            bm.noisePersistence, bm.noiseLacunarity,
                            seedOffX, seedOffY);

                        float range = bm.heightMax - bm.heightMin;
                        float biomeH = (bm.heightMin + noise * range)
                                       * bm.noiseAmplitude * data.globalHeightScale;

                        height += biomeH * weights[b];
                    }
                }

                // Normalize to 0-1 (TerrainData.size.y = max world height)
                heights[z, x] = Mathf.Clamp01(height / data.terrainH);
            }

            if (z % 32 == 0)
            {
                float frac = pStart + (pEnd - pStart) * ((float)z / res);
                progress?.Report(($"Heightmap row {z}/{res}...", frac));
            }
        }

        return heights;
    }

    // ─── Canyon carving (background thread) ──────────────────────────────────

    private static void ApplyCanyons(
        PreparedData data,
        float[,] heights,
        IProgress<(string, float)> progress,
        float pStart, float pEnd)
    {
        if (data.canyons == null || data.canyons.Length == 0) return;

        int res = data.hmRes;
        float invW = 1f / data.terrainW;
        float invL = 1f / data.terrainL;

        for (int ci = 0; ci < data.canyons.Length; ci++)
        {
            var canyon = data.canyons[ci];
            if (canyon.controlPoints == null || canyon.controlPoints.Length < 2) continue;

            float halfWidth     = canyon.width * 0.5f;
            float smoothZone    = canyon.smoothness * halfWidth;
            float totalRadius   = halfWidth + smoothZone;
            float floorHalfW    = halfWidth * canyon.floorFraction;
            float depthNorm     = canyon.depth / data.terrainH;

            // Pre-sample spline in world XZ
            var splineSamplesXZ = SampleSplineXZ(canyon.controlPoints, data.splineSamples);

            for (int z = 0; z < res; z++)
            {
                float wz = (float)z / (res - 1) * data.terrainL;

                for (int x = 0; x < res; x++)
                {
                    float wx = (float)x / (res - 1) * data.terrainW;

                    float dist = ClosestDistanceXZ(splineSamplesXZ, wx, wz, out _);
                    if (dist >= totalRadius) continue;

                    float normDist = dist / halfWidth; // 0=center, 1=rim, >1=blend zone

                    float depthMul;
                    if (normDist <= 1f)
                    {
                        if (canyon.flatFloor && dist < floorHalfW)
                        {
                            depthMul = 1f;
                        }
                        else
                        {
                            depthMul = canyon.useCustomCurve
                                ? EvalBakedCurve(canyon.wallCurveSamples, Mathf.Clamp01(normDist))
                                : SimpleWallFalloff(normDist, canyon.wallProfile);
                        }
                    }
                    else
                    {
                        // Smooth blend zone beyond rim
                        float blendT = 1f - (dist - halfWidth) / smoothZone;
                        depthMul = Mathf.Clamp01(blendT) * 0.0f; // rim is already 0 from curve
                    }

                    // Erosion noise
                    float erosion = 0f;
                    if (canyon.erosionAmplitude > 0f && canyon.erosionFrequency > 0f)
                    {
                        int   cSafe = System.Math.Abs(data.seed % 65521);
                        erosion = (Mathf.PerlinNoise(wx * canyon.erosionFrequency + cSafe,
                                                     wz * canyon.erosionFrequency + cSafe * 1.3f)
                                   - 0.5f) * 2f * canyon.erosionAmplitude / data.terrainH * depthMul;
                    }

                    // Additive max-depth merge (overlapping canyons stack, not double-dip)
                    float carve = depthNorm * depthMul + erosion;
                    heights[z, x] = Mathf.Clamp01(heights[z, x] - carve);
                }
            }

            float cfrac = pStart + (pEnd - pStart) * ((float)(ci + 1) / data.canyons.Length);
            progress?.Report(($"Canyon {ci + 1}/{data.canyons.Length}...", cfrac));
        }
    }

    // ─── Road flattening (background thread) ─────────────────────────────────

    private static void ApplyRoads(
        PreparedData data,
        float[,] heights,
        IProgress<(string, float)> progress,
        float pStart, float pEnd)
    {
        if (data.roads == null || data.roads.Length == 0) return;

        int res = data.hmRes;

        for (int ri = 0; ri < data.roads.Length; ri++)
        {
            var road = data.roads[ri];
            if (road.controlPoints == null || road.controlPoints.Length < 2) continue;

            float halfWidth    = road.width * 0.5f;
            float totalRadius  = halfWidth + road.smoothingFalloff;

            // Pre-sample spline in full 3D (we need Y for road elevation)
            var spline3D = SampleSpline3D(road.controlPoints, data.splineSamples);

            for (int z = 0; z < res; z++)
            {
                float wz = (float)z / (res - 1) * data.terrainL;

                for (int x = 0; x < res; x++)
                {
                    float wx = (float)x / (res - 1) * data.terrainW;

                    float dist = ClosestDistanceXZ3D(spline3D, wx, wz,
                                                     out int closestIdx,
                                                     out float closestY,
                                                     out float lateralSign);
                    if (dist >= totalRadius) continue;

                    float targetH     = Mathf.Clamp01(closestY / data.terrainH);
                    float camberOff   = lateralSign * road.camber * dist / data.terrainH;
                    float targetFinal = Mathf.Clamp01(targetH + camberOff);

                    float blend;
                    if (dist <= halfWidth)
                        blend = road.flatness;
                    else
                        blend = road.flatness * (1f - (dist - halfWidth) / road.smoothingFalloff);

                    heights[z, x] = Mathf.Lerp(heights[z, x], targetFinal, Mathf.Clamp01(blend));
                }
            }

            float rfrac = pStart + (pEnd - pStart) * ((float)(ri + 1) / data.roads.Length);
            progress?.Report(($"Road {ri + 1}/{data.roads.Length}...", rfrac));
        }
    }

    // ─── Mask application (background thread) ────────────────────────────────

    private static void ApplyMask(PreparedData data, float[,] heights)
    {
        if (data.maskPixels == null) return;

        int res = data.hmRes;
        int mw  = data.maskW;
        int mh  = data.maskH;

        for (int z = 0; z < res; z++)
        {
            float v  = (float)z / (res - 1);
            int   my = Mathf.Clamp(Mathf.RoundToInt(v * (mh - 1)), 0, mh - 1);

            for (int x = 0; x < res; x++)
            {
                float u  = (float)x / (res - 1);
                int   mx = Mathf.Clamp(Mathf.RoundToInt(u * (mw - 1)), 0, mw - 1);

                Color32 mp = data.maskPixels[my * mw + mx];

                // R > 127 → exclusion zone
                if (mp.r > 127) { heights[z, x] = 0f; continue; }

                // B > 127 → height clamp
                if (mp.b > 127) heights[z, x] = Mathf.Min(heights[z, x], data.maskHeightClamp);
            }
        }
    }

    // ─── Splatmap computation (background thread) ─────────────────────────────

    private static float[,,] ComputeSplatmap(
        PreparedData data,
        float[,] heights,
        IProgress<(string, float)> progress,
        float pStart, float pEnd)
    {
        int   res       = data.alphaRes;
        int   hmRes     = data.hmRes;
        int   numLayers = data.totalLayers;
        if (numLayers == 0) numLayers = 1;

        float[,,] splatmap = new float[res, res, numLayers];
        int numBiomes = data.biomes.Length;

        bool hasCM = data.cmPixels != null && numBiomes > 0;

        float maxBlendRadius = 0f;
        for (int b = 0; b < numBiomes; b++)
            maxBlendRadius = Mathf.Max(maxBlendRadius, data.biomes[b].blendRadius);

        float[] biomeWeightMap;
        int bmW, bmH;
        if (hasCM)
        {
            bmW = data.cmW; bmH = data.cmH;
            biomeWeightMap = BuildBlurredBiomeWeightMap(
                data.cmPixels, bmW, bmH, data.biomes, maxBlendRadius, data.terrainW);
        }
        else
        {
            bmW = 1; bmH = 1;
            biomeWeightMap = new float[numBiomes > 0 ? numBiomes : 1];
            if (numBiomes > 0) biomeWeightMap[0] = 1f;
        }

        // Pre-sample roads for splatmap override
        var roadSplines = new Vector2[data.roads.Length][];
        for (int ri = 0; ri < data.roads.Length; ri++)
        {
            var road = data.roads[ri];
            if (road.controlPoints != null && road.controlPoints.Length >= 2)
                roadSplines[ri] = SampleSplineXZ(road.controlPoints, data.splineSamples);
        }

        var weights = new float[numBiomes > 0 ? numBiomes : 1];

        for (int z = 0; z < res; z++)
        {
            float v = (float)z / (res - 1);

            for (int x = 0; x < res; x++)
            {
                float u = (float)x / (res - 1);

                // Get height and compute slope from heightmap
                int hx  = Mathf.Clamp(Mathf.RoundToInt(u * (hmRes - 1)), 0, hmRes - 1);
                int hz  = Mathf.Clamp(Mathf.RoundToInt(v * (hmRes - 1)), 0, hmRes - 1);
                float slope = ComputeSlope(heights, hx, hz, hmRes, data.terrainW, data.terrainH);

                // Sample biome weights
                if (hasCM)
                    SampleBiomeWeightMap(biomeWeightMap, bmW, bmH, numBiomes, u, v, weights);
                else if (numBiomes > 0)
                    weights[0] = 1f;

                // Accumulate layer weights from biome contributions
                var layerWeights = new float[numLayers];
                if (numBiomes > 0)
                {
                    for (int b = 0; b < numBiomes; b++)
                    {
                        if (weights[b] < 0.0001f) continue;
                        var bm = data.biomes[b];
                        if (bm.layerGlobalIndices == null || bm.layerGlobalIndices.Length == 0) continue;

                        int baseLayerIdx = bm.layerGlobalIndices[0];
                        if (baseLayerIdx < numLayers)
                            layerWeights[baseLayerIdx] += weights[b] * (1f - Mathf.Clamp01(slope / bm.slopeLayerThreshold));

                        // Secondary (slope) layer
                        if (bm.layerGlobalIndices.Length >= 2)
                        {
                            int slopeLayerIdx = bm.layerGlobalIndices[1];
                            if (slopeLayerIdx < numLayers)
                                layerWeights[slopeLayerIdx] += weights[b] * Mathf.Clamp01(slope / bm.slopeLayerThreshold);
                        }
                        else if (bm.layerGlobalIndices.Length == 1 && baseLayerIdx < numLayers)
                        {
                            // Only one layer: give full weight regardless of slope
                            layerWeights[baseLayerIdx] = weights[b];
                        }
                    }
                }
                else if (numLayers > 0)
                {
                    layerWeights[0] = 1f;
                }

                // Road texture override
                float wx = u * data.terrainW;
                float wz = v * data.terrainL;
                for (int ri = 0; ri < data.roads.Length; ri++)
                {
                    var road = data.roads[ri];
                    if (roadSplines[ri] == null) continue;
                    if (road.textureLayerIndex >= numLayers) continue;

                    float dist = ClosestDistanceXZ(roadSplines[ri], wx, wz, out _);
                    if (dist < road.width * 0.5f)
                    {
                        // Full road texture
                        for (int l = 0; l < numLayers; l++) layerWeights[l] = 0f;
                        layerWeights[road.textureLayerIndex] = 1f;
                        break;
                    }
                    else if (dist < road.width * 0.5f + road.smoothingFalloff)
                    {
                        float t = 1f - (dist - road.width * 0.5f) / road.smoothingFalloff;
                        for (int l = 0; l < numLayers; l++) layerWeights[l] *= (1f - t);
                        layerWeights[road.textureLayerIndex] += t;
                    }
                }

                // Normalize and write
                float total = 0f;
                for (int l = 0; l < numLayers; l++) total += layerWeights[l];
                if (total > 0f)
                    for (int l = 0; l < numLayers; l++)
                        splatmap[z, x, l] = layerWeights[l] / total;
                else
                    splatmap[z, x, 0] = 1f;
            }

            if (z % 32 == 0)
            {
                float frac = pStart + (pEnd - pStart) * ((float)z / res);
                progress?.Report(($"Splatmap row {z}/{res}...", frac));
            }
        }

        return splatmap;
    }

    // ─── Biome weight map (separable Gaussian blur) ───────────────────────────

    private static float[] BuildBlurredBiomeWeightMap(
        Color32[] cmPixels, int cmW, int cmH,
        BiomeCacheEntry[] biomes, float maxBlendRadius, float terrainW)
    {
        int numBiomes = biomes.Length;
        var weights   = new float[cmW * cmH * numBiomes];

        // Step 1: hard biome assignment
        for (int i = 0; i < Mathf.Min(cmPixels.Length, cmW * cmH); i++)
        {
            int b = FindClosestBiome(cmPixels[i], biomes);
            if (b >= 0) weights[i * numBiomes + b] = 1f;
        }

        // Step 2: separable Gaussian blur based on max blend radius
        float blendRadiusCM = (terrainW > 0f && maxBlendRadius > 0f)
            ? maxBlendRadius / terrainW * cmW
            : 0f;

        if (blendRadiusCM >= 1f)
            weights = SeparableGaussianBlur(weights, cmW, cmH, numBiomes, blendRadiusCM);

        // Step 3: normalize per pixel
        for (int i = 0; i < cmW * cmH; i++)
        {
            float sum = 0f;
            for (int b = 0; b < numBiomes; b++) sum += weights[i * numBiomes + b];
            if (sum > 0f)
                for (int b = 0; b < numBiomes; b++) weights[i * numBiomes + b] /= sum;
        }

        return weights;
    }

    private static float[] SeparableGaussianBlur(
        float[] src, int w, int h, int numBiomes, float radiusPx)
    {
        int kernelR = Mathf.CeilToInt(radiusPx);
        int kernelSize = kernelR * 2 + 1;
        float sigma = radiusPx * 0.5f;

        float[] kw = new float[kernelSize];
        float kSum = 0f;
        for (int i = 0; i < kernelSize; i++)
        {
            float d = i - kernelR;
            kw[i] = Mathf.Exp(-d * d / (2f * sigma * sigma));
            kSum += kw[i];
        }
        for (int i = 0; i < kernelSize; i++) kw[i] /= kSum;

        float[] tmp = new float[w * h * numBiomes];
        float[] dst = new float[w * h * numBiomes];

        // Horizontal pass
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int baseIdx = (y * w + x) * numBiomes;
                for (int b = 0; b < numBiomes; b++)
                {
                    float val = 0f;
                    for (int k = -kernelR; k <= kernelR; k++)
                    {
                        int sx = Mathf.Clamp(x + k, 0, w - 1);
                        val += src[(y * w + sx) * numBiomes + b] * kw[k + kernelR];
                    }
                    tmp[baseIdx + b] = val;
                }
            }
        }

        // Vertical pass
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int baseIdx = (y * w + x) * numBiomes;
                for (int b = 0; b < numBiomes; b++)
                {
                    float val = 0f;
                    for (int k = -kernelR; k <= kernelR; k++)
                    {
                        int sy = Mathf.Clamp(y + k, 0, h - 1);
                        val += tmp[(sy * w + x) * numBiomes + b] * kw[k + kernelR];
                    }
                    dst[baseIdx + b] = val;
                }
            }
        }

        return dst;
    }

    private static void SampleBiomeWeightMap(
        float[] map, int w, int h, int numBiomes, float u, float v, float[] outWeights)
    {
        float fx = u * (w - 1);
        float fy = v * (h - 1);
        int x0   = Mathf.Clamp((int)fx, 0, w - 2);
        int y0   = Mathf.Clamp((int)fy, 0, h - 2);
        float tx = fx - x0;
        float ty = fy - y0;

        int i00 = (y0 * w + x0)         * numBiomes;
        int i10 = (y0 * w + x0 + 1)     * numBiomes;
        int i01 = ((y0 + 1) * w + x0)   * numBiomes;
        int i11 = ((y0 + 1) * w + x0 + 1) * numBiomes;

        for (int b = 0; b < numBiomes; b++)
        {
            float w00 = map[i00 + b];
            float w10 = map[i10 + b];
            float w01 = map[i01 + b];
            float w11 = map[i11 + b];
            outWeights[b] = Mathf.Lerp(Mathf.Lerp(w00, w10, tx), Mathf.Lerp(w01, w11, tx), ty);
        }
    }

    private static int FindClosestBiome(Color32 c, BiomeCacheEntry[] biomes)
    {
        int   best     = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < biomes.Length; i++)
        {
            float dr = c.r - biomes[i].mapR;
            float dg = c.g - biomes[i].mapG;
            float db = c.b - biomes[i].mapB;
            float d  = dr * dr + dg * dg + db * db;
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    // ─── Noise ───────────────────────────────────────────────────────────────

    /// <summary>Fractal (fBm) Perlin noise — thread-safe in Unity 2022+.</summary>
    private static float FractalNoise(
        float x, float y,
        int octaves, float frequency,
        float persistence, float lacunarity,
        float seedOffX, float seedOffY)
    {
        float value     = 0f;
        float amplitude = 1f;
        float total     = 0f;
        float freq      = frequency;

        for (int i = 0; i < octaves; i++)
        {
            value += Mathf.PerlinNoise(x * freq + seedOffX, y * freq + seedOffY) * amplitude;
            total     += amplitude;
            amplitude *= persistence;
            freq      *= lacunarity;
        }

        return total > 0f ? value / total : 0f;
    }

    // ─── Spline helpers ───────────────────────────────────────────────────────

    /// <summary>Evaluates a Catmull-Rom spline at parameter t ∈ [0,1].</summary>
    private static Vector3 EvalCatmullRom(Vector3[] pts, float t)
    {
        int n  = pts.Length;
        if (n == 1) return pts[0];
        if (n == 2) return Vector3.Lerp(pts[0], pts[1], t);

        float ft = t * (n - 1);
        int   i  = Mathf.Clamp(Mathf.FloorToInt(ft), 0, n - 2);
        float u  = ft - i;

        Vector3 p0 = pts[Mathf.Max(0,     i - 1)];
        Vector3 p1 = pts[i];
        Vector3 p2 = pts[Mathf.Min(n - 1, i + 1)];
        Vector3 p3 = pts[Mathf.Min(n - 1, i + 2)];

        return 0.5f * (
            2f * p1
            + (-p0 + p2) * u
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (u * u)
            + (-p0 + 3f * p1 - 3f * p2 + p3)      * (u * u * u)
        );
    }

    private static Vector2[] SampleSplineXZ(Vector3[] pts, int samples)
    {
        var result = new Vector2[samples + 1];
        for (int i = 0; i <= samples; i++)
        {
            Vector3 p = EvalCatmullRom(pts, (float)i / samples);
            result[i] = new Vector2(p.x, p.z);
        }
        return result;
    }

    private static Vector3[] SampleSpline3D(Vector3[] pts, int samples)
    {
        var result = new Vector3[samples + 1];
        for (int i = 0; i <= samples; i++)
            result[i] = EvalCatmullRom(pts, (float)i / samples);
        return result;
    }

    private static float ClosestDistanceXZ(Vector2[] splineXZ, float wx, float wz, out int closestIdx)
    {
        float minDist = float.MaxValue;
        closestIdx = 0;
        for (int i = 0; i < splineXZ.Length; i++)
        {
            float dx = wx - splineXZ[i].x;
            float dz = wz - splineXZ[i].y;
            float d  = dx * dx + dz * dz;
            if (d < minDist) { minDist = d; closestIdx = i; }
        }
        return Mathf.Sqrt(minDist);
    }

    private static float ClosestDistanceXZ3D(
        Vector3[] spline3D, float wx, float wz,
        out int closestIdx, out float closestY, out float lateralSign)
    {
        float minDist = float.MaxValue;
        closestIdx = 0; closestY = 0f; lateralSign = 1f;

        for (int i = 0; i < spline3D.Length; i++)
        {
            float dx = wx - spline3D[i].x;
            float dz = wz - spline3D[i].z;
            float d  = dx * dx + dz * dz;
            if (d < minDist)
            {
                minDist    = d;
                closestIdx = i;
                closestY   = spline3D[i].y;

                // Compute lateral sign: cross product of road tangent and (point - spline point) in XZ
                if (i + 1 < spline3D.Length)
                {
                    float tx = spline3D[i + 1].x - spline3D[i].x;
                    float tz = spline3D[i + 1].z - spline3D[i].z;
                    lateralSign = Mathf.Sign(tx * dz - tz * dx);
                }
            }
        }
        return Mathf.Sqrt(minDist);
    }

    // ─── Canyon wall profile ──────────────────────────────────────────────────

    private static float SimpleWallFalloff(float normDist, float wallProfile)
    {
        // wallProfile 0 = cliff (power < 1 = steep), 1 = gentle (linear)
        float exponent = Mathf.Lerp(0.15f, 1f, wallProfile);
        return Mathf.Clamp01(1f - Mathf.Pow(normDist, exponent));
    }

    private static float EvalBakedCurve(float[] samples, float t)
    {
        if (samples == null || samples.Length == 0) return 0f;
        float fx = t * (samples.Length - 1);
        int   i  = Mathf.Clamp((int)fx, 0, samples.Length - 2);
        return Mathf.Lerp(samples[i], samples[i + 1], fx - i);
    }

    // ─── Slope ───────────────────────────────────────────────────────────────

    private static float ComputeSlope(float[,] heights, int x, int z, int res, float terrainW, float terrainH)
    {
        int x1 = Mathf.Min(x + 1, res - 1);
        int z1 = Mathf.Min(z + 1, res - 1);
        float scale = terrainH / (terrainW / res); // world slope scale

        float dx = (heights[z, x1] - heights[z, x]) * scale;
        float dz = (heights[z1, x] - heights[z, x]) * scale;
        // Normalize slope to 0-1 (0=flat, 1=~45°)
        return Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz));
    }
}
