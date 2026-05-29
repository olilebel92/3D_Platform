using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

/// <summary>
/// Gamepad aim provider for the isometric camera.
/// Attach to the Main Camera alongside <see cref="IsoCursorAim"/>.
///
/// Converts the right stick into a world-space aim point projected forward from
/// the local player, then pushes it into <see cref="IsoAim"/>.
///
/// Device switching:
///   - Any left-stick or right-stick movement claims Gamepad mode.
///   - <see cref="IsoCursorAim"/> reclaims Mouse mode when the mouse moves or clicks.
///
/// ─── Input note ─────────────────────────────────────────────────────────────
/// This script intentionally reads <c>Gamepad.current</c> directly and is an
/// allowed exception to the "all input via Actions" rule (same class as
/// <c>CursorManager</c>). Its reads are device-active detection (stick magnitude to
/// claim Gamepad aim mode) plus the gamepad-specific right-stick aim vector — a
/// unified Look action merges mouse + stick and would defeat that discrimination.
/// Do NOT "migrate" these to Actions.
/// </summary>
public class IsoControllerAim : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Aim")]
    [Tooltip("How far ahead of the player the aim point is projected along the stick direction.")]
    public float aimRange = 10f;

    [Tooltip("Right stick magnitude below which aim is considered neutral (no world-point update).")]
    [Range(0f, 1f)]
    public float aimDeadzone = 0.25f;

    [Tooltip("Left or right stick magnitude above which the gamepad claims aim priority.")]
    [Range(0f, 1f)]
    public float claimDeadzone = 0.15f;

    // ─── Private ──────────────────────────────────────────────────────────────

    private Camera _cam;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        _cam = GetComponent<Camera>() ?? Camera.main;
    }

    void Update()
    {
        if (Gamepad.current == null) return;

        Vector2 leftStick  = Gamepad.current.leftStick.ReadValue();
        Vector2 rightStick = Gamepad.current.rightStick.ReadValue();

        // ── Device claim ──────────────────────────────────────────────────────
        // Moving either stick beyond the claim threshold counts as "using gamepad".
        bool leftActive  = leftStick.sqrMagnitude  > claimDeadzone * claimDeadzone;
        bool rightActive = rightStick.sqrMagnitude > claimDeadzone * claimDeadzone;

        if (leftActive || rightActive)
            IsoAim.ClaimDevice(IsoAim.Device.Gamepad);

        if (IsoAim.ActiveDevice != IsoAim.Device.Gamepad) return;

        // ── Right stick neutral → face movement direction ─────────────────────
        // Clearing HasHit lets PlayerController fall through to its movement-
        // direction fallback, so the character naturally faces where they walk.
        if (rightStick.sqrMagnitude < aimDeadzone * aimDeadzone)
        {
            IsoAim.Submit(IsoAim.Device.Gamepad, Vector3.zero, false);
            return;
        }

        Transform camT = _cam != null ? _cam.transform : transform;
        Vector3 camFwd   = camT.forward; camFwd.y = 0f;
        Vector3 camRight = camT.right;   camRight.y = 0f;

        if (camFwd.sqrMagnitude   < 0.001f) camFwd   = Vector3.forward;
        if (camRight.sqrMagnitude < 0.001f) camRight  = Vector3.right;
        camFwd.Normalize();
        camRight.Normalize();

        Vector3 aimDir = (camFwd * rightStick.y + camRight * rightStick.x).normalized;

        // ── World point = player position + aim direction × range ─────────────
        Transform player = GetLocalPlayer();
        Vector3 origin   = player != null ? player.position : Vector3.zero;
        Vector3 worldPt  = origin + aimDir * aimRange;
        worldPt.y        = origin.y; // keep flat on the player's plane

        IsoAim.Submit(IsoAim.Device.Gamepad, worldPt, true);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    static Transform GetLocalPlayer()
    {
        foreach (GameObject go in PlayerController.All)
        {
            if (go == null) continue;
            PlayerController pc = go.GetComponent<PlayerController>();
            if (pc == null) continue;
            // Networked: only the owned player. Solo (not spawned): take first found.
            if (pc.IsSpawned && !pc.IsOwner) continue;
            return pc.transform;
        }
        return null;
    }
}
