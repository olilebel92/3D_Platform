using UnityEngine;
using UnityEngine.AI;

public class EnemyAI : MonoBehaviour
{
    // ─── References ───────────────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("Assign the Player Transform here, or leave blank to auto-find by tag.")]
    public Transform player;
    private NavMeshAgent agent;
    private Animator animator;
    private HealthSystem _playerHealth;

    // ─── Detection & Attack ───────────────────────────────────────────────────
    [Header("Range Settings")]
    [Tooltip("Enemy starts chasing the player within this range.")]
    public float detectionRange = 10f;
    [Tooltip("Enemy stops and attacks within this range.")]
    public float attackRange = 2f;

    [Header("Attack Settings")]
    [Tooltip("Seconds between each attack.")]
    public float attackCooldown = 1.5f;
    [Tooltip("Damage dealt per attack.")]
    public int attackDamage = 1;
    private float attackTimer = 0f;

    [Header("Movement")]
    public float moveSpeed = 3f;

    // ─── Animator Parameter Names ─────────────────────────────────────────────
    // These must match EXACTLY what you named them in the Animator
    private static readonly int AnimWalk = Animator.StringToHash("Walk");
    private static readonly int AnimAttack = Animator.StringToHash("Attack");
    private static readonly int AnimIdle = Animator.StringToHash("Idle");

    // ─── State ────────────────────────────────────────────────────────────────
    private enum State { Idle, Chase, Attack }
    private State currentState = State.Idle;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    void Start()
    {
        // Grab components
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();

        // Auto-find player if not assigned
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                player = playerObj.transform;
            else
                Debug.LogWarning("[EnemyAI] No player found! Tag your player as 'Player'.");
        }

        // Cache player HealthSystem to avoid GetComponent every attack tick
        if (player != null)
            _playerHealth = player.GetComponent<HealthSystem>();

        // Apply speed to NavMeshAgent
        if (agent != null)
            agent.speed = moveSpeed;
    }

    void Update()
    {
        if (player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        // ── Determine State ──────────────────────────────────────────────────
        if (distanceToPlayer <= attackRange)
            currentState = State.Attack;
        else if (distanceToPlayer <= detectionRange)
            currentState = State.Chase;
        else
            currentState = State.Idle;

        // ── Run State Logic ──────────────────────────────────────────────────
        switch (currentState)
        {
            case State.Idle:
                HandleIdle();
                break;
            case State.Chase:
                HandleChase();
                break;
            case State.Attack:
                HandleAttack();
                break;
        }
    }

    // ─── State Handlers ───────────────────────────────────────────────────────
    void HandleIdle()
    {
        // Stop moving
        if (agent != null) agent.ResetPath();

        // Play idle animation
        SetAnimation(AnimIdle);
    }

    void HandleChase()
    {
        // Move toward player using NavMesh
        if (agent != null)
            agent.SetDestination(player.position);

        // Play walk animation
        SetAnimation(AnimWalk);
    }

    void HandleAttack()
    {
        // Stop moving when in attack range
        if (agent != null) agent.ResetPath();

        // Always face the player while attacking
        Vector3 direction = (player.position - transform.position).normalized;
        direction.y = 0f;
        if (direction != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(direction);

        // Cooldown timer
        attackTimer -= Time.deltaTime;
        if (attackTimer <= 0f)
        {
            attackTimer = attackCooldown;

            // Trigger attack animation
            if (animator != null)
                animator.SetTrigger(AnimAttack);

            // Deal damage to player
            if (_playerHealth != null)
                _playerHealth.TakeDamage(attackDamage);

        }

        // Stand still while in attack range
        SetAnimation(AnimIdle);
    }

    // ─── Animation Helper ─────────────────────────────────────────────────────
    void SetAnimation(int stateHash)
    {
        if (animator == null) return;

        // Only set bools if the animator has those parameters
        if (stateHash == AnimWalk)
        {
            animator.SetBool(AnimWalk, true);
            animator.SetBool(AnimIdle, false);
        }
        else
        {
            animator.SetBool(AnimWalk, false);
            animator.SetBool(AnimIdle, true);
        }
    }

    // ─── Public Method for Spawner ────────────────────────────────────────────
    // Called by EnemySpawner to assign the player target after spawning
    public void SetTarget(Transform target)
    {
        player = target;
        _playerHealth = target != null ? target.GetComponent<HealthSystem>() : null;
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        // Yellow = detection range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Red = attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}