using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Unity.Netcode;

public class EnemyAI : NetworkBehaviour
{
    // ─── Enemy Data ───────────────────────────────────────────────────────────
    [Header("Enemy Data")]
    [Tooltip("Optional ScriptableObject. When assigned, overrides all stat fields below at runtime.")]
    [SerializeField] private EnemyData _data;

    public string EnemyDisplayName => _data != null ? _data.enemyName : gameObject.name;
    public int    EnemyLevel        => _data != null ? _data.level     : 1;

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
    [Tooltip("Minimum damage dealt per attack (inclusive).")]
    public int attackDamageMin = 1;
    [Tooltip("Maximum damage dealt per attack (inclusive).")]
    public int attackDamageMax = 2;
    [Tooltip("Chance (0–1) to stun the target on a successful attack.")]
    [Range(0f, 1f)]
    public float attackStunChance = 0.2f;
    [Tooltip("Duration (seconds) of the stun applied on a successful stun roll.")]
    public float attackStunDuration = 1f;
    [Tooltip("Seconds the enemy stops moving after triggering an attack (match to attack animation length).")]
    public float attackMoveLockDuration = 0.6f;
    private float attackTimer = 0f;
    private float attackMoveLockTimer = 0f;

    [Header("Movement")]
    public float moveSpeed = 3f;
    [Tooltip("NavMeshAgent angular speed while chasing (deg/sec).")]
    public float angularSpeed = 200f;
    [Tooltip("Rotation speed while attacking (deg/sec).")]
    public float rotationSpeed = 200f;

    [Header("Separation")]
    [Tooltip("Radius within which this enemy pushes away from other enemies.")]
    [SerializeField] private float _separationRadius = 1.2f;
    [Tooltip("Strength of the separation push (world units/sec).")]
    [SerializeField] private float _separationStrength = 3f;

    [Header("Visual")]
    [Tooltip("Width of the toon diffuse blend window around the lit/shadow threshold. " +
             "Increase (e.g. 0.1–0.2) to stop toon bands snapping on low-poly meshes during idle.")]
    [SerializeField] private float _diffuseSmoothingHalfWidth = 0.08f;

    // ─── Multi-target retarget interval ──────────────────────────────────────
    [Header("Multiplayer")]
    [Tooltip("How often (seconds) the enemy re-evaluates the nearest player.")]
    public float retargetInterval = 1f;
    private float retargetTimer = 0f;

    // ─── Animator Parameter Names ─────────────────────────────────────────────
    private static readonly int AnimWalk   = Animator.StringToHash("Walk");
    private static readonly int AnimAttack = Animator.StringToHash("Attack");
    private static readonly int AnimIdle   = Animator.StringToHash("Idle");
    private static readonly int AnimDeath  = Animator.StringToHash("Death");
    private bool animHasWalk;
    private bool animHasIdle;
    private bool animHasDeath;

    // ─── State ────────────────────────────────────────────────────────────────
    private enum State { Idle, Chase, Attack }
    private State currentState = State.Idle;

    // ─── NGO Lifecycle ────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        // HealthSystem.OnNetworkSpawn seeds the HP NetworkVariables from its own
        // inspector value. ApplyData (below) overrides them with EnemyData if assigned.
        ApplyData();
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        // ApplyData runs here for singleplayer (OnNetworkSpawn doesn't fire without NGO).
        ApplyData();

        agent    = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        animHasWalk  = HasAnimParam(AnimWalk);
        animHasIdle  = HasAnimParam(AnimIdle);
        animHasDeath = HasAnimParam(AnimDeath);

        if (agent != null)
        {
            agent.speed                 = moveSpeed;
            agent.angularSpeed          = angularSpeed;
            agent.obstacleAvoidanceType = UnityEngine.AI.ObstacleAvoidanceType.GoodQualityObstacleAvoidance;
            agent.avoidancePriority     = Random.Range(20, 80);
        }

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            r.receiveShadows    = false;
            // TwoSided fills back-face gaps in the shadow projection for animated meshes.
            r.shadowCastingMode = r is SkinnedMeshRenderer
                ? ShadowCastingMode.TwoSided
                : ShadowCastingMode.On;

