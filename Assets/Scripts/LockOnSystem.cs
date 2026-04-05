using UnityEngine;
using UnityEngine.InputSystem;

public class LockOnSystem : MonoBehaviour
{
    // ─── Lock-On Settings ─────────────────────────────────────────────────────

    [Header("Lock-On Settings")]
    [Tooltip("Maximum distance to search for lockable enemies.")]
    public float lockRange = 20f;

    [Tooltip("How fast the player rotates toward the locked target.")]
    public float lockRotationSpeed = 720f;

    [Tooltip("Tag used to identify enemy GameObjects.")]
    public string enemyTag = "Enemy";

    // ─── Indicator ────────────────────────────────────────────────────────────

    [Header("Indicator")]
    [Tooltip("Prefab shown above the locked enemy (assign a sprite/quad with your target icon).")]
    [SerializeField] private GameObject indicatorPrefab;

    [Tooltip("Height offset above the enemy pivot where the indicator floats.")]
    [SerializeField] private float indicatorHeight = 2.2f;

    [Tooltip("How far up and down the indicator bobs (Unity units).")]
    [SerializeField] private float bobAmplitude = 0.15f;

    [Tooltip("How many full bobs per second.")]
    [SerializeField] private float bobSpeed = 2f;

    // ─── Switch Target ────────────────────────────────────────────────────────

    [Header("Switch Target")]
    [Tooltip("How far the right stick must be pushed to trigger a switch (0–1).")]
    [SerializeField] private float switchDeadzone = 0.5f;

    [Tooltip("Minimum seconds between consecutive switches.")]
    [SerializeField] private float switchCooldown = 0.4f;

    // ─── Input ────────────────────────────────────────────────────────────────

    [Header("Input")]
    [Tooltip("Button that toggles lock-on. Default: Tab (keyboard).")]
    [SerializeField] private InputAction lockOnAction = new InputAction(
        type: InputActionType.Button,
        binding: "<Keyboard>/tab"
    );

    [Tooltip("Axis used to switch between targets. Default: right gamepad stick.")]
    [SerializeField] private InputAction switchAction = new InputAction(
        name: "SwitchTarget",
        type: InputActionType.Value,
        binding: "<Gamepad>/rightStick",
        expectedControlType: "Vector2"
    );

    // ─── Public State ─────────────────────────────────────────────────────────

    /// <summary>Currently locked enemy transform, or null.</summary>
    public Transform LockTarget { get; private set; }

    /// <summary>True when a target is actively locked.</summary>
    public bool IsLockedOn => LockTarget != null;

    // ─── Private ──────────────────────────────────────────────────────────────

