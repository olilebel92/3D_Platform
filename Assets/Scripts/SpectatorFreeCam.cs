using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

/// <summary>
/// Free-roam spectator camera. Attach to the Camera GameObject alongside
/// CameraControllerIsometric / CameraControllerThirdPerson.
///
/// Activates automatically on spectator clients (non-host). Disables all
/// regular camera controllers so it has full control.
///
/// Controls:
///   WASD       — move horizontally
///   Q / E      — move down / up
///   Mouse      — look around (hold Right Mouse Button)
///   Left Shift — hold for fast move
/// </summary>
public class SpectatorFreeCam : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Movement")]
    [Tooltip("Normal fly speed.")]
    public float moveSpeed = 12f;

    [Tooltip("Speed when holding Left Shift.")]
    public float fastSpeed = 35f;

    [Header("Look")]
    [Tooltip("Mouse sensitivity for looking around.")]
    public float mouseSensitivity = 0.15f;

    [Tooltip("Gamepad right-stick sensitivity for looking around.")]
    public float stickSensitivity = 90f;

    // ─── Private State ────────────────────────────────────────────────────────

    private float _yaw;
    private float _pitch;
    private bool  _cursorLocked = true;
    private bool  _positioned;     // true once we've snapped to a spawned player

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        // Activate if the local player chose the Spectator class in the lobby.
        // Falls back to the old behaviour (any non-host client) when no LobbyPlayer exists
        // (e.g. direct scene load for testing).
        bool isSpectator = false;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            LobbyPlayer mine = null;
            foreach (LobbyPlayer lp in FindObjectsByType<LobbyPlayer>(FindObjectsSortMode.None))
                if (lp.IsOwner) { mine = lp; break; }

            if (mine != null)
                isSpectator = mine.IsSpectator;
            else
                isSpectator = NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost;
        }

        if (!isSpectator)
        {
            enabled = false;
            return;
        }

        // Disable regular camera controllers so they stop fighting for control.
        var switcher = GetComponent<CameraModeSwitcher>();
        if (switcher != null) switcher.enabled = false;

        var iso = GetComponent<CameraControllerIsometric>();
        if (iso != null) iso.enabled = false;

        var tp = GetComponent<CameraControllerThirdPerson>();
        if (tp != null) tp.enabled = false;

        // Initialise yaw/pitch from the camera's current rotation so there's no snap.
        _yaw   = transform.eulerAngles.y;
        _pitch = transform.eulerAngles.x;

        LockCursor(true);

        Debug.Log("[SpectatorFreeCam] Spectator mode active — WASD to move, mouse to look, Escape to release cursor.");
    }

    void Update()
    {
        // Mirror the same freeze conditions as the regular camera.
        // Since the spectator's HUD is hidden they can't trigger these themselves —
        // the camera will only freeze when the host's game does.
        if (Time.timeScale == 0f)       return;
        if (CameraControllerThirdPerson.IsLocked) return;

        // Snap to near the first spawned player on the first frame they exist.
        // Players are spawned by PlayerSpawner after the scene loads, so they
        // may not exist yet when SpectatorFreeCam.Start() fires.
        if (!_positioned)
            TryPositionNearPlayer();

        HandleLook();
        HandleMovement();
    }

    void TryPositionNearPlayer()
    {
        PlayerController pc = FindAnyObjectByType<PlayerController>();
        if (pc == null) return;

        // Hover above and slightly behind the first player we find.
        transform.position = pc.transform.position + Vector3.up * 6f + pc.transform.forward * -4f;
        _yaw    = transform.eulerAngles.y;
        _pitch  = 20f;
        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        _positioned = true;
        Debug.Log("[SpectatorFreeCam] Positioned near player: " + pc.name);
    }

    // ─── Look ─────────────────────────────────────────────────────────────────

    void HandleLook()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            // Toggle cursor lock so the player can interact with Unity editor / OS.
            LockCursor(!_cursorLocked);
        }

        float lookX = 0f;
        float lookY = 0f;

        // Mouse — only when cursor is locked.
        if (_cursorLocked && Mouse.current != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            lookX += delta.x * mouseSensitivity;
            lookY += delta.y * mouseSensitivity;
        }

        // Gamepad right stick — always active.
        if (Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.rightStick.ReadValue();
            lookX += stick.x * stickSensitivity * Time.deltaTime;
            lookY += stick.y * stickSensitivity * Time.deltaTime;
        }

        if (lookX == 0f && lookY == 0f) return;

        _yaw   += lookX;
        _pitch -= lookY;
        _pitch  = Mathf.Clamp(_pitch, -89f, 89f);

        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    void LockCursor(bool locked)
    {
        _cursorLocked    = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;
    }

    // ─── Movement ─────────────────────────────────────────────────────────────

    void HandleMovement()
    {
        Vector3 dir   = Vector3.zero;
        bool    fast  = false;

        // ── Keyboard ──────────────────────────────────────────────────────────
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) dir += transform.forward;
            if (Keyboard.current.sKey.isPressed) dir -= transform.forward;
            if (Keyboard.current.dKey.isPressed) dir += transform.right;
            if (Keyboard.current.aKey.isPressed) dir -= transform.right;
            if (Keyboard.current.eKey.isPressed) dir += Vector3.up;
            if (Keyboard.current.qKey.isPressed) dir -= Vector3.up;

            fast = Keyboard.current.leftShiftKey.isPressed;
        }

        // ── Gamepad ───────────────────────────────────────────────────────────
        // Left stick  → horizontal fly
        // L2 / R2     → descend / ascend
        // Left Stick click (L3) → fast mode
        if (Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.leftStick.ReadValue();
            if (stick.sqrMagnitude > 0.01f)
            {
                dir += transform.forward * stick.y;
                dir += transform.right   * stick.x;
            }

            if (Gamepad.current.rightTrigger.isPressed) dir += Vector3.up;
            if (Gamepad.current.leftTrigger.isPressed)  dir -= Vector3.up;

            if (Gamepad.current.leftStickButton.isPressed) fast = true;
        }

        if (dir.sqrMagnitude < 0.001f) return;

        float speed = fast ? fastSpeed : moveSpeed;
        transform.position += dir.normalized * speed * Time.deltaTime;
    }
}
