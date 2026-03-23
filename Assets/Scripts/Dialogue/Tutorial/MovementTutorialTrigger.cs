using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shows a "Use WASD to move" popup when the game starts.
/// Automatically dismisses it the moment the player inputs any movement.
/// Attach this to any GameObject in your opening scene.
/// </summary>
public class MovementTutorialTrigger : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Popup Settings")]
    [Tooltip("Message shown to the player at the start.")]
    public string message = "Use WASD or left stick to move";

    [Tooltip("Seconds to wait before checking for movement input (prevents instant dismiss).")]
    public float inputDelay = 0.5f;

    // ─── Private State ────────────────────────────────────────────────────────

    private bool _dismissed = false;
    private float _timer = 0f;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private bool _hasShown = false;

    void Start()
    {
        if (_hasShown) return;

        if (PopupManager.Instance == null)
        {
            Debug.LogWarning("[MovementTutorialTrigger] PopupManager instance not found in scene!");
            return;
        }

        _hasShown = true;
        PopupManager.Instance.Show(message, duration: 9999f); // stays open until dismissed
        Debug.Log("[MovementTutorialTrigger] Movement tutorial popup shown.");
    }

    void Update()
    {
        if (_dismissed) return;

        // Wait out the input delay before listening for movement
        _timer += Time.unscaledDeltaTime;
        if (_timer < inputDelay) return;

        // Check for any WASD or left stick movement
        if (PlayerIsMoving())
            Dismiss();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private bool PlayerIsMoving()
    {
        // Keyboard — any of the four movement keys
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed ||
                Keyboard.current.aKey.isPressed ||
                Keyboard.current.sKey.isPressed ||
                Keyboard.current.dKey.isPressed)
                return true;
        }

        // Gamepad — left stick past a small dead zone
        if (Gamepad.current != null)
        {
            if (Gamepad.current.leftStick.ReadValue().magnitude > 0.2f)
                return true;
        }

        return false;
    }

    private void Dismiss()
    {
        _dismissed = true;
        PopupManager.Instance.Hide();
        Debug.Log("[MovementTutorialTrigger] Player moved — tutorial popup dismissed.");
    }
}