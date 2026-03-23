using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    // ─── Movement ─────────────────────────────────────────────────────────────

    [Header("Movement")]
    [Tooltip("Base walk speed before AGI bonuses are applied.")]
    public float moveSpeed = 5f;

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
    private float verticalVelocity;
    private bool _sprintToggle = false;
    private PlayerInputActions inputActions;
    private Vector2 moveInput;

    /// <summary>
    /// Shared input actions instance. Other components on this GameObject
    /// (e.g. PlayerAttack) should borrow this rather than creating their own,
    /// to avoid double-enable / double-disable conflicts.
    /// </summary>
    public PlayerInputActions InputActions => inputActions;

    // ─── Animator Parameters ──────────────────────────────────────────────────

    private static readonly int AnimJump = Animator.StringToHash("Jump");
    private static readonly int AnimWalk = Animator.StringToHash("Walk");
    private static readonly int AnimSprint = Animator.StringToHash("Sprint");
    private static readonly int AnimIdle = Animator.StringToHash("Idle");

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        inputActions = new PlayerInputActions();
    }

    void OnEnable()
    {
        inputActions.Player.Enable();
        inputActions.Player.Jump.performed += OnJump;
        inputActions.Player.SprintToggle.performed += OnSprintToggle;

        // Temp debug — fires on ANY interaction with SprintToggle
        inputActions.Player.SprintToggle.started += ctx => Debug.Log("[SprintToggle] started");
        inputActions.Player.SprintToggle.performed += ctx => Debug.Log("[SprintToggle] performed");
        inputActions.Player.SprintToggle.canceled += ctx => Debug.Log("[SprintToggle] canceled");
    }

    void OnDisable()
    {
        inputActions.Player.Jump.performed -= OnJump;
        inputActions.Player.SprintToggle.performed -= OnSprintToggle;
        inputActions.Player.Disable();
    }

    void Start()
    {
        controller = GetComponent<CharacterController>();
        stamina = GetComponent<StaminaSystem>();
        animator = GetComponentInChildren<Animator>();

        if (animator == null)
            Debug.LogWarning("[PlayerController] No Animator found in children!");

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnJump(InputAction.CallbackContext ctx)
    {
        if (controller != null && controller.isGrounded)
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
    }

    void OnSprintToggle(InputAction.CallbackContext ctx)
    {
        if (stamina == null || stamina.CanSprint())
            _sprintToggle = !_sprintToggle;
    }

    void Update()
    {
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
        float effectiveMoveSpeed = ExperienceManager.Instance != null
            ? ExperienceManager.Instance.ComputedMoveSpeed(moveSpeed)
            : moveSpeed;

        float effectiveSprintSpeed = ExperienceManager.Instance != null
            ? ExperienceManager.Instance.ComputedSprintSpeed(sprintSpeed)
            : sprintSpeed;

        float currentSpeed = isSprinting ? effectiveSprintSpeed : effectiveMoveSpeed;

        // Notify stamina system
        if (stamina != null)
            stamina.SetSprinting(isSprinting);

        // ── Animate ───────────────────────────────────────────────────────────
        bool isJumping = !controller.isGrounded;

        if (animator != null)
        {
            animator.SetBool(AnimJump, isJumping);
            animator.SetBool(AnimIdle, !isMoving && !isJumping);
            animator.SetBool(AnimWalk, isMoving && !isSprinting && !isJumping);
            animator.SetBool(AnimSprint, isSprinting && !isJumping);
        }

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

        // ── Rotation ──────────────────────────────────────────────────────────
        Vector3 flatMove = new Vector3(move.x, 0f, move.z);
        if (flatMove.magnitude > 0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(flatMove);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        // ── Lock Model Position ───────────────────────────────────────────────
        // Prevents the child model from drifting due to animation root motion
        if (modelTransform != null)
            modelTransform.localPosition = modelPositionOffset;
    }
}