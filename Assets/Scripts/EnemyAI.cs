using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

public class EnemyAI : NetworkBehaviour
{
    // ─── References ───────────────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("Leave blank — target is resolved automatically from connected players.")]
    public Transform player;
    private NavMeshAgent agent;
    private Animator animator;
    private HealthSystem _playerHealth;
    private NetworkObject _targetNetObj;   // used in MP to route damage to the correct client

    // ─── Detection & Attack ───────────────────────────────────────────────────
    [Header("Range Settings")]
    [Tooltip("Enemy starts chasing the nearest player within this range.")]
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

    // ─── Multi-target retarget interval ──────────────────────────────────────
    [Header("Multiplayer")]
    [Tooltip("How often (seconds) the enemy re-evaluates the nearest player.")]
    public float retargetInterval = 1f;
    private float retargetTimer = 0f;

    // ─── Animator Parameter Names ─────────────────────────────────────────────
    private static readonly int AnimWalk   = Animator.StringToHash("Walk");
    private static readonly int AnimAttack = Animator.StringToHash("Attack");
    private static readonly int AnimIdle   = Animator.StringToHash("Idle");

    // ─── Networked Health ─────────────────────────────────────────────────────
    // Read by EnemyHealthBar on all clients to drive the HP bar UI.

    [HideInInspector]
    public NetworkVariable<int> NetworkHealth = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [HideInInspector]
    public NetworkVariable<int> NetworkMaxHealth = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ─── State ────────────────────────────────────────────────────────────────
    private enum State { Idle, Chase, Attack }
    private State currentState = State.Idle;

    // ─── NGO Lifecycle ────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        // Initialise NetworkVariables here — OnNetworkSpawn fires during
        // NetworkObject.Spawn() when the object IS registered with NGO,
        // so writes are valid and won't trigger the "not spawned yet" warning.
        // maxHealth is an Inspector field, always ready before Start().
        if (IsServer)
        {
            HealthSystem h = GetComponent<HealthSystem>();
            if (h != null)
            {
                NetworkHealth.Value    = h.maxHealth;
                NetworkMaxHealth.Value = h.maxHealth;
            }
        }
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        agent    = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();

