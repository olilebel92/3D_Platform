using UnityEngine;

/// <summary>A single entry in an enemy's drop table.</summary>
[System.Serializable]
public class DropEntry
{
    [Tooltip("Prefab to spawn when this entry drops (e.g. Coin, HealthOrb).")]
    public GameObject prefab;

    [Tooltip("Probability this entry drops on death. 1 = always, 0 = never.")]
    [Range(0f, 1f)]
    public float dropChance = 0.5f;

    [Tooltip("Minimum number of this prefab to spawn.")]
    public int minCount = 1;

    [Tooltip("Maximum number of this prefab to spawn.")]
    public int maxCount = 1;
}
