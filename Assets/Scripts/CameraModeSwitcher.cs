using UnityEngine;

/// <summary>
/// Attach this alongside CameraControllerIsometric and CameraControllerThirdPerson
/// on the same Camera GameObject. Use the Active Mode dropdown to enable one script
/// and disable the other instantly — works both in Edit mode and Play mode.
/// </summary>
public class CameraModeSwitcher : MonoBehaviour
{
    public enum CameraMode { Isometric, ThirdPerson }

    [Header("Camera Mode")]
    [Tooltip("Switch between Isometric (top-down) and Third-Person orbit camera.")]
    public CameraMode activeMode = CameraMode.Isometric;

    // ─── Auto-wire on reset / first add ──────────────────────────────────────

    void Reset() => ApplyMode();

    // ─── Fires when any Inspector value changes ───────────────────────────────

    void OnValidate() => ApplyMode();

    // ─── Also apply at runtime start ─────────────────────────────────────────

    void Awake() => ApplyMode();

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the locally owned PlayerController after it spawns.
    /// Sets the follow target on both camera controllers at once.
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        var iso = GetComponent<CameraControllerIsometric>();
        var tp  = GetComponent<CameraControllerThirdPerson>();

        if (iso != null) iso.target = newTarget;
        if (tp  != null) tp.target  = newTarget;

        Debug.Log("[CameraModeSwitcher] Target set to: " + newTarget.name);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    void ApplyMode()
    {
        var iso = GetComponent<CameraControllerIsometric>();
        var tp  = GetComponent<CameraControllerThirdPerson>();

        if (iso != null) iso.enabled = (activeMode == CameraMode.Isometric);
        if (tp  != null) tp.enabled  = (activeMode == CameraMode.ThirdPerson);
    }
}
