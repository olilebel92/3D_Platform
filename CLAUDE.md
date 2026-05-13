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
- `HealthSystem`: `TakeDamage(amount, isCrit)` / `Heal(amount)` / `ApplyEquipmentHP(bonus)`. Plain MonoBehaviour — damage must be applied server-side only. `destroyOnDeath = true` for enemies; `Die()` has an `IsServer` guard — preserve it.
- `ExperienceManager.Instance`: **Per-player singleton** — `SetAsLocalInstance()` must be called after spawn. Never call `.Instance` before `OnNetworkSpawn()` completes. `GainXP(amount)` — handles XP, leveling, STR/AGI/INT/Crit stats.
- `SpellBarManager.Instance`: 10 spell slots, hotkeys 1–0.
- `DamagePopupManager.Instance`: `ShowDamage()` / `ShowHeal()` / `ShowXP()`.
- `WaveManager.Instance`: Wave spawning. Call `OnPlayerDeath()` on player death.
- `DeathScreenManager.Instance`: `ShowDeathScreen()`.
- `SkillTreeManager.Instance`: `AddSkillPoint()` on level-up.

## Input Rules
- Use **only** `PlayerInputActions` bindings.
- Never use legacy `Input.GetKey`, `Input.GetAxis`, or Input Manager.
- Actions reference: Move (Vector2), Look (MouseDelta / RStick), Jump, Sprint (hold or toggle), Fire (LMB / West button).

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
- Never edit `PlayerInputActions.cs`
- Never use legacy Input system
- Never call `FindObjectOfType<>()` or heavy searches in `Update()` — cache in `Awake()` or `Start()`
- Never use `DontDestroyOnLoad()` unless explicitly asked
- Player GameObject is always tagged `"Player"`
- Prefer early returns and guard clauses
- Initialize fields where possible; avoid unnecessary allocations in hot paths
