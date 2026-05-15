using UnityEngine;

// ─── Enums ────────────────────────────────────────────────────────────────────

/// <summary>The creature's race or species — used for tagging, filtering, and future resistances.</summary>
public enum CreatureType { Undead, Human, Orc, Slime }

/// <summary>The enemy's role tier — drives difficulty design and loot scaling.</summary>
public enum EnemyCategory { Normal, Elite, Boss, Magic, Hunter }

// ─── Enemy Data ───────────────────────────────────────────────────────────────

/// <summary>
/// Data-driven enemy definition. Assign to EnemyAI and EnemyReward to override
/// their Inspector values at runtime. One prefab can represent many enemy variants
/// by swapping this asset.
/// Right-click → Create → Enemies → Enemy Data.
/// </summary>
[CreateAssetMenu(fileName = "NewEnemy", menuName = "Enemies/Enemy Data")]
public class EnemyData : ScriptableObject
{
    // ─── Identity ─────────────────────────────────────────────────────────────

    [Header("Identity")]
    [Tooltip("Display name shown in UI and logs.")]
    public string enemyName = "New Enemy";
    [TextArea(2, 4)] public string description;
    public Sprite icon;

    [Tooltip("The creature's race/species — Undead, Human, Orc, Slime, etc.")]
    public CreatureType creatureType = CreatureType.Undead;

    [Tooltip("Difficulty tier — Normal, Elite, Boss, Magic, Hunter.")]
    public EnemyCategory category = EnemyCategory.Normal;

    [Header("Prefab")]
    [Tooltip("The enemy GameObject prefab to spawn. Must have NetworkObject + EnemyAI. " +
             "When set, EnemySpawner uses this instead of its own prefab field.")]
    public GameObject prefab;

    // ─── Stats ────────────────────────────────────────────────────────────────

    [Header("Stats")]
    [Tooltip("Starting and maximum hit points.")]
    public float maxHealth = 10f;

    [Tooltip("NavMesh movement speed (units/sec).")]
    public float moveSpeed = 3f;

    [Tooltip("Flat damage dealt per attack.")]
    public int attackDamage = 1;

    [Tooltip("Seconds between each attack.")]
    public float attackCooldown = 1.5f;

    [Tooltip("Distance at which the enemy enters attack state.")]
    public float attackRange = 2f;

    [Tooltip("Distance at which the enemy begins chasing.")]
    public float detectionRange = 10f;

    [Tooltip("Probability (0–1) of stunning the target on a successful attack.")]
    [Range(0f, 1f)] public float attackStunChance = 0.2f;

    [Tooltip("How long (seconds) the stun lasts when triggered.")]
    public float attackStunDuration = 1f;

    [Tooltip("How often (seconds) the enemy re-evaluates the nearest player in multiplayer.")]
    public float retargetInterval = 1f;

    [Tooltip("Rotation speed while chasing (NavMeshAgent angular speed, deg/sec).")]
    public float angularSpeed = 200f;

    [Tooltip("Rotation speed while attacking (deg/sec). Controls how fast the enemy faces the player.")]
    public float rotationSpeed = 200f;

    // ─── Rewards ──────────────────────────────────────────────────────────────

    [Header("Rewards")]
    [Tooltip("XP awarded to every player when this enemy is killed.")]
    public int xpReward = 50;

    [Tooltip("Heal the nearest player on death?")]
    public bool giveHPOnDeath = false;

    [Tooltip("HP restored to the nearest player when giveHPOnDeath is true.")]
    public int hpRewardOnDeath = 1;
}
