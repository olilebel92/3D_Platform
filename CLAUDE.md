# CLAUDE.md — HackNSLASH (3D Action RPG - Unity 6.0.3.11f1)

## Core Directive
- **Only read and modify files in `Assets/Scripts/` by default.**
- Never auto-read scripts unrelated to the current task. Read a script only when: the task directly involves it, OR another script you're already working on references its API and you need to understand that API.
- Never read binary/asset files (`.prefab`, `.unity`, `.asset`, `.mat`, images, audio, etc.) — list filename only if needed. Exception: ScriptableObject `.asset` files may be read as text when directly required (e.g. debugging item/spell config) — ask first.
- For networking tasks, also read `.claude/rules/networking.md`.
- For any task involving the dashboard, creating/editing ScriptableObjects (ItemData, SpellData, or any new SO type), or adding stats/enums, also read `.claude/rules/dashboard.md`.

## Tech Stack
- Unity 6.0.3.11f1 (MonoBehaviour-based)
- Language: C#
- Networking: **Netcode for GameObjects (NGO)** — support both Singleplayer and Multiplayer
- Input: `UnityEngine.InputSystem` — `PlayerInputActions.cs` is AUTO-GENERATED, never modify it
- UI: uGUI + TextMeshPro (`TMP_Text` / `TextMeshProUGUI` only)
- Navigation: `UnityEngine.AI` (NavMesh)

## Networking & Mode Sync
- Always develop for both singleplayer and multiplayer. Every change must work in both modes.
- For any networking task, read `.claude/rules/networking.md` before writing code.

## Key Systems
- `HealthSystem`: `NetworkBehaviour` with `NetworkVariable<float>` `currentHealth`/`maxHealth` synced server→clients. `TakeDamage(amount, isCrit = false, isPhysical = true, school = SpellSchool.Arcane)` / `Heal(amount)` / `IncreaseMaxHealth(amount)` / `ApplyEquipmentHP(bonus)` / `InitializeServerHP(...)` auto-forward to the server via ServerRpc when called from a client — call them unconditionally from any context (no `IsServer` guard needed at the call site). **Always pass `school:` for elemental damage** (Fire / Frost / Lightning) so resist + affinity apply; defaulting to Arcane bypasses the elemental layer. `destroyOnDeath = true` for enemies; `Die()` still has an `IsServer` guard for the despawn path — preserve it.
- `ExperienceManager.Instance`: **Per-player singleton** — `SetAsLocalInstance()` must be called after spawn. Never call `.Instance` before `OnNetworkSpawn()` completes. `GainXP(amount)` — handles XP, leveling, STR/AGI/INT/Crit stats.
- `SpellBarManager.Instance`: 10 spell slots, hotkeys 1–0.
- `DamagePopupManager.Instance`: `ShowDamage()` / `ShowHeal()` / `ShowXP()`.
- `WaveManager.Instance`: Wave spawning. Call `OnPlayerDeath()` on player death.
- `DeathScreenManager.Instance`: `ShowDeathScreen()`.
- `SkillTreeManager.Instance`: `AddSkillPoint()` on level-up.

