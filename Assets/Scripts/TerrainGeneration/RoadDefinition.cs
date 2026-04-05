using System;
using UnityEngine;

/// <summary>
/// Serializable data class that defines a road path and its baking properties.
/// Roads are evaluated as Catmull-Rom splines and baked into the heightmap and splatmap.
/// </summary>
[Serializable]
public class RoadDefinition
{
    // ─── Identity ────────────────────────────────────────────────────────────

    [Tooltip("Display name shown in the TerrainGeneratorWindow.")]
    public string roadName = "Road";

    // ─── Spline ──────────────────────────────────────────────────────────────

    [Tooltip("World-space control points for the road centerline (Catmull-Rom spline). " +
             "Y coordinate sets the road elevation.")]
    public Vector3[] controlPoints = new Vector3[]
    {
        new Vector3(0f, 0f, 0f),
        new Vector3(50f, 0f, 100f),
        new Vector3(100f, 0f, 200f)
    };

    // ─── Road Properties ─────────────────────────────────────────────────────

    [Header("Road Properties")]
    [Tooltip("Width of the road in world units.")]
    [Min(0.5f)] public float width = 8f;

    [Tooltip("How aggressively the terrain is flattened under the road " +
             "(0 = no flattening, 1 = perfectly flat at road elevation).")]
    [Range(0f, 1f)] public float flatness = 0.85f;

    [Tooltip("Width of the blend zone from the road edge back to natural terrain, in world units.")]
    [Min(0f)] public float smoothingFalloff = 14f;

    [Tooltip("Global terrain layer index (in TerrainData.terrainLayers) to paint along the road.")]
    [Min(0)] public int textureLayerIndex = 0;

    [Tooltip("Lateral camber: slight side-to-side slope for drainage aesthetics. " +
             "Positive = left side higher when looking along road direction.")]
    [Range(-0.15f, 0.15f)] public float camber = 0.01f;
}
