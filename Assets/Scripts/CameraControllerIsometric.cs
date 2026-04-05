using UnityEngine;

/// <summary>
/// Fixed isometric / top-down camera. Pitch, yaw, and distance are locked in the
/// Inspector — the player has no control over the angle. Swap this component with
/// CameraControllerThirdPerson to switch camera modes.
/// </summary>
public class CameraControllerIsometric : MonoBehaviour
{
    // ─── Target ───────────────────────────────────────────────────────────────

    [Header("Target")]
    [Tooltip("The transform the camera orbits around (assign the Player root).")]
    public Transform target;

    // ─── Isometric Settings ───────────────────────────────────────────────────

    [Header("Isometric Settings")]
    [Tooltip("Vertical angle in degrees (0 = horizontal, 90 = straight down). Diablo is ~65–70.")]
    public float pitch = 70f;

    [Tooltip("Horizontal rotation of the camera around the world Y axis. 45 = classic isometric diagonal.")]
    public float yaw = 45f;

    [Tooltip("Distance from the target along the camera's backward direction.")]
    public float distance = 18f;

    [Tooltip("Vertical offset applied to the look-at point so the camera aims slightly above the feet.")]
    public float heightOffset = 1f;

    // ─── Follow Smoothing ─────────────────────────────────────────────────────

    [Header("Follow Smoothing")]
    [Tooltip("How quickly the camera follows the target. 0 = instant.")]
    public float smoothSpeed = 0f;

    /// <summary>Set to true by any UI panel that needs to freeze the camera.</summary>
    public static bool IsLocked = false;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void LateUpdate()
    {
        // Auto-find the local player if no target is assigned.
        if (target == null)
        {
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                // In networked play: only follow the owned player.
                // In solo play: IsSpawned is false, so just take the first one found.
                if (!pc.IsSpawned || pc.IsOwner)
                {
                    target = pc.transform;
                    Debug.Log("[CameraControllerIsometric] Auto-found player target: " + target.name);
                    break;
                }
            }
            return; // wait until next frame once target is set
        }

        if (Time.timeScale == 0f) return;

        Vector3 lookAtPoint = target.position + Vector3.up * heightOffset;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredPosition = lookAtPoint - rotation * Vector3.forward * distance;

        if (smoothSpeed > 0f)
            transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        else
            transform.position = desiredPosition;

        transform.LookAt(lookAtPoint);
    }
}
