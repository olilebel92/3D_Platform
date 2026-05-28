using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Process-wide owner of the UI action map. Lets managers that don't have a
/// Player reference (UI panels, CursorManager, MusicManager, debug tools) read
/// input via Actions instead of polling <c>Keyboard.current</c> /
/// <c>Mouse.current</c> / <c>Gamepad.current</c> directly.
///
/// ─── Why this exists separately from <c>PlayerInputActions</c> ────────────
/// <see cref="PlayerInputActions"/> is auto-generated from the .inputactions
/// asset and is owned per-player by <c>PlayerController</c>. UI managers run
/// in scenes without a player (Main Menu, Lobby) and must work before any
/// player spawns. InputManager constructs its UI map programmatically so it
/// is independent of player lifecycle, scene loads, and the regeneration
/// timing of <c>PlayerInputActions.cs</c>.
///
/// ─── Keeping bindings in sync ─────────────────────────────────────────────
/// The same bindings are defined in BOTH places:
///   • <c>Assets/Scripts/PlayerInputActions.inputactions</c> — canonical
///     source for the Unity input editor and future runtime rebinding UI.
///   • This file — runtime fallback that builds the same map in code.
/// When you change a UI binding, update both. The asset is what shows up in
/// the Unity Input Action editor; this code is what executes at runtime.
///
/// ─── Lifecycle ────────────────────────────────────────────────────────────
/// Initialised on <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>
/// so the UI map is live from frame 0. The map persists across scene loads
/// because it's owned by a static field, not a GameObject.
/// </summary>
public static class InputManager
{
    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>UI hotkey actions (Pause, OpenInventory, Spell1..10, etc.).</summary>
    public static UIActions UI => _ui;

    /// <summary>The raw <see cref="InputActionMap"/> backing <see cref="UI"/>.
    /// Use for advanced scenarios (rebinding UI, action callbacks).</summary>
    public static InputActionMap UIMap => _ui.Map;

    // ─── Internal State ──────────────────────────────────────────────────────

    private static UIActions _ui;

    // SubsystemRegistration runs once per play session, BEFORE any scene scripts.
    // This guarantees the UI map exists before the first Update() of any UI
    // manager. Tearing down any prior map handles the Editor case where domain
    // reload is disabled and static state leaks across play sessions.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Bootstrap()
    {
        // Direct field access (not a method call) — UIActions is a struct, so
        // calling Dispose() on it would mutate a copy and leave the static
        // field's Map reference alive.
        if (_ui.Map != null)
        {
            _ui.Map.Disable();
            _ui.Map.Dispose();
        }

        _ui = UIActions.Build();
        _ui.Map.Enable();
    }

    // ─── UI Action Map ───────────────────────────────────────────────────────

    /// <summary>
    /// Strongly-typed handle to the UI input action map. Constructed once at
    /// startup; bindings mirror those in <c>PlayerInputActions.inputactions</c>.
    /// </summary>
    public struct UIActions
    {
        public InputActionMap Map;
        public InputAction Pause;
        public InputAction Cancel;
        public InputAction OpenInventory;
        public InputAction OpenSkillTree;
        public InputAction OpenCharacter;
        public InputAction ToggleFPS;
        public InputAction ToggleGodMode;
        public InputAction Confirm;
        public InputAction Navigate;

        /// <summary>Spell hotkeys 1..10. Indices 0-8 map to keyboard 1..9, index 9 maps to keyboard 0.</summary>
        public InputAction[] Spell;

        /// <summary>Disables the entire UI map. Use when entering a context that
        /// should consume input directly (e.g. an editable text field).</summary>
        public void Disable()
        {
            if (Map != null && Map.enabled) Map.Disable();
        }

        /// <summary>Re-enables the UI map after a previous <see cref="Disable"/>.</summary>
        public void Enable()
        {
            if (Map != null && !Map.enabled) Map.Enable();
        }

        internal static UIActions Build()
        {
            var map = new InputActionMap("UI");
            var r = new UIActions { Map = map, Spell = new InputAction[10] };

            r.Pause          = map.AddAction("Pause",         InputActionType.Button);
            r.Cancel         = map.AddAction("Cancel",        InputActionType.Button);
            r.OpenInventory  = map.AddAction("OpenInventory", InputActionType.Button);
            r.OpenSkillTree  = map.AddAction("OpenSkillTree", InputActionType.Button);
            r.OpenCharacter  = map.AddAction("OpenCharacter", InputActionType.Button);
            r.ToggleFPS      = map.AddAction("ToggleFPS",     InputActionType.Button);
            r.ToggleGodMode  = map.AddAction("ToggleGodMode", InputActionType.Button);
            r.Confirm        = map.AddAction("Confirm",       InputActionType.Button);
            r.Navigate       = map.AddAction("Navigate",      InputActionType.Value, expectedControlLayout: "Vector2");

            r.Pause.AddBinding("<Keyboard>/escape");
            r.Pause.AddBinding("<Gamepad>/start");

            r.Cancel.AddBinding("<Keyboard>/escape");
            r.Cancel.AddBinding("<Gamepad>/buttonEast");

            r.OpenInventory.AddBinding("<Keyboard>/i");
            r.OpenInventory.AddBinding("<Gamepad>/buttonNorth");

            r.OpenSkillTree.AddBinding("<Keyboard>/k");
            r.OpenSkillTree.AddBinding("<Gamepad>/dpad/up");

            r.OpenCharacter.AddBinding("<Keyboard>/c");
            r.OpenCharacter.AddBinding("<Gamepad>/select");

            r.ToggleFPS.AddBinding("<Keyboard>/f3");
            r.ToggleGodMode.AddBinding("<Keyboard>/f4");

            r.Confirm.AddBinding("<Keyboard>/enter");
            r.Confirm.AddBinding("<Keyboard>/space");
            r.Confirm.AddBinding("<Keyboard>/e");
            r.Confirm.AddBinding("<Gamepad>/buttonSouth");

            r.Navigate.AddCompositeBinding("2DVector")
                .With("Up",    "<Keyboard>/upArrow")
                .With("Down",  "<Keyboard>/downArrow")
                .With("Left",  "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            r.Navigate.AddBinding("<Gamepad>/leftStick");
            r.Navigate.AddBinding("<Gamepad>/dpad");

            // Spell1..Spell9 → Keyboard 1..9. Spell10 → Keyboard 0.
            string[] digits = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
            for (int i = 0; i < 10; i++)
            {
                r.Spell[i] = map.AddAction("Spell" + (i + 1), InputActionType.Button);
                r.Spell[i].AddBinding("<Keyboard>/" + digits[i]);
            }

            return r;
        }
    }
}
