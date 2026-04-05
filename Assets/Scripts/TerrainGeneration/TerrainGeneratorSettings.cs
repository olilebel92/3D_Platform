using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Master settings asset for the procedural terrain generator.
/// Create via Assets > Create > Terrain Generator > Settings.
/// Assign to the TerrainGeneratorWindow to persist settings across sessions.
/// </summary>
[CreateAssetMenu(fileName = "TerrainGeneratorSettings", menuName = "Terrain Generator/Settings")]
public class TerrainGeneratorSettings : ScriptableObject
{
    // ─── Global ──────────────────────────────────────────────────────────────

    [Header("Global")]
    [Tooltip("Integer seed for fully reproducible terrain generation. " +
             "The same seed always produces the same terrain from the same settings.")]
    public int seed = 42;

    [Tooltip("Overall height multiplier applied on top of all biome amplitudes.")]
    public float globalHeightScale = 1f;

    [Tooltip("UV offset added to all noise coordinate lookups (shift the noise pattern).")]
    public Vector2 globalNoiseOffset = Vector2.zero;

    [Tooltip("Number of evaluation samples along each road/canyon spline during baking. " +
             "Higher = smoother curves, slower generation.")]
    [Range(50, 1000)] public int splineSamples = 300;

    // ─── Color Map ───────────────────────────────────────────────────────────

    [Header("Color Map")]
    [Tooltip("PNG/JPG texture where each unique color defines a biome region. " +
             "The texture is mapped 1:1 onto the terrain's UV space.")]
    public Texture2D colorMap;

    [Tooltip("Biome definitions, each mapped to a color in the color map. " +
             "Order does not matter — matching is done by closest color distance.")]
    [NonReorderable]
    public List<BiomeDefinition> biomes = new List<BiomeDefinition>();

    // ─── Mask ────────────────────────────────────────────────────────────────

    [Header("Terrain Mask")]
    [Tooltip("Optional mask texture painted via the TerrainGeneratorWindow's paint tool.\n" +
             "R channel > 0.5 → Exclusion zone (height set to 0).\n" +
             "G channel > 0.5 → Forced biome zone (uses maskForcedBiomeIndex).\n" +
             "B channel > 0.5 → Height clamp zone (height clamped to maskHeightClamp).")]
    public Texture2D terrainMask;

    [Tooltip("Index into the biomes list that is forced wherever the mask G channel > 0.5.")]
    [Min(0)] public int maskForcedBiomeIndex = 0;

    [Tooltip("Normalized height (0–1) applied as an upper clamp wherever the mask B channel > 0.5.")]
    [Range(0f, 1f)] public float maskHeightClamp = 0.5f;

    // ─── Roads ───────────────────────────────────────────────────────────────

    [Header("Roads")]
    [Tooltip("Road definitions baked into the heightmap and splatmap on generation.")]
    public List<RoadDefinition> roads = new List<RoadDefinition>();

    // ─── Canyons ─────────────────────────────────────────────────────────────

    [Header("Canyons / Valleys")]
    [Tooltip("Canyon definitions carved into the heightmap on generation. " +
             "Overlapping canyons merge using additive max-depth blending.")]
    public List<CanyonDefinition> canyons = new List<CanyonDefinition>();

#if UNITY_EDITOR
    // Strip null biome entries — Unity 6's inspector throws SerializedObjectNotCreatableException
    // when it encounters a null ScriptableObject reference in a serialized list.
    private void OnValidate()
    {
        for (int i = biomes.Count - 1; i >= 0; i--)
            if (biomes[i] == null) biomes.RemoveAt(i);
    }
#endif
}
