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

    // ─── Model Offset ─────────────────────────────────────────────────────────

    [Header("Model Offset")]
    [Tooltip("Assign the child model (Offensive Idle) here to lock its local position.")]
    public Transform modelTransform;

    [Tooltip("Local position offset of the model relative to the player root.")]
    public Vector3 modelPositionOffset = Vector3.zero;

    // ─── Private State ────────────────────────────────────────────────────────

    private CharacterController controller;
    private Animator animator;
    private StaminaSystem stamina;
    private LockOnSystem lockOn;
    private ExperienceManager _xp;
    private float verticalVelocity;
    private bool _sprintToggle = false;
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
        controller = GetComponent<CharacterController>();
        stamina    = GetComponent<StaminaSystem>();
        lockOn     = GetComponent<LockOnSystem>();
        _xp        = GetComponent<ExperienceManager>();
        animator   = GetComponentInChildren<Animator>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        if (animator == null)
            Debug.LogWarning("[PlayerController] No Animator found in children!");

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
        DisableInput();
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

        // Re-lock cursor for gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

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
        if (controller != null && controller.isGrounded)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _isJumping = true;

            if (audioSource != null && jumpSound != null)
                audioSource.PlayOneShot(jumpSound);
        }
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
        bool isMoving = moveInput.magnitude > 0.1f;

        // ── Sprint Logic ──────────────────────────────────────────────────────
        bool holdingSprint = inputActions.Player.Sprint.IsPressed();
        bool wantsToSprint = holdingSprint || _sprintToggle;

        if (!isMoving)
            _sprintToggle = false;

        if (stamina != null && !stamina.CanSprint())
        {
            _sprintToggle = false;
            wantsToSprint = false;
        }

        bool isSprinting = wantsToSprint && isMoving;

        // ── AGI Speed Bonus ───────────────────────────────────────────────────
        // ExperienceManager applies the per-point AGI multiplier to the base speeds
        // set in this Inspector. Tweak agiMoveSpeedBonus / agiSprintSpeedBonus there.
        float effectiveMoveSpeed = _xp != null
            ? _xp.ComputedMoveSpeed(moveSpeed)
            : moveSpeed;

        float effectiveSprintSpeed = _xp != null
            ? _xp.ComputedSprintSpeed(sprintSpeed)
            : sprintSpeed;

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
        // Face the locked target only when idle or walking (not while sprinting)
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
        else
        {
            Vector3 flatMove = new Vector3(move.x, 0f, move.z);
            if (flatMove.magnitude > 0.1f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(flatMove);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
        }

        // ── Lock Model Position ───────────────────────────────────────────────
        // Prevents the child model from drifting due to animation root motion
        if (modelTransform != null)
            modelTransform.localPosition = modelPositionOffset;
    }
}
