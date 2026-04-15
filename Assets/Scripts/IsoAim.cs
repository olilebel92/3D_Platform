using UnityEngine;

/// <summary>
/// Static aim facade — the single source of truth for world-space aim point.
///
/// All game systems (SpellCaster, TelegraphProjector, PlayerController) read from here.
/// <see cref="IsoCursorAim"/> and <see cref="IsoControllerAim"/> each write here
/// when their device is active.
///
/// Device switching is automatic: whichever device has meaningful input last wins.
/// </summary>
public static class IsoAim
{
    // ─── Device ───────────────────────────────────────────────────────────────

    public enum Device { Mouse, Gamepad }

    /// <summary>Which input device is currently driving aim.</summary>
    public static Device ActiveDevice { get; private set; } = Device.Mouse;

    // ─── Aim Data ─────────────────────────────────────────────────────────────

    /// <summary>World position of the current aim point this frame.</summary>
    public static Vector3 WorldPoint { get; private set; }

    /// <summary>False only when neither device has valid aim data this frame.</summary>
    public static bool HasHit { get; private set; }

    /// <summary>
    /// Flat (XZ) normalised direction from <paramref name="origin"/> to <see cref="WorldPoint"/>.
    /// Returns <c>Vector3.forward</c> when WorldPoint is directly over the origin.
    /// </summary>
    public static Vector3 AimDirectionFrom(Vector3 origin)
    {
        Vector3 dir = WorldPoint - origin;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
    }

    // ─── Write API (used only by IsoCursorAim / IsoControllerAim) ────────────

    /// <summary>
    /// Switches the active aim device. Call this when meaningful input is detected
    /// on a device — last caller in the frame wins.
    /// </summary>
    public static void ClaimDevice(Device device) => ActiveDevice = device;

    /// <summary>
    /// Writes the aim world point. Ignored if <paramref name="device"/> is not
    /// the current active device, preventing a dormant device from overwriting.
    /// </summary>
    public static void Submit(Device device, Vector3 worldPoint, bool hasHit)
    {
        if (device != ActiveDevice) return;
        WorldPoint = worldPoint;
        HasHit     = hasHit;
    }
}
