using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Listens for the Fire input action and triggers a melee attack animation.
/// Deals damage to any enemies within attackRadius using a sphere overlap check.
/// Attack damage scales with STR; attack cooldown is reduced by AGI.
/// Both bonuses are configured in ExperienceManager's Inspector.
///
/// IMPORTANT: Does NOT create its own PlayerInputActions instance.
/// It borrows the one owned by PlayerController (on the same GameObject)
/// to avoid double-enable / double-disable conflicts.
///
/// Animator setup required:
///   - Add a Trigger parameter named "Attack"
///   - Any State → Attack: no exit time, duration 0, condition = Attack trigger
///   - Attack → Any State: Has Exit Time checked, Exit Time 1.0, duration 0, no conditions
///   - Player state must have a Motion assigned (e.g. an Idle clip)
/// </summary>
public class PlayerAttack : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Attack Settings")]
    [Tooltip("Base cooldown in seconds. AGI reduces this — tune agiCooldownReduction " +
             "in ExperienceManager.")]
    public float attackCooldown = 0.8f;

    [Tooltip("Fallback damage if ExperienceManager is not in the scene.")]
    public int attackDamage = 1;

    [Tooltip("Radius of the hit detection sphere in front of the player.")]
    public float attackRadius = 1.5f;

    [Tooltip("How far in front of the player the hit sphere is centered.")]
    public float attackOffset = 1f;

    [Tooltip("Only GameObjects with this tag will be damaged.")]
    public string enemyTag = "Enemy";

    [Header("References")]
    [Tooltip("Leave blank — auto-found on this GameObject at Start.")]
    public Animator animator;

    // ─── Animator Parameter ───────────────────────────────────────────────────

    private static readonly int AnimAttack = Animator.StringToHash("Attack");

    // ─── Private State ────────────────────────────────────────────────────────

    // No local PlayerInputActions — we borrow PlayerController's shared instance.
    private PlayerInputActions _inputActions;
    private float _cooldownTimer = 0f;
    private bool _isAttacking = false;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        // ── Borrow input actions from PlayerController ────────────────────────
        // Both scripts live on the same GameObject; PlayerController owns the
        // single PlayerInputActions instance and is responsible for Enable/Disable.
        PlayerController pc = GetComponent<PlayerController>();
        if (pc != null)
        {
            _inputActions = pc.InputActions;
        }
        else
        {
            // Fallback: create our own instance only if PlayerController is absent.
            Debug.LogWarning("[PlayerAttack] PlayerController not found on this GameObject. " +
                             "Creating a standalone PlayerInputActions instance as fallback. " +
                             "Ensure Fire action map is enabled.");
            _inputActions = new PlayerInputActions();
            _inputActions.Player.Enable();
        }

        if (_inputActions != null)
            _inputActions.Player.Fire.performed += OnFire;

        // ── Animator ──────────────────────────────────────────────────────────
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null)
            Debug.LogWarning("[PlayerAttack] No Animator found on " + gameObject.name +
                             " or its children.");
    }

    void OnDestroy()
    {
        // Always unsubscribe to avoid memory leaks / stale callbacks.
        if (_inputActions != null)
            _inputActions.Player.Fire.performed -= OnFire;
    }

    void Update()
    {
        // ── Cooldown Tick ─────────────────────────────────────────────────────
        if (_isAttacking)
        {
            _cooldownTimer -= Time.deltaTime;
            if (_cooldownTimer <= 0f)
                _isAttacking = false;
        }
    }

    // ─── Input Callback ───────────────────────────────────────────────────────

    private void OnFire(InputAction.CallbackContext ctx)
    {
        if (_isAttacking) return;
        if (animator == null) return;

        // ── Trigger animation ─────────────────────────────────────────────────
        animator.SetTrigger(AnimAttack);

        // ── Deal damage ───────────────────────────────────────────────────────
        DamageEnemiesInRange();

        _isAttacking = true;

        // ── AGI Cooldown Reduction ────────────────────────────────────────────
        // ExperienceManager subtracts (agility × agiCooldownReduction) from the
        // base cooldown set in this Inspector, floored at agiMinAttackCooldown.
        _cooldownTimer = ExperienceManager.Instance != null
            ? ExperienceManager.Instance.ComputedAttackCooldown(attackCooldown)
            : attackCooldown;

        Debug.Log($"[PlayerAttack] Attack triggered. Effective cooldown: {_cooldownTimer:F2}s");
    }

    // ─── Hit Detection ────────────────────────────────────────────────────────

    private void DamageEnemiesInRange()
    {
        Vector3 hitCenter = transform.position + transform.forward * attackOffset;
        Collider[] hits = Physics.OverlapSphere(hitCenter, attackRadius);

        // ── STR Damage ────────────────────────────────────────────────────────
        // Uses STR from ExperienceManager; falls back to Inspector value if missing.
        int damage = ExperienceManager.Instance != null
            ? ExperienceManager.Instance.strength
            : attackDamage;

        foreach (Collider hit in hits)
        {
            if (!hit.CompareTag(enemyTag)) continue;

            HealthSystem health = hit.GetComponent<HealthSystem>();
            if (health != null)
            {
                health.TakeDamage(damage);
                Debug.Log($"[PlayerAttack] Hit {hit.gameObject.name} for {damage} damage " +
                          $"(STR={damage}).");
            }
        }
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 hitCenter = transform.position + transform.forward * attackOffset;
        Gizmos.DrawWireSphere(hitCenter, attackRadius);
    }
}