## Input Rules
- **Single source of truth:** `Assets/Scripts/PlayerInputActions.inputactions` is the ONE definition of all bindings; `PlayerInputActions.cs` is auto-generated from it. There is ONE runtime instance, owned by `InputManager` and shared by everyone. Do NOT `new PlayerInputActions()` anywhere else — use `InputManager.Actions` (or borrow `PlayerController.InputActions`, which returns the same instance).
- Read actions through **`InputManager`**: `InputManager.UI.<Action>` (Pause/Cancel/OpenInventory/Confirm/Navigate/Point/Click/Spell1..10/…), `InputManager.Player.<Action>` (Move/Look/Fire/Jump/Sprint/Interact/CancelCast/CastSlot1/CastSlot2/CycleTarget/ReadyUp), and `InputManager.Spell[i]` for the spell-hotkey array.
- **Map enabling:** `InputManager` enables the **UI** map from frame 0 (menus exist before any player). The **Player** map is enabled per-owner by `PlayerController` on spawn (guarded by `_inputEnabled` so non-owners never toggle the shared map). Sibling components (SpellCaster, PlayerAttack, TargetSelector) just read; only `PlayerController` calls Enable/Disable.
- Suppress UI hotkeys inside a focused text field with `InputManager.UI.Disable()` / `Enable()` (the generated map struct exposes these).
- **Active scheme:** `InputManager.ActiveScheme` (`KeyboardMouse`/`Gamepad`) + `InputManager.OnSchemeChanged` are the single authority for the current device. Driven by `InputSchemeTracker` (auto-created). `CursorManager` and the iso-aim device claim source from here — do NOT re-implement device detection elsewhere.
- **Control schemes** (`Keyboard&Mouse`, `Gamepad`) are defined in the asset and every binding is tagged with its group — foundation for future rebinding / button prompts / device assignment.
- Never use legacy `UnityEngine.Input.*` (`Input.GetKey`, `Input.GetAxis`, Input Manager).
- Direct `Keyboard.current` / `Mouse.current` / `Gamepad.current` is the new system's low-level API. Allowed only for: (1) input-device detection — centralized in `InputSchemeTracker`; plus `IsoCursorAim` / `IsoControllerAim` claiming mouse-vs-gamepad aim mode (a unified `Look` action would defeat the discrimination); (2) screen-to-world pointer position for raycasts/tooltips (`IsoCursorAim`, `TargetSelector`, `LootPickup` hover) — the Input System has no cleaner equivalent; (3) raw text input where Actions don't help (e.g. `LobbyChatManager` UI Toolkit chat field). Do not add new direct device polls outside these categories. (`SpectatorFreeCam` still polls devices but is pending removal — do not migrate it.)
- **UI menu navigation (EventSystem):** the `InputSystemUIInputModule` in GameScene/LobbyScene/MainMenu currently references the default `Assets/InputSystem_Actions.inputactions` (not ours). Menus are gamepad-navigable through it. To route UI nav through our single source instead, point each module's Actions Asset at `PlayerInputActions` and map Point→UI/Point, Move→UI/Navigate, Submit→UI/Confirm, Cancel→UI/Cancel, Left Click→UI/Click (editor-only change, per scene).

## Code Style
- No custom namespaces (global scope)
- Use `[Header("...")]` for Inspector groups and `[Tooltip("...")]` on non-obvious fields
- Logging: `Debug.Log("[ClassName] message")`
- Always null-check results from `GetComponent<>()`, `FindGameObjectWithTag()`, etc.
- Cache Animator hashes: `private static readonly int AnimHash = Animator.StringToHash("ParamName");`
- Use section banners in long files: `// ─── Section Name ───`
- Fields: `private` by default. Use `[SerializeField]` for Inspector exposure
- Always use `TMP_Text` / `TextMeshProUGUI` — never legacy `UnityEngine.UI.Text`

## Hard Rules (Never Break)
- Never edit `PlayerInputActions.cs` — it auto-regenerates from `PlayerInputActions.inputactions` on reimport
- Never use legacy `UnityEngine.Input.*` (`Input.GetKey` / `Input.GetAxis` / Input Manager). Direct `Keyboard.current` / `Mouse.current` / `Gamepad.current` are the new system's low-level API — use only for the device-detection / pointer-raycast / raw-text exceptions enumerated under **Input Rules**. All other input goes through the shared instance (`InputManager.UI` / `InputManager.Player`).
- Never call `FindObjectOfType<>()` or heavy searches in `Update()` — cache in `Awake()` or `Start()`
- Never use `DontDestroyOnLoad()` in new code unless explicitly asked. Existing exceptions: `CursorManager` and `MusicManager` (persistent input/audio singletons that survive scene loads by design)
- Player GameObject is always tagged `"Player"`
- Prefer early returns and guard clauses
- Initialize fields where possible; avoid unnecessary allocations in hot paths
