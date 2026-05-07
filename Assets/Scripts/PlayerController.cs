using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class PlayerController : NetworkBehaviour
{
    // ─── Movement ─────────────────────────────────────────────────────────────

    [Header("Movement")]
    [Tooltip("Base walk speed before AGI bonuses are applied.")]
    public float moveSpeed = 5f;

    [Tooltip("Sharpness of lock-on blend tree transitions. 1 = smooth, 3+ = snappy.")]
    public float blendSharpness = 2f;

    [Tooltip("Base sprint speed before AGI bonuses are applied.")]
    public float sprintSpeed = 9f;

    public float gravity = -30f;
    public float jumpHeight = 2f;

    // ─── Camera ───────────────────────────────────────────────────────────────

    [Header("Camera")]
    public Transform cameraTransform;

    [Header("Rotation")]
    [Tooltip("How fast the character rotates to face movement direction. Higher = snappier.")]
    public float rotationSpeed = 720f;

    // ─── Audio ────────────────────────────────────────────────────────────────

    [Header("Audio")]
    [Tooltip("Sound played when the player jumps.")]
    public AudioClip jumpSound;

    [Tooltip("AudioSource used to play jump sound. Auto-found if blank.")]
    public AudioSource audioSource;

    // ─── Regen ────────────────────────────────────────────────────────────────

    [Header("Regen")]
    [Tooltip("Stamina restored per second. Applied to StaminaSystem.rechargeRate at startup.")]
    [SerializeField] private float staminaRegenPerSecond = 10f;

    // ─── Model Offset ─────────────────────────────────────────────────────────

    [Header("Model Offset")]
    [Tooltip("Assign the child model (Offensive Idle) here to lock its local position.")]
    public Transform modelTransform;

    [Tooltip("Local position offset of the model relative to the player root.")]
    public Vector3 modelPositionOffset = Vector3.zero;

    // ─── Player Registry ──────────────────────────────────────────────────────
    /// <summary>
    /// All active player GameObjects in the scene. Populated in Start(), cleared in
    /// OnDestroy(). Used by server-side systems (HealingWave, WaveManager) as a
    /// GC-free alternative to FindGameObjectsWithTag("Player") on every tick.
    /// Exposed as IReadOnlyList so external callers can iterate but not mutate.
    /// </summary>
    public static IReadOnlyList<GameObject> All => _all;
    private static readonly List<GameObject> _all = new();

    // ─── Private State ────────────────────────────────────────────────────────

    private CharacterController controller;
    private Animator animator;
    private StaminaSystem stamina;
    private HealthSystem _health;
    private LockOnSystem lockOn;
    private ExperienceManager _xp;
    private SpellCaster          _spellCaster;
    private StatusEffectHandler  _statusEffects;

    /// <summary>True while the player is actively sprinting (moving + sprint input + stamina).</summary>
    public bool IsSprinting { get; private set; }

    /// <summary>
    /// When set by SpellCaster, drives the player toward this world position automatically.
    /// Overrides movement only when there is no manual input. Cleared when the cast fires or cancels.
    /// </summary>
    public Vector3? AutoMoveTarget { get; set; }

    /// <summary>
    /// Stops the current sprint immediately. Suppresses re-entry from held sprint input
    /// until the sprint button is fully released, so a held sprint key doesn't instantly
    /// re-enable sprint on the next frame.
    /// </summary>
    public void CancelSprint()
    {
        _sprintToggle    = false;
        _sprintSuppressed = true;
    }
    private float verticalVelocity;
    private bool _sprintToggle    = false;
    private bool _sprintSuppressed = false; // true while a cast is suppressing sprint
    private bool _isJumping = false;
    private PlayerInputActions inputActions;
    private Vector2 moveInput;

    /// <summary>
    /// Shared input actions instance. Other components on this GameObject
    /// (e.g. PlayerAttack) should borrow this rather than creating their own,
    /// to avoid double-enable / double-disable conflicts.
    /// </summary>
    public PlayerInputActions InputActions => inputActions;

    // ─── Animator Parameters ──────────────────────────────────────────────────

    private static readonly int AnimJump       = Animator.StringToHash("Jump");
    private static readonly int AnimWalk       = Animator.StringToHash("Walk");
    private static readonly int AnimSprint     = Animator.StringToHash("Sprint");
    private static readonly int AnimVelocityX  = Animator.StringToHash("VelocityX");
    private static readonly int AnimVelocityZ  = Animator.StringToHash("VelocityZ");
    private static readonly int AnimIsLockedOn = Animator.StringToHash("IsLockedOn");
    private static readonly int AnimCasting    = Animator.StringToHash("Casting");
    private static readonly int AnimCastThrow  = Animator.StringToHash("CastThrow");
    private bool _animHasCasting;
    private bool _animHasCastThrow;
    private bool _isCastThrowing;
    private Vector3 _castHorizontalMomentum = Vector3.zero; // horizontal carry-through during airborne movement lock
    private bool    _wasMovementLocked       = false;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        inputActions = new PlayerInputActions();
    }

    // ─── NGO Lifecycle ────────────────────────────────────────────────────────

    // Called by NGO when this object is spawned in a networked session.
    // IsOwner is already set correctly at this point.
    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            // ── Non-owner: disable all input-driven components ────────────────
            // SpellCaster and LockOnSystem use raw device APIs (Keyboard.current,
            // Gamepad.current) that are global — they would respond to input on every
            // player instance on this machine, not just the locally owned one.
            // Disabling them here ensures only the owner's instance processes input.
            var spellCaster = GetComponent<SpellCaster>();
            if (spellCaster != null) spellCaster.enabled = false;

            var lockOnSys = GetComponent<LockOnSystem>();
            if (lockOnSys != null) lockOnSys.enabled = false;

            // ── Disable CharacterController on remote players ─────────────────
            // OwnerNetworkTransform syncs position by writing transform.position
            // directly. An enabled CharacterController intercepts and blocks those
            // writes, causing remote players to appear stuck at their spawn point
            // or underground. Disabling it here lets NGO drive the position freely.
            var remoteCc = GetComponent<CharacterController>();
            if (remoteCc != null) remoteCc.enabled = false;

            return;
        }

        // ── Freeze movement until the server confirms our spawn position ─────
        // With OwnerNetworkTransform the client controls position, but the prefab
        // default is Y=0 (underground). Disable the CharacterController immediately
        // so gravity cannot move us during the 1-2 frames before the spawn-position
        // RPC arrives. It is re-enabled inside ApplySpawnPositionClientRpc.
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        // ── Owner: wire camera and enable input ───────────────────────────────
        if (Camera.main != null)
            cameraTransform = Camera.main.transform;
        else
            Debug.LogWarning("[PlayerController] Camera.main is null.");

        CameraModeSwitcher switcher = FindFirstObjectByType<CameraModeSwitcher>();
        if (switcher != null) switcher.SetTarget(transform);
        else Debug.LogWarning("[PlayerController] No CameraModeSwitcher found in scene.");

        // ── Set singletons for this owner's player ────────────────────────────
        // NOTE: _xp is cached in Start() which hasn't run yet at this point in NGO.
        // Use GetComponent directly to guarantee we get the reference.
        GetComponent<ExperienceManager>()?.SetAsLocalInstance();
        GetComponent<PlayerInventory>()?.SetAsLocalInstance();

        EnableInput();
        Debug.Log($"[PlayerController] OnNetworkSpawn — owner client {OwnerClientId}, input enabled.");
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;
        DisableInput();
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        _all.Add(gameObject);
        WaveManager.Instance?.RegisterPlayer(transform);

        controller     = GetComponent<CharacterController>();
        stamina        = GetComponent<StaminaSystem>();
        _health        = GetComponent<HealthSystem>();
        _spellCaster   = GetComponent<SpellCaster>();
        if (_spellCaster != null)
        {
            _spellCaster.OnCastThrowStart += OnCastThrow;
            _spellCaster.OnCastComplete   += () => _isCastThrowing = false;
            _spellCaster.OnCastCancelled  += () => _isCastThrowing = false;
        }
        _statusEffects = GetComponent<StatusEffectHandler>();
        if (stamina != null) stamina.rechargeRate = staminaRegenPerSecond;
        lockOn     = GetComponent<LockOnSystem>();
        _xp        = GetComponent<ExperienceManager>();

        StartCoroutine(HpRegenTick());
        animator   = GetComponentInChildren<Animator>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        if (animator == null)
        {
            Debug.LogWarning("[PlayerController] No Animator found in children!");
        }
        else
        {
            foreach (var p in animator.parameters)
            {
                if (p.nameHash == AnimCasting)   _animHasCasting   = true;
                if (p.nameHash == AnimCastThrow) _animHasCastThrow = true;
            }
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        // Solo / non-networked fallback: NGO never calls OnNetworkSpawn when
        // NetworkManager is not running, so enable input here instead.
        // When networking IS active, OnNetworkSpawn handles it with IsOwner checks.
        bool networkingActive = NetworkManager.Singleton != null
                             && NetworkManager.Singleton.IsListening;
        if (!networkingActive)
        {
            if (Camera.main != null)
                cameraTransform = Camera.main.transform;

            CameraModeSwitcher switcher = FindFirstObjectByType<CameraModeSwitcher>();
            if (switcher != null) switcher.SetTarget(transform);

            // Solo: set singletons immediately (no OnNetworkSpawn will fire)
            _xp?.SetAsLocalInstance();
            GetComponent<PlayerInventory>()?.SetAsLocalInstance();

            EnableInput();
            Debug.Log("[PlayerController] Start — solo mode, input enabled.");
        }
    }

    public override void OnDestroy()
    {
        // Safety cleanup — always unsubscribe to avoid stale callbacks.
        base.OnDestroy();
        _all.Remove(gameObject);
        DisableInput();
        if (_spellCaster != null) _spellCaster.OnCastThrowStart -= OnCastThrow;
    }

    // ─── HP Regen (server-authoritative) ─────────────────────────────────────

    /// <summary>
    /// Runs on every player instance. In multiplayer only the server applies the heal
    /// and then broadcasts it to clients. In singleplayer it always runs.
    /// </summary>
    IEnumerator HpRegenTick()
    {
        var wait = new WaitForSeconds(1f);
        while (true)
        {
            yield return wait;

            if (_health == null) continue;
            if (_health.currentHealth >= _health.maxHealth) continue;

            float regen = _health.TotalRegenPerSecond;
            if (regen <= 0f) continue;

            // In a networked session only the server heals; in singleplayer always heal.
            bool networkActive = NetworkManager.Singleton != null
                              && NetworkManager.Singleton.IsListening;
            if (networkActive && !IsServer) continue;

            int amount = Mathf.RoundToInt(regen);
            _health.Heal(amount);
            DebugLogger.Log(DebugLogger.Category.Regen,
                $"{gameObject.name} regen tick +{amount} — HP: {_health.currentHealth}/{_health.maxHealth}");

            // Mirror the heal on all clients.
            if (IsSpawned)
                SyncRegenHealClientRpc(amount);
        }
    }

    /// <summary>
    /// Mirrors a server-applied regen heal on every client.
    /// The server (host) skips it — it already healed above.
    /// </summary>
    [ClientRpc]
    void SyncRegenHealClientRpc(int amount)
    {
        if (IsServer) return; // host already healed server-side
        if (_health != null)
            _health.Heal(amount);
    }

    /// <summary>
    /// Called by the owning client after taking damage locally (via DealDamageClientRpc /
    /// DamageZone) to keep the server's HealthSystem.currentHealth in sync.
    /// This lets server-side heal checks (e.g. HealingWave) see the correct value.
    /// Does NOT call TakeDamage — only sets the raw value — so death logic is not
    /// re-triggered server-side for a remote client.
    /// </summary>
    [ServerRpc]
    public void SyncServerHealthServerRpc(int clientHealth)
    {
        if (_health == null) return;
        _health.currentHealth = Mathf.Clamp(clientHealth, 0, _health.maxHealth);
        DebugLogger.Log(DebugLogger.Category.NetworkSync,
            $"{gameObject.name} server health synced → {_health.currentHealth}/{_health.maxHealth}");
    }

    /// <summary>
    /// Called by the owning client whenever maxHealth changes (STR stat spend, equipment).
    /// Keeps the server's HealthSystem.maxHealth in sync so regen and healing checks
    /// (which run server-side) use the correct cap instead of the initial prefab value.
    /// </summary>
    [ServerRpc]
    public void SyncMaxHealthServerRpc(int newMaxHealth)
    {
        if (_health == null) return;
        _health.maxHealth = newMaxHealth;
        _health.currentHealth = Mathf.Clamp(_health.currentHealth, 0, newMaxHealth);
        DebugLogger.Log(DebugLogger.Category.NetworkSync,
            $"{gameObject.name} server maxHealth synced → {newMaxHealth}");
    }

    /// <summary>
    /// Mirrors a server-applied heal on the owning client and shows the popup.
    /// Send with ClientRpcParams targeting OwnerClientId so only that client runs it.
    /// The server (host) skips it — it already healed server-side.
    /// </summary>
    [ClientRpc]
    public void ApplyHealClientRpc(int amount, ClientRpcParams rpcParams = default)
    {
        if (IsServer) return; // host already healed server-side
        if (_health != null)
            _health.Heal(amount);
        if (DamagePopupManager.Instance != null)
            DamagePopupManager.Instance.ShowHeal(transform.position, amount);
    }

    /// <summary>
    /// Shows a heal popup on the owning client after a server-side heal (e.g. HealingWave tick).
    /// Broadcast to all clients but only the owner acts — avoids per-client RPC overhead.
    /// </summary>
    [ClientRpc]
    public void ShowHealPopupClientRpc(int amount)
    {
        if (!IsOwner) return;
        if (DamagePopupManager.Instance != null)
            DamagePopupManager.Instance.ShowHeal(transform.position, amount);
    }

    // ─── Spawn Position (Multiplayer) ─────────────────────────────────────────

    /// <summary>
    /// Sent by PlayerSpawner (server) to the owning client right after spawn.
    /// The CLIENT calls Teleport() on its own OwnerNetworkTransform — only the
    /// owner has authority to do so on a client-authoritative transform.
    /// Also re-enables the CharacterController that was frozen in OnNetworkSpawn.
    /// </summary>
    [ClientRpc]
    public void ApplySpawnPositionClientRpc(Vector3 position, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        // Set position directly — for an owner-authoritative transform the owner
        // IS the authority, so transform.position is the source of truth.
        // NetworkTransform.Teleport() requires CanCommitToTransform which can be
        // unset inside a ClientRpc in some NGO versions even when IsOwner is true.
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.position = position;
        if (cc != null) cc.enabled = true;

        verticalVelocity = 0f;
        Debug.Log($"[PlayerController] Spawn position applied: {position}");
    }

    // ─── Respawn (Multiplayer) ────────────────────────────────────────────────

    /// <summary>
    /// Called on the owning client when the player clicks "Respawn" on the death screen.
    /// Forwards the request to the server which heals the player and teleports them
    /// back to a spawn point, then notifies this client to fade back in.
    /// </summary>
    [ServerRpc]
    public void RespawnServerRpc()
    {
        // Heal server-side copy of HealthSystem
        HealthSystem health = GetComponent<HealthSystem>();
        if (health != null)
            health.Heal(health.maxHealth);

        // Find a valid respawn position
        Vector3 spawnPos = GetRespawnPosition();

        // Send to owning client only
        ClientRpcParams ownerOnly = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { OwnerClientId } }
        };
        RespawnClientRpc(spawnPos, ownerOnly);

        Debug.Log($"[PlayerController] Server respawned client {OwnerClientId} at {spawnPos}");
    }

    [ClientRpc]
    void RespawnClientRpc(Vector3 spawnPos, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        // Heal client-side HealthSystem (health is not a NetworkVariable, so both
        // sides need to be updated independently)
        HealthSystem health = GetComponent<HealthSystem>();
        if (health != null)
            health.Heal(health.maxHealth);

        // Teleport: disable CharacterController first so we can move the transform
        if (controller != null) controller.enabled = false;
        transform.position = spawnPos;
        if (controller != null) controller.enabled = true;

        // Reset vertical velocity so the player doesn't fall through if spawned mid-air
        verticalVelocity = 0f;

        // Fade back in from the black death screen
        if (SceneTransitionManager.Instance != null)
            SceneTransitionManager.Instance.FadeIn();

        // Isometric: cursor must always be visible and free for aiming.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        Debug.Log($"[PlayerController] Respawned at {spawnPos}");
    }

    Vector3 GetRespawnPosition()
    {
        PlayerSpawner spawner = FindFirstObjectByType<PlayerSpawner>();
        if (spawner != null)
            return spawner.GetRespawnPosition();

        // Absolute fallback — should never happen if PlayerSpawner is in the scene
        return Vector3.up * 5f;
    }

    // ─── Input Helpers ────────────────────────────────────────────────────────

    void EnableInput()
    {
        inputActions.Player.Enable();
        inputActions.Player.Jump.performed        += OnJump;
        inputActions.Player.SprintToggle.performed += OnSprintToggle;
    }

    void DisableInput()
    {
        inputActions.Player.Jump.performed        -= OnJump;
        inputActions.Player.SprintToggle.performed -= OnSprintToggle;
        inputActions.Player.Disable();
    }

    void OnJump(InputAction.CallbackContext ctx)
    {
        if (_spellCaster != null && _spellCaster.IsMovementLocked) return;
        if (controller != null && controller.isGrounded)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _isJumping = true;

            if (audioSource != null && jumpSound != null)
                audioSource.PlayOneShot(jumpSound);
        }
    }

    void OnCastThrow()
    {
        _isCastThrowing = true;
        if (animator != null && _animHasCastThrow)
            animator.SetTrigger(AnimCastThrow);
    }

    void OnSprintToggle(InputAction.CallbackContext ctx)
    {
        if (stamina == null || stamina.CanSprint())
            _sprintToggle = !_sprintToggle;
    }

    void Update()
    {
        // Spectator clients have no ownership — skip all input and movement.
        if (IsSpawned && !IsOwner) return;

        // ── UI Panel Open ─────────────────────────────────────────────────────
        // In multiplayer Time.timeScale stays at 1 (pausing one client would desync
        // the server), so we must explicitly block input while any panel is open.
        if (PauseManager.IsPaused) return;

        // ── Stun ──────────────────────────────────────────────────────────────
        // While stunned, freeze all input and movement (cast is cancelled by StatusEffectHandler).
        if (_statusEffects != null && _statusEffects.IsStunned) return;

        // ── Camera fallback ───────────────────────────────────────────────────
        // OnNetworkSpawn can fire before Camera.main is ready (especially on the
        // client in a multiplayer session). Retry every frame until we have it.
        if (cameraTransform == null)
        {
            if (Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
                Debug.Log("[PlayerController] Camera found on retry.");
            }
            else
            {
                return; // Still no camera — skip movement this frame.
            }
        }

        // ── Read Input ────────────────────────────────────────────────────────
        moveInput = inputActions.Player.Move.ReadValue<Vector2>();
        Vector2 rawMoveInput = moveInput; // preserve before possible lock suppression

        // ── Movement Grace Lock ───────────────────────────────────────────────
        // During a spell's movementInterruptGrace window the player is rooted in
        // place — suppress movement input so the character stops and holds their
        // facing direction while the cast animation begins.
        if (_spellCaster != null && _spellCaster.IsMovementLocked)
            moveInput = Vector2.zero;

        bool isMoving = moveInput.magnitude > 0.1f;

        // ── Spell Cast Interrupt Check ────────────────────────────────────────
        // Moving while a cast is active may cancel it, depending on the spell's
        // movementInterruptGrace. Casting never freezes movement.
        if (isMoving && _spellCaster != null && _spellCaster.IsActive)
            _spellCaster.TryCancelByMovement();

        // ── Sprint Logic ──────────────────────────────────────────────────────
        bool holdingSprint = inputActions.Player.Sprint.IsPressed();

        // Clear suppression once the player fully releases the sprint button.
        if (!holdingSprint) _sprintSuppressed = false;

        bool wantsToSprint = (holdingSprint || _sprintToggle) && !_sprintSuppressed;

        if (!isMoving)
            _sprintToggle = false;

        if (stamina != null && !stamina.CanSprint())
        {
            _sprintToggle = false;
            wantsToSprint = false;
        }

        bool isSprinting = wantsToSprint && isMoving;
        IsSprinting = isSprinting;

        // ── Sprint cancels active cast ─────────────────────────────────────────
        if (isSprinting && _spellCaster != null && _spellCaster.IsActive)
            _spellCaster.CancelCast();

        // ── AGI Speed Bonus ───────────────────────────────────────────────────
        // ExperienceManager applies the per-point AGI multiplier to the base speeds
        // set in this Inspector. Tweak agiMoveSpeedBonus / agiSprintSpeedBonus there.
        float equipMoveBonus = PlayerInventory.Instance != null
            ? PlayerInventory.Instance.TotalBonusMovementSpeed
            : 0f;

        float effectiveMoveSpeed = (_xp != null
            ? _xp.ComputedMoveSpeed(moveSpeed)
            : moveSpeed) * (1f + equipMoveBonus);

        float effectiveSprintSpeed = (_xp != null
            ? _xp.ComputedSprintSpeed(sprintSpeed)
            : sprintSpeed) * (1f + equipMoveBonus);

        float currentSpeed = isSprinting ? effectiveSprintSpeed : effectiveMoveSpeed;

        // Notify stamina system
        if (stamina != null)
            stamina.SetSprinting(isSprinting);

        // ── Grounded ──────────────────────────────────────────────────────────
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        // ── Gravity ───────────────────────────────────────────────────────────
        verticalVelocity += gravity * Time.deltaTime;
        verticalVelocity = Mathf.Max(verticalVelocity, -20f);

        // ── Direction ─────────────────────────────────────────────────────────
        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight = cameraTransform.right;
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 move = (camForward * moveInput.y +
                        camRight * moveInput.x).normalized * currentSpeed;

        // ── Auto Move (SpellCaster walk-to-cast) ──────────────────────────────
        // Only activates when no manual input so the player can always override by moving.
        if (AutoMoveTarget.HasValue && moveInput.magnitude < 0.1f)
        {
            Vector3 toAuto = AutoMoveTarget.Value - transform.position;
            toAuto.y = 0f;
            if (toAuto.sqrMagnitude > 0.01f)
            {
                move     = toAuto.normalized * currentSpeed;
                isMoving = true; // drive walk animation
            }
        }

        // ── Cast Momentum ──────────────────────────────────────────────────────
        // When movement lock begins while airborne, carry the horizontal velocity
        // forward so the jump arc completes naturally instead of stopping dead.
        // Momentum is cleared once the player lands.
        bool moveLocked = _spellCaster != null && _spellCaster.IsMovementLocked;
        if (moveLocked)
        {
            if (!_wasMovementLocked && !controller.isGrounded && rawMoveInput.magnitude > 0.1f)
            {
                Vector3 rawMove = (camForward * rawMoveInput.y + camRight * rawMoveInput.x).normalized * currentSpeed;
                _castHorizontalMomentum = new Vector3(rawMove.x, 0f, rawMove.z);
            }
            if (!controller.isGrounded && _castHorizontalMomentum.sqrMagnitude > 0.001f)
            {
                move.x = _castHorizontalMomentum.x;
                move.z = _castHorizontalMomentum.z;
            }
            else if (controller.isGrounded)
            {
                _castHorizontalMomentum = Vector3.zero;
            }
        }
        else
        {
            _castHorizontalMomentum = Vector3.zero;
        }
        _wasMovementLocked = moveLocked;

        // ── Single Move Call ──────────────────────────────────────────────────
        move.y = verticalVelocity;
        controller.Move(move * Time.deltaTime);

        // ── Animate ───────────────────────────────────────────────────────────
        // Reset _isJumping AFTER Move() so isGrounded reflects the new position
        if (controller.isGrounded)
            _isJumping = false;

        // Strafing walk: locked on, moving, not sprinting, not jumping
        bool lockedOnWalk = lockOn != null && lockOn.IsLockedOn && isMoving && !isSprinting && !_isJumping;

        if (animator != null)
        {
            if (_animHasCasting) animator.SetBool(AnimCasting, _spellCaster != null && _spellCaster.IsActive && !_isCastThrowing);
            animator.SetBool(AnimJump,       _isJumping);
            animator.SetBool(AnimIsLockedOn, lockedOnWalk);
            animator.SetBool(AnimWalk,       isMoving && !isSprinting && !_isJumping && !lockedOnWalk);
            animator.SetBool(AnimSprint,     isSprinting && !_isJumping);

            if (lockedOnWalk)
            {
                // Drive strafing blend tree in player-local space
                Vector3 localMove = transform.InverseTransformDirection(
                    new Vector3(move.x, 0f, move.z).normalized);
                animator.SetFloat(AnimVelocityX,
                    Mathf.Lerp(animator.GetFloat(AnimVelocityX), localMove.x, blendSharpness * Time.deltaTime));
                animator.SetFloat(AnimVelocityZ,
                    Mathf.Lerp(animator.GetFloat(AnimVelocityZ), localMove.z, blendSharpness * Time.deltaTime));
            }
        }

        // ── Rotation ──────────────────────────────────────────────────────────
        // Priority 1 — soft-lock: face the locked enemy while not sprinting.
        if (lockOn != null && lockOn.IsLockedOn && !isSprinting)
        {
            Vector3 toTarget = lockOn.LockTarget.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(toTarget);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRotation, lockOn.lockRotationSpeed * Time.deltaTime);
            }
        }
        // Priority 2 — unified aim: IsoAim provides the world point for whichever
        // device is active (mouse cursor or gamepad right stick via IsoControllerAim).
        else if (IsoAim.HasHit)
        {
            Vector3 aimDir = IsoAim.AimDirectionFrom(transform.position);
            if (aimDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(aimDir);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }
        }
        // Priority 3 — fallback: face movement direction when no aim data exists.
        else if (isMoving)
        {
            Vector3 flatMove = new Vector3(move.x, 0f, move.z);
            if (flatMove.magnitude > 0.1f)
            {
                Quaternion targetRot = Quaternion.LookRotation(flatMove);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }
        }

        // ── Lock Model Position ───────────────────────────────────────────────
        // Prevents the child model from drifting due to animation root motion
        if (modelTransform != null)
            modelTransform.localPosition = modelPositionOffset;
    }
}