            // Toon Shaders Pro ignores receiveShadows — zero out shadow sampling via
            // MaterialPropertyBlock so toon step-bands don't shift under shadows.
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetFloat("_ReceiveShadows", 0f);
            // Widen the toon diffuse smoothstep window so low-poly normals don't
            // snap the lit/shadow band during subtle idle movement.
            float hw = _diffuseSmoothingHalfWidth;
            mpb.SetVector("_DiffuseThresholds", new Vector4(-hw, hw, 0f, 0f));
            r.SetPropertyBlock(mpb);
        }

        HealthSystem h = GetComponent<HealthSystem>();
        if (EnemyHealthBarManager.Instance != null && h != null)
            EnemyHealthBarManager.Instance.AttachTo(this, h, transform);
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

        // ── Move-lock: keep enemy frozen in Attack state for the animation window
        if (attackMoveLockTimer > 0f)
        {
            attackMoveLockTimer -= Time.deltaTime;
            currentState = State.Attack;
        }

        // ── Run State Logic ──────────────────────────────────────────────────
        switch (currentState)
        {
            case State.Idle:   HandleIdle();   break;
            case State.Chase:  HandleChase();  break;
            case State.Attack: HandleAttack(); break;
        }

        ApplySeparation();
    }

    // ─── Nearest-Player Resolution ────────────────────────────────────────────
    // Scans all GameObjects tagged "Player" and picks the closest one.
    // Called on a timer to avoid per-frame FindGameObjectsWithTag overhead.
    void RefreshNearestTarget()
    {
        var players = PlayerController.All;
        if (players == null || players.Count == 0)
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

    // ─── Separation Steering ──────────────────────────────────────────────────

    // Static reuse buffer keeps separation queries GC-free across all enemies.
    private static readonly Collider[] s_separationBuffer = new Collider[16];

    void ApplySeparation()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        int count = Physics.OverlapSphereNonAlloc(transform.position, _separationRadius, s_separationBuffer);
        Vector3 push = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            Collider col = s_separationBuffer[i];
            if (col.gameObject == gameObject) continue;
            if (!col.CompareTag("Enemy")) continue;

            Vector3 away = transform.position - col.transform.position;
            float dist = away.magnitude;
            if (dist < 0.001f) continue;

            push += away.normalized * (1f - dist / _separationRadius);
        }

        if (push != Vector3.zero)
            agent.Move(push * (_separationStrength * Time.deltaTime));
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

        // Smoothly face the player while attacking
        Vector3 direction = (player.position - transform.position).normalized;
        direction.y = 0f;
        if (direction != Vector3.zero)
        {
            Quaternion target = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target, rotationSpeed * Time.deltaTime);
        }

        // Cooldown timer
        attackTimer -= Time.deltaTime;
        if (attackTimer <= 0f)
        {
            attackTimer = attackCooldown;
            attackMoveLockTimer = attackMoveLockDuration;

            bool networkActive = NetworkManager.Singleton != null
                              && NetworkManager.Singleton.IsListening;

            // Trigger attack animation. In MP the ClientRpc broadcasts to all
            // clients; in solo we trigger locally (no NetworkManager listening).
            if (animator != null)
            {
                if (networkActive)
                    TriggerAttackAnimClientRpc();
                else
                    animator.SetTrigger(AnimAttack);
            }

            // ── Apply damage ──────────────────────────────────────────────────
            // HealthSystem auto-routes to the server in MP and applies directly in solo.
            // The NetworkVariable broadcast updates the target client's UI automatically.
            int   rolledDamage = Random.Range(attackDamageMin, attackDamageMax + 1);
            float stunDuration = (Random.value < attackStunChance) ? attackStunDuration : 0f;

            if (_playerHealth != null)
                _playerHealth.TakeDamage(rolledDamage);

            // Stun is a client-side effect — apply locally in solo, target owner in MP.
            if (stunDuration > 0f)
            {
                if (!networkActive)
                {
                    if (player != null)
                        player.GetComponent<StatusEffectHandler>()?.ApplyStun(stunDuration);
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
                    ApplyStunOnOwnerClientRpc(stunDuration, ownerOnly);
                }
            }
        }

        SetAnimation(AnimIdle);
    }

    // ─── Network RPCs ─────────────────────────────────────────────────────────

    // Fires the Attack trigger on every client so the animation plays everywhere.
    [ClientRpc]
    void TriggerAttackAnimClientRpc()
    {
        if (animator != null)
            animator.SetTrigger(AnimAttack);
    }

    /// <summary>
    /// Plays the Death animation and stops the agent. Called by HealthSystem.Die()
    /// before the AI is disabled so the death clip can run during deathDelay.
    /// In MP, broadcasts to all clients so the animation plays everywhere before despawn.
    /// </summary>
    public void PlayDeathAnimation()
    {
        if (agent != null && agent.isOnNavMesh)
            agent.ResetPath();

        if (animator == null || !animHasDeath) return;

        bool networkActive = NetworkManager.Singleton != null
                          && NetworkManager.Singleton.IsListening;
        if (networkActive)
            TriggerDeathAnimClientRpc();
        else
            animator.SetTrigger(AnimDeath);
    }

    // Fires the Death trigger on every client so the death clip plays everywhere
    // before the server-side Destroy() replicates the despawn.
    [ClientRpc]
    void TriggerDeathAnimClientRpc()
    {
        if (animator != null && animHasDeath)
            animator.SetTrigger(AnimDeath);
    }

    // Sent only to the targeted player's owning client. Applies stun locally.
    // HP is replicated server→clients via HealthSystem's NetworkVariable — not in this RPC.
    [ClientRpc]
    private void ApplyStunOnOwnerClientRpc(float stunDuration, ClientRpcParams clientRpcParams = default)
    {
        foreach (GameObject p in PlayerController.All)
        {
            NetworkObject net = p.GetComponent<NetworkObject>();
            if (net != null && net.IsOwner)
            {
                p.GetComponent<StatusEffectHandler>()?.ApplyStun(stunDuration);
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
            if (animHasWalk) animator.SetBool(AnimWalk, true);
            if (animHasIdle) animator.SetBool(AnimIdle, false);
        }
        else
        {
            if (animHasWalk) animator.SetBool(AnimWalk, false);
            if (animHasIdle) animator.SetBool(AnimIdle, true);
        }
    }

    private bool HasAnimParam(int hash)
    {
        if (animator == null) return false;
        foreach (var p in animator.parameters)
            if (p.nameHash == hash) return true;
        return false;
    }

    // ─── Solo / MP Helper ─────────────────────────────────────────────────────
    private bool ShouldRunAI()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return true;   // solo / offline — always run
        return IsServer;   // multiplayer — server only
    }

    // ─── Public Methods for Spawner ───────────────────────────────────────────

    /// <summary>Assign an EnemyData asset and immediately apply its values. Call before Spawn().</summary>
    public void SetData(EnemyData data)
    {
        _data = data;
        ApplyData();
    }

    // Difficulty multipliers — set by WaveManager before Spawn so ApplyData composes
    // them with the EnemyData SO values. Default 1f for non-WaveManager spawns.
    private float _hpMult  = 1f;
    private float _dmgMult = 1f;
    private float _spdMult = 1f;

    /// <summary>Sets per-wave scaling multipliers. ApplyData multiplies the SO values by these.</summary>
    public void SetDifficultyMultipliers(float hp, float dmg, float spd)
    {
        _hpMult  = hp;
        _dmgMult = dmg;
        _spdMult = spd;
    }

    // EnemySpawner can still call this to pre-assign a target on spawn;
    // the retarget loop will override it with the nearest player if needed.
    public void SetTarget(Transform target)
    {
        player        = target;
        _playerHealth = target != null ? target.GetComponent<HealthSystem>() : null;
    }

    // ─── Data Application ─────────────────────────────────────────────────────

    private void ApplyData()
    {
        if (_data == null) return;

        // Multipliers default to 1f and are set by WaveManager.SetDifficultyMultipliers before Spawn.
        moveSpeed          = _data.moveSpeed * _spdMult;
        angularSpeed       = _data.angularSpeed;
        rotationSpeed      = _data.rotationSpeed;
        attackDamageMin    = Mathf.Max(1, Mathf.RoundToInt(_data.attackDamageMin * _dmgMult));
        attackDamageMax    = Mathf.Max(attackDamageMin, Mathf.RoundToInt(_data.attackDamageMax * _dmgMult));
        attackCooldown     = _data.attackCooldown;
        attackRange        = _data.attackRange;
        detectionRange     = _data.detectionRange;
        attackStunChance   = _data.attackStunChance;
        attackStunDuration    = _data.attackStunDuration;
        attackMoveLockDuration = _data.attackMoveLockDuration;
        retargetInterval       = _data.retargetInterval;

        // Server-authority: only the server (or solo player) writes HealthSystem.
        // Clients receive HP via HealthSystem's NetworkVariables.
        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (!networkActive || IsServer)
        {
            HealthSystem h = GetComponent<HealthSystem>();
            if (h != null)
            {
                float scaledMax = Mathf.Max(1f, _data.maxHealth * _hpMult);
                h.InitializeServerHP(scaledMax, scaledMax);
                h.destroyOnDeath = true;   // SO-driven enemies always despawn on death
                h.keepColliderOnDeath = _data.category == EnemyCategory.Boss;
            }
        }

        if (agent != null)
        {
            agent.speed        = moveSpeed;
            agent.angularSpeed = angularSpeed;
        }
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
