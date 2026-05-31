using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

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
public class PlayerAttack : NetworkBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Attack Settings")]
    [Tooltip("Base cooldown in seconds. AGI reduces this — tune agiCooldownReduction " +
             "in ExperienceManager.")]
    public float attackCooldown = 0.8f;

    [Tooltip("How long the hitbox stays active after the attack starts (should match the swing portion of the animation).")]
    public float attackActiveDuration = 0.5f;

    [Tooltip("Fallback damage if ExperienceManager is not in the scene.")]
    public int attackDamage = 1;

    [Tooltip("Radius of the hit detection sphere in front of the player.")]
    public float attackRadius = 1.5f;

    [Tooltip("How far in front of the player the hit sphere is centered.")]
    public float attackOffset = 1f;

    [Tooltip("Only GameObjects with this tag will be damaged.")]
    public string enemyTag = "Enemy";

    [Header("Audio")]
    [Tooltip("Sound played when the player swings / attacks.")]
    public AudioClip attackSwingClip;

    [Tooltip("AudioSource used to play attack sounds. Auto-found if blank.")]
    public AudioSource audioSource;

    [Tooltip("Minimum pitch multiplier for swing sound.")]
    [SerializeField] private float swingPitchMin = 0.9f;
    [Tooltip("Maximum pitch multiplier for swing sound.")]
    [SerializeField] private float swingPitchMax = 1.1f;

    [Header("References")]
    [Tooltip("Leave blank — auto-found on this GameObject at Start.")]
    public Animator animator;

    // ─── Animator Parameter ───────────────────────────────────────────────────

    private static readonly int AnimAttack = Animator.StringToHash("Attack");

    // ─── Private State ────────────────────────────────────────────────────────

    // No local PlayerInputActions — we borrow PlayerController's shared instance.
    private PlayerInputActions _inputActions;
    private ExperienceManager _xp;
    private SpellCaster _spellCaster;
    private StatusEffectHandler _statusEffects;
    private float _cooldownTimer = 0f;
    private float _activeWindowTimer = 0f;
    private bool _isAttacking = false;
    private bool _attackWindowActive = false;
    private readonly HashSet<int> _hitThisSwing = new HashSet<int>();

    // Index of the UppgerBody layer in the Animator controller.
    private int _upperBodyLayerIndex = -1;

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

        // Cache own ExperienceManager — each player has their own instance.
        _xp            = GetComponent<ExperienceManager>();
        _spellCaster   = GetComponent<SpellCaster>();
        _statusEffects = GetComponent<StatusEffectHandler>();

        if (_inputActions == null)
        {
            // Fallback: PlayerController absent — use the shared instance directly.
            // PlayerController owns enable/disable of the Player map.
            _inputActions = InputManager.Actions;
        }

        // No event subscription needed — fire input is polled each frame in Update.

        // ── Animator ──────────────────────────────────────────────────────────
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null)
        {
            Debug.LogWarning("[PlayerAttack] No Animator found on " + gameObject.name +
                             " or its children.");
        }
        else
        {
            // Cache the UppgerBody layer index and start with weight 0
            // so the Base Layer drives the arms during idle/walk/sprint.
            _upperBodyLayerIndex = animator.GetLayerIndex("UpperBody");
            if (_upperBodyLayerIndex >= 0)
                animator.SetLayerWeight(_upperBodyLayerIndex, 0f);
            else
                Debug.LogWarning("[PlayerAttack] 'UpperBody' layer not found in Animator.");
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
    }

    // ─── NGO Lifecycle ────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        // Non-owners have no business running hit detection or receiving input.
        // Disabling the component stops Update() and prevents Start() from
        // running on remote player instances.
        if (!IsOwner)
        {
            enabled = false;
            return;
        }
    }

    void Update()
    {
        // ── Cooldown Tick ─────────────────────────────────────────────────────
        // Use unscaledDeltaTime so the cooldown still drains when the game is
        // paused (Time.timeScale = 0) during the level-up stat-choice panel.
        // Without this, attacking right as a level-up triggers leaves
        // _isAttacking permanently true and blocks all future attacks.
        if (_isAttacking)
        {
            _cooldownTimer -= Time.unscaledDeltaTime;
            if (_cooldownTimer <= 0f)
            {
                _isAttacking = false;

                // Drop the upper body layer weight back to 0 so the
                // Base Layer resumes driving the arms during locomotion.
                if (_upperBodyLayerIndex >= 0)
                    animator.SetLayerWeight(_upperBodyLayerIndex, 0f);
            }
        }

        // ── Auto-attack while LMB is held ─────────────────────────────────────
        if (!_isAttacking && _inputActions != null &&
            _inputActions.Player.Fire.IsPressed())
        {
            TryAttack();
        }

        // ── Active Damage Window ──────────────────────────────────────────────
        if (_attackWindowActive)
        {
            _activeWindowTimer -= Time.unscaledDeltaTime;
            if (_activeWindowTimer <= 0f)
            {
                _attackWindowActive = false;
                _hitThisSwing.Clear();
            }
            else
            {
                DamageEnemiesInRange();
            }
        }
    }

    // ─── Attack Logic ─────────────────────────────────────────────────────────

    private void TryAttack()
    {
        if (_isAttacking) return;
        if (animator == null) return;
        if (Time.timeScale == 0f) return; // block attacks while any popup/menu is open
        if (_statusEffects != null && _statusEffects.IsStunned) return;
        if (_spellCaster != null && _spellCaster.IsActive) return; // block during Target/Cast/Channel

        // ── Trigger animation ─────────────────────────────────────────────────
        // Activate the upper body layer so the attack clip overrides the arms.
        if (_upperBodyLayerIndex >= 0)
            animator.SetLayerWeight(_upperBodyLayerIndex, 1f);

        animator.SetTrigger(AnimAttack);

        // ── Attack sound ──────────────────────────────────────────────────────
        if (audioSource != null && attackSwingClip != null)
        {
            audioSource.pitch = Random.Range(swingPitchMin, swingPitchMax);
            audioSource.PlayOneShot(attackSwingClip);
        }

        // ── Open damage window ────────────────────────────────────────────────
        _hitThisSwing.Clear();
        _attackWindowActive = true;
        _activeWindowTimer = attackActiveDuration;

        _isAttacking = true;

        // ── AGI Cooldown Reduction ────────────────────────────────────────────
        // ExperienceManager subtracts (agility × agiCooldownReduction) from the
        // base cooldown set in this Inspector, floored at agiMinAttackCooldown.
        _cooldownTimer = _xp != null
            ? _xp.ComputedAttackCooldown(attackCooldown)
            : attackCooldown;

        Debug.Log($"[PlayerAttack] Attack triggered. Effective cooldown: {_cooldownTimer:F2}s");
    }

    // ─── Hit Detection ────────────────────────────────────────────────────────

    // Static reuse buffer keeps melee hit queries GC-free.
    private static readonly Collider[] s_meleeBuffer = new Collider[16];

    private void DamageEnemiesInRange()
    {
        Vector3 hitCenter = transform.position + transform.forward * attackOffset;
        int hitCount = Physics.OverlapSphereNonAlloc(hitCenter, attackRadius, s_meleeBuffer);

        // ── Damage Range ──────────────────────────────────────────────────────
        // Roll a value between computed min and max; falls back to Inspector value.
        int baseDamage;
        if (_xp != null)
        {
            int minDmg = _xp.ComputedMinDamage;
            int maxDmg = _xp.ComputedMaxDamage;
            baseDamage = Random.Range(minDmg, maxDmg + 1);
        }
        else
        {
            baseDamage = attackDamage;
        }

        // ── Crit Roll ─────────────────────────────────────────────────────────
        bool isCrit = _xp != null && Random.value < _xp.ComputedCritRate;

        int damage = isCrit && _xp != null
            ? Mathf.RoundToInt(baseDamage * (1f + _xp.ComputedCritDamage))
            : baseDamage;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = s_meleeBuffer[i];
            if (!hit.CompareTag(enemyTag)) continue;

            int id = hit.gameObject.GetInstanceID();
            if (_hitThisSwing.Contains(id)) continue;

            HealthSystem health = hit.GetComponent<HealthSystem>();
            if (health != null)
            {
                // HealthSystem auto-routes to the server in MP and applies directly in solo.
                health.TakeDamage(damage, isCrit);
                _hitThisSwing.Add(id);
                Debug.Log($"[PlayerAttack] Hit {hit.gameObject.name} for {damage} damage " +
                          $"(STR={baseDamage}{(isCrit ? " CRIT!" : "")}).");
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