    private Camera mainCam;
    private GameObject activeIndicator;
    private HealthSystem targetHealth;   // cached on SetTarget; used for early-death detection
    private float switchTimer;
    private bool stickWasNeutral = true;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        mainCam = Camera.main;
    }

    void OnEnable()
    {
        lockOnAction.Enable();
        switchAction.Enable();
        lockOnAction.performed += OnLockOnPressed;
    }

    void OnDisable()
    {
        lockOnAction.performed -= OnLockOnPressed;
        lockOnAction.Disable();
        switchAction.Disable();
    }

    void Update()
    {
        ValidateTarget();
        UpdateIndicator();
        HandleSwitchInput();

        if (switchTimer > 0f)
            switchTimer -= Time.deltaTime;
    }

    // ─── Lock-On Logic ────────────────────────────────────────────────────────

    private void OnLockOnPressed(InputAction.CallbackContext ctx)
    {
        if (IsLockedOn)
            ClearTarget();
        else
            TryAcquireTarget();
    }

    private void TryAcquireTarget()
    {
        Transform best = FindBestTarget(null, 0f);
        if (best != null)
        {
            SetTarget(best);
        }
        else
        {
            Debug.Log("[LockOnSystem] No valid target found.");
        }
    }

    /// <summary>
    /// Finds the best lockable enemy.
    /// <paramref name="exclude"/> is always skipped (used to ignore the current/dying target).
    /// <paramref name="stickDir"/> != 0 activates directional filtering relative to <paramref name="exclude"/>
    /// (positive = right/clockwise, negative = left/counter-clockwise).
    /// </summary>
    private Transform FindBestTarget(Transform exclude, float stickDir)
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, lockRange);

        Transform best = null;
        float bestScore = float.MaxValue;

        Vector3 toExcludeFlat = exclude != null
            ? Vector3.ProjectOnPlane(exclude.position - transform.position, Vector3.up).normalized
            : Vector3.zero;

        bool useDirectional = exclude != null && Mathf.Abs(stickDir) > 0.001f;

        foreach (Collider col in hits)
        {
            if (!col.CompareTag(enemyTag)) continue;

            // Always skip the excluded (current/dying) target
            if (exclude != null && col.transform == exclude) continue;

            // Skip enemies that are dead or dying
            HealthSystem hs = col.GetComponent<HealthSystem>();
            if (hs != null && hs.currentHealth <= 0) continue;

            Vector3 screenPos = mainCam.WorldToViewportPoint(col.transform.position);
            if (screenPos.z <= 0f) continue;
            if (screenPos.x < 0f || screenPos.x > 1f) continue;
            if (screenPos.y < 0f || screenPos.y > 1f) continue;

            if (useDirectional)
            {
                // Filter to the correct angular half based on stick direction
                Vector3 toCandFlat = Vector3.ProjectOnPlane(
                    col.transform.position - transform.position, Vector3.up).normalized;

                float signedAngle = Vector3.SignedAngle(toExcludeFlat, toCandFlat, Vector3.up);

                // stickDir > 0 → right → want positive (clockwise) angles
                // stickDir < 0 → left  → want negative (counter-clockwise) angles
                if (stickDir > 0f && signedAngle <= 0f) continue;
                if (stickDir < 0f && signedAngle >= 0f) continue;

                float absAngle = Mathf.Abs(signedAngle);
                if (absAngle < bestScore)
                {
                    bestScore = absAngle;
                    best = col.transform;
                }
            }
            else
            {
                // Score by closeness to screen center
                float dx = screenPos.x - 0.5f;
                float dy = screenPos.y - 0.5f;
                float score = dx * dx + dy * dy;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = col.transform;
                }
            }
        }

        return best;
    }

    private void SetTarget(Transform t)
    {
        LockTarget = t;
        targetHealth = t.GetComponent<HealthSystem>();
        Debug.Log($"[LockOnSystem] Locked onto {t.name}");

        // Spawn indicator
        DestroyIndicator();
        if (indicatorPrefab != null)
        {
            activeIndicator = Instantiate(indicatorPrefab);
            UpdateIndicator(); // position immediately to avoid one-frame pop
        }
    }

    private void ClearTarget()
    {
        Debug.Log("[LockOnSystem] Lock-on released.");
        LockTarget = null;
        targetHealth = null;
        DestroyIndicator();
    }

    // ─── Validation ───────────────────────────────────────────────────────────

    private void ValidateTarget()
    {
        if (LockTarget == null) return;

        // Catch the dying window: HP hit 0 but Destroy() hasn't fired yet
        bool isDying = targetHealth != null && targetHealth.currentHealth <= 0;

        if (isDying || !LockTarget.gameObject.activeInHierarchy)
        {
            // Shift to the nearest other enemy immediately, or release.
            // Pass LockTarget as the exclusion so the dying enemy is never re-selected.
            Transform fallback = FindBestTarget(LockTarget, 0f);
            if (fallback != null)
                SetTarget(fallback);
            else
                ClearTarget();
            return;
        }

        if (Vector3.Distance(transform.position, LockTarget.position) > lockRange * 1.2f)
        {
            Debug.Log("[LockOnSystem] Target out of range, releasing lock.");
            ClearTarget();
        }
    }

    // ─── Switch Target ────────────────────────────────────────────────────────

    private void HandleSwitchInput()
    {
        if (!IsLockedOn) return;
        if (switchTimer > 0f) return;

        Vector2 stick = switchAction.ReadValue<Vector2>();
        float horizontal = stick.x;

        if (Mathf.Abs(horizontal) < switchDeadzone)
        {
            stickWasNeutral = true;
            return;
        }

        // Only fire once per stick push (require returning to neutral first)
        if (!stickWasNeutral) return;
        stickWasNeutral = false;
        switchTimer = switchCooldown;

        float dir = horizontal > 0f ? 1f : -1f;
        Transform next = FindBestTarget(LockTarget, dir);

        if (next != null)
        {
            SetTarget(next);
        }
        else
        {
            // No target in that direction — wrap to the other side
            Transform wrap = FindBestTarget(LockTarget, -dir);
            if (wrap != null) SetTarget(wrap);
        }
    }

    // ─── Indicator ────────────────────────────────────────────────────────────

    private void UpdateIndicator()
    {
        if (activeIndicator == null) return;
        if (LockTarget == null) { DestroyIndicator(); return; }

        // Float above the enemy with a gentle sine-wave bob
        float bob = Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f) * bobAmplitude;
        Vector3 pos = LockTarget.position + Vector3.up * (indicatorHeight + bob);
        activeIndicator.transform.position = pos;

        // Point Z away from the camera so the sprite's front face is visible
        Vector3 dirAwayFromCam = pos - mainCam.transform.position;
        if (dirAwayFromCam.sqrMagnitude > 0.001f)
            activeIndicator.transform.rotation = Quaternion.LookRotation(dirAwayFromCam);
    }

    private void DestroyIndicator()
    {
        if (activeIndicator != null)
        {
            Destroy(activeIndicator);
            activeIndicator = null;
        }
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, lockRange);

        if (LockTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position + Vector3.up, LockTarget.position + Vector3.up);
        }
    }
}
