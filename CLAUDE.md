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
- Use `PlayerInputActions` for ALL input in new code — gameplay (Move/Look/Fire/Jump/Sprint/Interact) AND UI hotkeys (Pause/Cancel/OpenInventory/OpenSkillTree/OpenCharacter/Confirm/Navigate/Spell1..10/ToggleFPS/ToggleGodMode).
- UI managers without a player reference read input via **`InputManager.UI`** — a static holder that owns the UI action map and is alive from frame 0. Toggle with `InputManager.UI.Disable()` / `Enable()` to suppress hotkeys inside a focused text field.
- Never use legacy `UnityEngine.Input.*` (`Input.GetKey`, `Input.GetAxis`, Input Manager).
- Direct `Keyboard.current` / `Mouse.current` / `Gamepad.current` is the new system's low-level API. Allowed only for: (1) input-device detection (e.g. `CursorManager` switching cursor mode based on the active device), and (2) raw text input where Actions don't help (e.g. `LobbyChatManager` UI Toolkit chat field). Gameplay scripts that still poll device APIs (`SpellCaster`, `LootPickup`, `TargetSelector`, `IsoCursorAim`, `IsoControllerAim`, `SpectatorFreeCam`, `PlayerController` revive key, `MovementTutorialTrigger`) are pending a Player-action-map migration — separate task. Do not add new direct device polls.
- Bindings live in `Assets/Scripts/PlayerInputActions.inputactions` (canonical, used by Unity's input editor). The UI map is also built programmatically in `Assets/Scripts/InputManager.cs` so the migration compiles without waiting for `PlayerInputActions.cs` regen — when adding a UI action, update **both**.
- Actions reference: Move (Vector2), Look (MouseDelta / RStick), Jump, Sprint (hold or toggle), Fire (LMB / West button), Interact (F / South). UI map: Pause (Esc/Start), Cancel (Esc/East), OpenInventory (I/North), OpenSkillTree (K/D-pad up), OpenCharacter (C/Select), ToggleFPS (F3), ToggleGodMode (F4), Confirm (Enter/Space/E/South), Navigate (Vector2 — Arrows/LStick/D-pad), Spell1..10 (Keyboard 1-9, 0 for Spell10).

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
- Never use legacy `UnityEngine.Input.*` (`Input.GetKey` / `Input.GetAxis` / Input Manager). Direct `Keyboard.current` / `Mouse.current` / `Gamepad.current` are the new system's low-level API — use only for device-active detection (cursor mode) and raw text input. All other input goes through Actions (`InputManager.UI` or `PlayerInputActions`).
- Never call `FindObjectOfType<>()` or heavy searches in `Update()` — cache in `Awake()` or `Start()`
- Never use `DontDestroyOnLoad()` in new code unless explicitly asked. Existing exceptions: `CursorManager` and `MusicManager` (persistent input/audio singletons that survive scene loads by design)
- Player GameObject is always tagged `"Player"`
- Prefer early returns and guard clauses
- Initialize fields where possible; avoid unnecessary allocations in hot paths
