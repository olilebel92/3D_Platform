using UnityEngine;

/// <summary>
/// ScriptableObject that defines one biome: its color-map key, terrain layers,
/// height range, noise parameters, and blend radius.
/// </summary>
[CreateAssetMenu(fileName = "NewBiome", menuName = "Terrain Generator/Biome Definition")]
public class BiomeDefinition : ScriptableObject
{
    // ─── Identity ────────────────────────────────────────────────────────────

    [Header("Identity")]
    [Tooltip("Display name used in the editor and debug logs.")]
    public string biomeName = "New Biome";

    [Tooltip("Color on the color-map texture that identifies this biome region.")]
    public Color mapColor = Color.green;

    // ─── Terrain Layers ──────────────────────────────────────────────────────

    [Header("Terrain Layers")]
    [Tooltip("Terrain layers for this biome. Index 0 = base. Additional layers blend in on steep slopes.")]
    public TerrainLayer[] terrainLayers;

    [Tooltip("Normalized slope threshold (0–1) above which secondary layers (e.g. rock/cliff) appear.")]
    [Range(0f, 1f)] public float slopeLayerThreshold = 0.35f;

    // ─── Height ──────────────────────────────────────────────────────────────

    [Header("Height Range")]
    [Tooltip("Minimum normalized height for this biome (0 = sea level, 1 = TerrainData.size.y).")]
    [Range(0f, 1f)] public float heightMin = 0f;

    [Tooltip("Maximum normalized height for this biome.")]
    [Range(0f, 1f)] public float heightMax = 0.5f;

    // ─── Noise ───────────────────────────────────────────────────────────────

    [Header("Height Noise")]
    [Tooltip("Base Perlin noise frequency — higher = more frequent hills.")]
    public float noiseFrequency = 0.005f;

    [Tooltip("Maximum height variation produced by noise, in world units.")]
    public float noiseAmplitude = 60f;

    [Tooltip("Number of fractal noise octaves (detail layers).")]
    [Range(1, 8)] public int noiseOctaves = 4;

    [Tooltip("Amplitude scale per additional octave (0.1 = rapid falloff, 1 = no falloff).")]
    [Range(0.1f, 1f)] public float noisePersistence = 0.5f;

    [Tooltip("Frequency multiplier per additional octave (lacunarity).")]
    [Range(1f, 4f)] public float noiseLacunarity = 2f;

    // ─── Climate ─────────────────────────────────────────────────────────────

    [Header("Climate")]
    [Tooltip("Moisture level (0 = arid, 1 = wet). Reserved for future moisture-based blending.")]
    [Range(0f, 1f)] public float moisture = 0.5f;

    [Tooltip("Temperature (0 = cold, 1 = hot). Reserved for future temperature-based blending.")]
    [Range(0f, 1f)] public float temperature = 0.5f;

    // ─── Blending ─────────────────────────────────────────────────────────────

    [Header("Blending")]
    [Tooltip("Radius in world units over which this biome smoothly transitions into its neighbors.")]
    [Min(0f)] public float blendRadius = 15f;
}
