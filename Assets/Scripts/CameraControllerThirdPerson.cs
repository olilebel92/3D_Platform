using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Third-person orbit camera. The player rotates the view with the mouse (or right
/// stick on gamepad). Swap this component with CameraControllerIsometric to switch
/// camera modes.
/// </summary>
public class CameraControllerThirdPerson : MonoBehaviour
{
    // ─── Target ───────────────────────────────────────────────────────────────

    [Header("Target")]
    [Tooltip("The transform the camera orbits around (assign the Player root).")]
    public Transform target;

    [Tooltip("Vertical offset applied to the orbit pivot so the camera looks at the chest, not the feet.")]
    public float pivotHeight = 1.5f;

    // ─── Orbit Settings ───────────────────────────────────────────────────────

    [Header("Orbit Settings")]
    [Tooltip("Horizontal gamepad stick sensitivity (degrees per second).")]
    public float sensitivityX = 200f;

    [Tooltip("Vertical gamepad stick sensitivity (degrees per second).")]
    public float sensitivityY = 150f;

    [Tooltip("Mouse sensitivity (0.1–1.0). Internally scaled by 0.01 so 0.25 = 0.0025 actual.")]
    [Range(0.1f, 1f)]
    public float mouseSensitivity = 0.25f;

    [Tooltip("Minimum vertical angle (looking down toward the feet).")]
    public float minPitch = -20f;

    [Tooltip("Maximum vertical angle (looking down from above).")]
    public float maxPitch = 70f;

    [Tooltip("Distance from the pivot point to the camera.")]
    public float distance = 5f;

    // ─── Follow Smoothing ─────────────────────────────────────────────────────

    [Header("Follow Smoothing")]
    [Tooltip("How quickly the camera catches up to the player. Higher = snappier.")]
    public float followSpeed = 10f;

    // ─── Collision ────────────────────────────────────────────────────────────

    [Header("Collision")]
    [Tooltip("Layer mask used for camera collision raycasts.")]
    public LayerMask collisionMask = ~0;

    [Tooltip("Minimum distance from pivot when geometry is blocking the view.")]
    public float minDistance = 0.5f;

    // ─── Cursor ───────────────────────────────────────────────────────────────

    [Header("Cursor")]
    [Tooltip("Lock and hide the cursor while in play mode.")]
    public bool lockCursor = true;

    /// <summary>Set to true by any UI panel that needs to freeze the camera.</summary>
    public static bool IsLocked = false;

    // ─── Private State ────────────────────────────────────────────────────────

    private float _yaw;
    private float _pitch = 15f;
    private Vector3 _currentPivot;
    private PlayerInputActions _input;
    private InputAction _lookAction;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        _input = new PlayerInputActions();
    }

    void OnEnable()
    {
        _input.Enable();
        _lookAction = _input.Player.Look;

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (target != null)
        {
            // Seed pivot at the actual player position so the first frame has no lerp jump.
            _currentPivot = target.position + Vector3.up * pivotHeight;

            // Seed yaw from current camera facing so there's no rotation snap on enable.
            _yaw = transform.eulerAngles.y;
        }
    }

    void OnDisable()
    {
        _input.Disable();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void LateUpdate()
    {
        // Auto-find the local player if no target is assigned.
        if (target == null)
        {
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (!pc.IsSpawned || pc.IsOwner)
                {
                    target = pc.transform;
                    _currentPivot = target.position + Vector3.up * pivotHeight;
                    _yaw = transform.eulerAngles.y;
                    Debug.Log("[CameraControllerThirdPerson] Auto-found player target: " + target.name);
                    break;
                }
            }
            return;
        }

        if (Time.timeScale == 0f) return;
        if (IsLocked) return;

        // ── Read look input ──────────────────────────────────────────────────
        Vector2 look = _lookAction.ReadValue<Vector2>();

        // Mouse delta is already per-frame (pixels) — no Time.deltaTime needed.
        // Gamepad stick is -1..1 continuous — needs Time.deltaTime.
        bool usingMouse = _lookAction.activeControl?.device is Mouse;
        float scale = usingMouse ? mouseSensitivity * 0.01f : Time.deltaTime;

        _yaw   += look.x * sensitivityX * scale;
        _pitch -= look.y * sensitivityY * scale;
        _pitch  = Mathf.Clamp(_pitch, minPitch, maxPitch);

        // ── Smooth follow the pivot ──────────────────────────────────────────
        Vector3 desiredPivot = target.position + Vector3.up * pivotHeight;
        _currentPivot = Vector3.Lerp(_currentPivot, desiredPivot, followSpeed * Time.deltaTime);

        // ── Compute desired camera position ──────────────────────────────────
        Quaternion rotation  = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 desiredPos   = _currentPivot - rotation * Vector3.forward * distance;

        // ── Camera collision – pull in if geometry blocks the view ───────────
        float actualDistance = distance;
        Vector3 dir = (desiredPos - _currentPivot).normalized;
        RaycastHit[] hits = Physics.SphereCastAll(_currentPivot, 0.2f, dir, distance, collisionMask,
                                                  QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            // Skip the player and its children.
            if (hit.transform.IsChildOf(target)) continue;

            // Skip dynamic objects (enemies, projectiles, pickups — anything with a Rigidbody).
            // Only static geometry like walls and terrain should push the camera.
            if (hit.rigidbody != null) continue;

            float d = Mathf.Max(hit.distance - 0.1f, minDistance);
            if (d < actualDistance) actualDistance = d;
        }

        transform.position = _currentPivot - rotation * Vector3.forward * actualDistance;
        transform.LookAt(_currentPivot);
    }
}
