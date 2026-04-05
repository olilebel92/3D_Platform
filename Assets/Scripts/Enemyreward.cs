using System.Collections.Generic;
using UnityEngine;

// ─── Enemy Reward ─────────────────────────────────────────────────────────────

/// <summary>
/// Attach to an enemy prefab to grant XP, HP, and/or world drops on death.
/// XP reward can be disabled in the Inspector while WaveManager handles scaling.
/// </summary>
public class EnemyReward : MonoBehaviour
{
    // ─── XP ───────────────────────────────────────────────────────────────────

    [Header("XP Reward")]
    [Tooltip("Uncheck to disable this component's XP grant (e.g. when WaveManager handles XP instead).")]
    public bool enableXPReward = false;

    [Tooltip("Amount of XP awarded to the player when this enemy is destroyed.")]
    public int xpReward = 50;

    // ─── HP ───────────────────────────────────────────────────────────────────

    [Header("HP Reward")]
    [Tooltip("If true, heals the player when this enemy is destroyed.")]
    public bool giveHPOnDeath = false;

    [Tooltip("Amount of HP restored to the player on death. Only used if Give HP On Death is enabled.")]
    public int hpReward = 1;

    // ─── Drop Table ───────────────────────────────────────────────────────────

    [Header("Drop Table")]
    [Tooltip("Each entry is rolled independently on death. Multiple entries can drop at once.")]
    public List<DropEntry> dropTable = new();

    [Tooltip("Radius around the death position in which drops are scattered.")]
    public float dropScatterRadius = 0.8f;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void OnDestroy()
    {
        // Skip rewards when the scene is unloading or play mode is stopping
        if (!gameObject.scene.isLoaded) return;
        if (!Application.isPlaying) return;

        // ── XP ────────────────────────────────────────────────────────────────
        if (enableXPReward && ExperienceManager.Instance != null)
        {
            ExperienceManager.Instance.GainXP(xpReward);
            Debug.Log($"[EnemyReward] Awarded {xpReward} XP for killing {gameObject.name}");
        }

        // ── HP ────────────────────────────────────────────────────────────────
        if (giveHPOnDeath && hpReward > 0)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                HealthSystem health = playerObj.GetComponent<HealthSystem>();
                if (health != null)
                    health.Heal(hpReward);
            }
        }

        // ── Drop Table ────────────────────────────────────────────────────────
        RollDrops();
    }

    // ─── Drop Helpers ─────────────────────────────────────────────────────────

    private void RollDrops()
    {
        if (dropTable == null || dropTable.Count == 0) return;

        foreach (DropEntry entry in dropTable)
        {
            if (entry.prefab == null) continue;
            if (Random.value > entry.dropChance) continue;

            int count = Random.Range(entry.minCount, entry.maxCount + 1);
            for (int i = 0; i < count; i++)
            {
                Vector3 scatter = Random.insideUnitSphere * dropScatterRadius;
                scatter.y = 0f;
                Instantiate(entry.prefab, transform.position + scatter, Quaternion.identity);
            }
        }
    }
}
