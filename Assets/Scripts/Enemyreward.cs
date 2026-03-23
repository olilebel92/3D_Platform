using UnityEngine;

/// <summary>
/// Add this component to an enemy prefab to grant XP when the enemy dies.
/// Works automatically alongside HealthSystem — no wiring required.
/// Adjust xpReward in the Inspector per enemy type.
/// </summary>
public class EnemyReward : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Reward Settings")]
    [Tooltip("Amount of XP awarded to the player when this enemy is destroyed.")]
    public int xpReward = 50;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void OnDestroy()
    {
        // Only award XP during actual gameplay, not when the scene is unloading
        if (!gameObject.scene.isLoaded) return;

        if (ExperienceManager.Instance != null)
        {
            ExperienceManager.Instance.GainXP(xpReward);
            Debug.Log("[EnemyReward] Awarded " + xpReward + " XP for killing " + gameObject.name);
        }
        else
        {
            Debug.LogWarning("[EnemyReward] ExperienceManager instance not found in scene!");
        }
    }
}