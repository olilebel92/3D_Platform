using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mouse-to-world raycast provider for the isometric camera.
/// Attach to the Main Camera alongside <see cref="IsoControllerAim"/>.
///
/// Each frame this script:
///   1. Claims <see cref="IsoAim.Device.Mouse"/> if the mouse moved or was clicked.
///   2. Skips the raycast when gamepad is the active device (saves work).
///   3. Pushes the result into <see cref="IsoAim"/> so all game systems stay in sync.
///
/// Ground Layers: set to your terrain/floor layer(s). Falls back to all layers
/// if nothing is hit, then to a horizontal plane at the last known floor height.
/// </summary>
public class IsoCursorAim : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Ground Detection")]
    [Tooltip("Layer(s) the cursor ray tests against. Include your ground / terrain layer. " +
             "Falls back to all layers automatically if nothing is hit here.")]
    public LayerMask groundLayers = ~0;

    [Tooltip("Fallback world Y used before any ground hit has been recorded.")]
    public float fallbackHeight = 0f;

    // ─── Private ──────────────────────────────────────────────────────────────

    private Camera _cam;
    private float  _lastHitY; // self-calibrates to actual floor height on first hit

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        _cam      = Camera.main;
        _lastHitY = fallbackHeight;
    }

    void Update()
    {
        // ── Device claim ──────────────────────────────────────────────────────
        // Any mouse movement or click switches aim to Mouse mode.
        if (Mouse.current != null)
        {
            bool moved   = Mouse.current.delta.ReadValue().sqrMagnitude > 0.5f;
            bool clicked = Mouse.current.leftButton.wasPressedThisFrame
                        || Mouse.current.rightButton.wasPressedThisFrame;
            if (moved || clicked)
                IsoAim.ClaimDevice(IsoAim.Device.Mouse);
        }

        // ── Skip when gamepad is driving ──────────────────────────────────────
        if (IsoAim.ActiveDevice != IsoAim.Device.Mouse) return;

        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        Vector2 screenPos = Mouse.current != null
            ? Mouse.current.position.ReadValue()
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        Ray ray = _cam.ScreenPointToRay(screenPos);

        // ── Pass 1: configured ground layer(s) ───────────────────────────────
        if (TryHit(ray, groundLayers, out Vector3 p1))
        {
            IsoAim.Submit(IsoAim.Device.Mouse, p1, true);
            return;
        }

        // ── Pass 2: all layers (floor may not be on the right layer) ─────────
        if (TryHit(ray, ~0, out Vector3 p2))
        {
            IsoAim.Submit(IsoAim.Device.Mouse, p2, true);
            return;
        }

        // ── Pass 3: horizontal plane at last known floor height ───────────────
        float denom = ray.direction.y;
        if (Mathf.Abs(denom) > 0.0001f)
        {
            float t = (_lastHitY - ray.origin.y) / denom;
            if (t > 0f)
            {
                IsoAim.Submit(IsoAim.Device.Mouse, ray.origin + ray.direction * t, true);
                return;
            }
        }

        IsoAim.Submit(IsoAim.Device.Mouse, IsoAim.WorldPoint, false); // keep last known
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    bool TryHit(Ray ray, LayerMask mask, out Vector3 point)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, 1000f, mask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit h in hits)
        {
            if (h.collider.CompareTag("Player")) continue;
            point     = h.point;
            _lastHitY = h.point.y;
            return true;
        }

        point = Vector3.zero;
        return false;
    }
}
