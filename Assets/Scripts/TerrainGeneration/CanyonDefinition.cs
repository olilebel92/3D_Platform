using System;
using UnityEngine;

/// <summary>
/// Serializable data class defining a canyon or valley carved into the terrain.
/// Canyons are evaluated as Catmull-Rom splines and merged gracefully when overlapping.
/// </summary>
[Serializable]
public class CanyonDefinition
{
    // ─── Identity ────────────────────────────────────────────────────────────

    [Tooltip("Display name shown in the TerrainGeneratorWindow.")]
    public string canyonName = "Canyon";

    // ─── Spline ──────────────────────────────────────────────────────────────

    [Tooltip("World-space control points for the canyon centerline (Catmull-Rom spline).")]
    public Vector3[] controlPoints = new Vector3[]
    {
        new Vector3(0f, 0f, 0f),
        new Vector3(50f, 0f, 100f),
        new Vector3(100f, 0f, 200f)
    };

    // ─── Canyon Shape ────────────────────────────────────────────────────────

    [Header("Canyon Shape")]
    [Tooltip("How deep the canyon floor is below the surrounding terrain, in world units.")]
    [Min(0f)] public float depth = 30f;

    [Tooltip("Canyon width at the rim (widest point), in world units.")]
    [Min(1f)] public float width = 40f;

    [Tooltip("Wall steepness when using the simple profile: 0 = near-vertical cliff, 1 = gentle slope.")]
    [Range(0f, 1f)] public float wallProfile = 0.5f;

    [Tooltip("Custom wall profile curve. X = normalized distance from center (0=center, 1=rim). " +
             "Y = normalized depth multiplier (1=full depth, 0=no carve). " +
             "Overrides wallProfile when the curve has more than the two default keys.")]
    public AnimationCurve wallCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Tooltip("Smoothness of the canyon rim: 0 = knife edge, 1 = wide smooth blend into terrain.")]
    [Range(0f, 1f)] public float smoothness = 0.4f;

    // ─── Floor ───────────────────────────────────────────────────────────────

    [Header("Floor")]
    [Tooltip("Flatten the canyon floor to create a valley rather than a V-shape.")]
    public bool flatFloor = false;

    [Tooltip("Fraction of canyon width that is flattened into a floor (0 = none, 1 = full width).")]
    [Range(0f, 1f)] public float floorFraction = 0.3f;

    // ─── Erosion ─────────────────────────────────────────────────────────────

    [Header("Erosion")]
    [Tooltip("Frequency of Perlin noise added to canyon walls to simulate erosion detail.")]
    [Min(0f)] public float erosionNoiseFrequency = 0.05f;

    [Tooltip("Maximum depth variation from erosion noise, in world units.")]
    [Min(0f)] public float erosionNoiseAmplitude = 3f;
}