        if (agent != null)
            agent.speed = moveSpeed;
    }

    void Update()
    {
        // ── Authority check ───────────────────────────────────────────────────
        // In multiplayer: only the server runs AI.
        // In solo/offline: always run (NetworkObject is not spawned via NGO).
        if (!ShouldRunAI()) return;

        // Re-evaluate nearest player on interval
        retargetTimer -= Time.deltaTime;
        if (retargetTimer <= 0f)
        {
            retargetTimer = retargetInterval;
            RefreshNearestTarget();
        }

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
            case State.Idle:   HandleIdle();   break;
            case State.Chase:  HandleChase();  break;
            case State.Attack: HandleAttack(); break;
        }
    }

    // ─── Nearest-Player Resolution ────────────────────────────────────────────
    // Scans all GameObjects tagged "Player" and picks the closest one.
    // Called on a timer to avoid per-frame FindGameObjectsWithTag overhead.
    void RefreshNearestTarget()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        if (players == null || players.Length == 0)
        {
            player        = null;
            _playerHealth = null;
            _targetNetObj = null;
            return;
        }

        GameObject nearest     = null;
        float      nearestDist = float.MaxValue;

        foreach (GameObject p in players)
        {
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < nearestDist)
            {
                nearestDist = d;
                nearest     = p;
            }
        }

        if (nearest != null && nearest.transform != player)
        {
            player        = nearest.transform;
            _playerHealth = nearest.GetComponent<HealthSystem>();
            _targetNetObj = nearest.GetComponent<NetworkObject>();

            if (_playerHealth == null)
                Debug.LogWarning("[EnemyAI] Nearest player has no HealthSystem.");
        }
    }

    // ─── State Handlers ───────────────────────────────────────────────────────
    void HandleIdle()
    {
        if (agent != null) agent.ResetPath();
        SetAnimation(AnimIdle);
    }

    void HandleChase()
    {
        if (agent != null)
            agent.SetDestination(player.position);
        SetAnimation(AnimWalk);
    }

    void HandleAttack()
    {
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

            // Trigger attack animation on all clients
            if (animator != null)
                TriggerAttackAnimClientRpc();

            // ── Apply damage ──────────────────────────────────────────────────
            // Solo: call TakeDamage directly on the HealthSystem.
            // MP:   HealthSystem has no NetworkVariable so a server-side call
            //       won't update the owning client's HP or UI. Instead, send a
            //       targeted ClientRpc to the player's owning client so damage
            //       is applied locally where the UI references live.
            bool networkActive = NetworkManager.Singleton != null
                              && NetworkManager.Singleton.IsListening;

            if (!networkActive)
            {
                if (_playerHealth != null)
                    _playerHealth.TakeDamage(attackDamage);
            }
            else if (_targetNetObj != null)
            {
                ClientRpcParams ownerOnly = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new[] { _targetNetObj.OwnerClientId }
                    }
                };
                DealDamageClientRpc(attackDamage, ownerOnly);
            }
        }

        SetAnimation(AnimIdle);
    }

    // ─── Network RPCs ─────────────────────────────────────────────────────────

    /// <summary>
    /// Called by non-server clients (e.g. PlayerAttack) to deal damage to this enemy.
    /// RequireOwnership = false because the attacking player does not own the enemy.
    /// Running damage on the server ensures Destroy() is called from the correct authority.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void TakeDamageServerRpc(int damage, bool isCrit)
    {
        HealthSystem health = GetComponent<HealthSystem>();
        if (health == null) return;

        health.TakeDamage(damage, isCrit);
        NetworkHealth.Value = health.currentHealth;
        Debug.Log($"[EnemyAI] NetworkHealth set to {NetworkHealth.Value} on {gameObject.name}");
    }

    // Fires the Attack trigger on every client so the animation plays everywhere.
    [ClientRpc]
    void TriggerAttackAnimClientRpc()
    {
        if (animator != null)
            animator.SetTrigger(AnimAttack);
    }

    // Sent only to the targeted player's owning client.
    // Finds the locally-owned player on that machine and applies damage directly,
    // so HealthSystem.UpdateHealthUI() runs where the UI references actually live.
    // After applying damage, syncs the new health back to the server so server-side
    // checks (e.g. HealingWave heal eligibility) see the real value.
    [ClientRpc]
    private void DealDamageClientRpc(int damage, ClientRpcParams clientRpcParams = default)
    {
        foreach (GameObject p in GameObject.FindGameObjectsWithTag("Player"))
        {
            NetworkObject net = p.GetComponent<NetworkObject>();
            if (net != null && net.IsOwner)
            {
                HealthSystem health = p.GetComponent<HealthSystem>();
                if (health != null)
                {
                    health.TakeDamage(damage);
                    PlayerController pc = p.GetComponent<PlayerController>();
                    pc?.SyncServerHealthServerRpc(health.currentHealth);
                }
                return;
            }
        }
    }

    // ─── Animation Helper ─────────────────────────────────────────────────────
    void SetAnimation(int stateHash)
    {
        if (animator == null) return;

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

    // ─── Solo / MP Helper ─────────────────────────────────────────────────────
    private bool ShouldRunAI()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return true;   // solo / offline — always run
        return IsServer;   // multiplayer — server only
    }

    // ─── Public Method for Spawner ────────────────────────────────────────────
    // EnemySpawner can still call this to pre-assign a target on spawn;
    // the retarget loop will override it with the nearest player if needed.
    public void SetTarget(Transform target)
    {
        player        = target;
        _playerHealth = target != null ? target.GetComponent<HealthSystem>() : null;
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
