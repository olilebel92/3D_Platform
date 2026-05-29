using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Process-wide input hub. Owns the ONE shared <see cref="PlayerInputActions"/>
/// instance (generated from <c>PlayerInputActions.inputactions</c>) that every
/// system reads from — the single runtime source of truth for bindings.
///
/// ─── Why a single shared instance ─────────────────────────────────────────
/// Previously the UI/Gameplay maps were hand-built in C# here (to compile
/// without waiting for wrapper regen) AND duplicated in the asset. That split
/// meant a runtime rebind on one wouldn't affect the other. Now there is one
/// definition (the asset) and one runtime instance (this), so rebinding and
/// control-scheme switching are future-proof. <c>PlayerController</c> exposes
/// the same instance via <c>InputActions</c> for sibling components to borrow.
///
/// ─── Maps and enabling ────────────────────────────────────────────────────
///   • <see cref="UI"/>  — enabled from frame 0 (menus exist before any player).
///     Gate it inside focused text fields with <c>InputManager.UI.Disable()</c> /
///     <c>Enable()</c> (the generated struct exposes those).
///   • <see cref="Player"/> — enabled per-owner by <c>PlayerController</c> on
///     spawn; carries gameplay + the extras (CancelCast, CastSlot1/2, CycleTarget,
///     ReadyUp). Owner-gating is the caller's responsibility.
///
/// ─── Active scheme ────────────────────────────────────────────────────────
/// <see cref="ActiveScheme"/> + <see cref="OnSchemeChanged"/> are the single
/// authority for "is the player on Keyboard&amp;Mouse or Gamepad right now"
/// (driven by <see cref="InputSchemeTracker"/>). CursorManager and the iso-aim
/// device claim source from here instead of polling devices themselves.
/// </summary>
public static class InputManager
{
    public enum InputScheme { KeyboardMouse, Gamepad }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>The single shared generated actions instance.</summary>
    public static PlayerInputActions Actions { get { EnsureInit(); return _actions; } }

    /// <summary>UI hotkey actions (Pause, OpenInventory, Navigate, Point, Click, Spell1..10, …).</summary>
    public static PlayerInputActions.UIActions UI { get { EnsureInit(); return _actions.UI; } }

    /// <summary>Gameplay actions (Move, Look, Fire, CancelCast, CastSlot1/2, CycleTarget, ReadyUp, …).</summary>
    public static PlayerInputActions.PlayerActions Player { get { EnsureInit(); return _actions.Player; } }

    /// <summary>Spell hotkeys 1..10 as an array. Index 0-8 → keyboard 1..9, index 9 → keyboard 0.</summary>
    public static InputAction[] Spell { get { EnsureInit(); return _spell; } }

    /// <summary>Which control scheme produced input most recently.</summary>
    public static InputScheme ActiveScheme => _activeScheme;

    /// <summary>Fires when <see cref="ActiveScheme"/> changes (e.g. mouse→gamepad).</summary>
    public static event Action<InputScheme> OnSchemeChanged;

    // ─── Internal State ──────────────────────────────────────────────────────

    private static PlayerInputActions _actions;
    private static InputAction[] _spell;
    private static InputScheme _activeScheme = InputScheme.KeyboardMouse;

    // BeforeSceneLoad runs before any scene MonoBehaviour but late enough that
    // creating the ScriptableObject-backed asset is allowed (unlike a ctor).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        // Domain-reload-disabled editor sessions leak static state across plays.
        if (_actions != null)
        {
            _actions.Dispose();
            _actions = null;
        }
        _activeScheme = InputScheme.KeyboardMouse;
        OnSchemeChanged = null;

        EnsureInit();
        InputSchemeTracker.Ensure();
    }

    private static void EnsureInit()
    {
        if (_actions != null) return;

        _actions = new PlayerInputActions();
        var ui = _actions.UI;
        _spell = new[]
        {
            ui.Spell1, ui.Spell2, ui.Spell3, ui.Spell4, ui.Spell5,
            ui.Spell6, ui.Spell7, ui.Spell8, ui.Spell9, ui.Spell10
        };
        _actions.UI.Enable();
    }

    // Called by InputSchemeTracker (and iso-aim device claims) — fires the event
    // only on an actual change so redundant per-frame sets are free.
    internal static void SetScheme(InputScheme scheme)
    {
        if (_activeScheme == scheme) return;
        _activeScheme = scheme;
        OnSchemeChanged?.Invoke(scheme);
    }
}

/// <summary>
/// Drives <see cref="InputManager.ActiveScheme"/> by watching which device class
/// last produced meaningful input. Auto-created at startup; persists across
/// scenes. This is the ONE place allowed to poll device APIs for scheme
/// detection (the documented device-detection exception) — consolidated here so
/// CursorManager and the iso-aim scripts don't each re-implement it.
/// </summary>
internal sealed class InputSchemeTracker : MonoBehaviour
{
    private static InputSchemeTracker _instance;

    public static void Ensure()
    {
        if (_instance != null) return;
        var go = new GameObject("InputSchemeTracker");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        _instance = go.AddComponent<InputSchemeTracker>();
    }

    void OnEnable()  => InputSystem.onDeviceChange += OnDeviceChange;
    void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

    void Update()
    {
        // wasUpdatedThisFrame fires on the plug-in frame even without real input;
        // require non-default state so mere connection never triggers a switch.
        bool gamepadActive = Gamepad.current != null &&
                             Gamepad.current.wasUpdatedThisFrame &&
                             !Gamepad.current.CheckStateIsAtDefault();

        bool kbmActive = (Mouse.current != null &&
                          (Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f ||
                           Mouse.current.leftButton.wasPressedThisFrame ||
                           Mouse.current.rightButton.wasPressedThisFrame)) ||
                         (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame);

        if (gamepadActive)
            InputManager.SetScheme(InputManager.InputScheme.Gamepad);
        else if (kbmActive)
            InputManager.SetScheme(InputManager.InputScheme.KeyboardMouse);
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        // Active gamepad unplugged → revert to keyboard&mouse.
        if (device is Gamepad && change == InputDeviceChange.Removed && Gamepad.current == null)
            InputManager.SetScheme(InputManager.InputScheme.KeyboardMouse);
    }
}
