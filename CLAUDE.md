# CLAUDE.md — 3D Action RPG (Unity 6)

This file gives Claude Code persistent context about this Unity project.
Read it at the start of every session.

---

## Project Overview

3D third-person action RPG built in Unity 6. Player explores, fights enemies,
collects items, casts spells, and levels up. Written in C# using Unity's
New Input System via a generated `PlayerInputActions` asset.

---

## Tools & Environment

- **Engine**: Unity 6 (New Input System enabled)
- **Language**: C# (.NET, Unity scripting API)
- **Input**: `UnityEngine.InputSystem` — `PlayerInputActions.cs` is AUTO-GENERATED, never modify it
- **UI**: Unity uGUI + TextMeshPro (TMPro)
- **Navigation**: Unity NavMesh (`UnityEngine.AI`) for enemy pathfinding
- **Architecture**: MonoBehaviour-based; singletons for `ExperienceManager` and `SpellBarManager`

---

## Scripts Location

All scripts live in `Assets/Scripts/`

### Player
| Script | Responsibility |
|---|---|
| `PlayerController.cs` | Movement, jumping, sprinting (hold + toggle), camera-relative direction |
| `PlayerInputActions.cs` | ⚠️ Auto-generated — do not edit |
| `StaminaSystem.cs` | Stamina drain/recharge, exhaustion state, UI bar |
| `HealthSystem.cs` | HP for player and enemies; `TakeDamage()`, `Heal()`, `Die()` |
| `FireballShooter.cs` | Fires a prefab from a fire point on input |
| `PlayerAttack.cs` | Melee attack; damage scales with STR; cooldown reduced by AGI |

### Camera
| Script | Responsibility |
|---|---|
| `CameraController.cs` | Orbit camera around player; mouse and gamepad stick |

### Enemies
| Script | Responsibility |
|---|---|
| `EnemyAI.cs` | State machine: Idle → Chase → Attack via NavMeshAgent |
| `EnemySpawner.cs` | Timed spawning at defined points, respects max enemy cap |
| `EnemyTracker.cs` | Auto-added to spawned enemies; notifies spawner on death |

### Progression & Items
| Script | Responsibility |
|---|---|
| `ExperienceManager.cs` | Singleton. XP, leveling, STR/AGI/INT stats, UI |
| `CoinPickup.cs` | Awards XP on trigger, plays FX, destroys self |
| `ItemSpawner.cs` | Spawns items via downward raycast onto terrain |
| `PickupItem.cs` | Bob + spin visual; notifies spawner on collection |

### Spells / UI
| Script | Responsibility |
|---|---|
| `SpellBarManager.cs` | Singleton. 10 spell slots; hotkeys 1–0 |
| `SpellSlot.cs` | Single slot UI: icon, highlight, click-to-select, drag-and-drop |
| `SpellData.cs` | ScriptableObject: spell name, description, icon |
| `CharacterWindow.cs` | Press C to open stats window (level, XP, HP, STR, AGI, etc.) |
| `DamagePopup.cs` | Floating damage numbers, rises and fades |

### Utilities
| Script | Responsibility |
|---|---|
| `DamageZone.cs` | Trigger zone that damages player on enter |
| `ControllerDebug.cs` | Debug helper — logs joystick button presses |

---

## Core Systems

### Input Actions (never use legacy Input.GetKey / Input.GetAxis)
| Action | Type | Keyboard | Gamepad |
|---|---|---|---|
| `Move` | Vector2 | WASD | Left Stick |
| `Look` | Vector2 | Mouse Delta | Right Stick |
| `Jump` | Button | Space | Button South |
| `Sprint` | Button (hold) | Left Shift | — |
| `SprintToggle` | Button | — | L3 |
| `Fire` | Button | Left Mouse Button | Button West |

### Singletons
- `ExperienceManager.Instance` — XP and leveling
- `SpellBarManager.Instance` — Spell bar UI
- Both use Awake() duplicate-destruction guard

### Health & Damage
- `HealthSystem` is the single source of truth for HP
- Use `health.TakeDamage(amount)` and `health.Heal(amount)`
- `destroyOnDeath = true` on enemies; player does not auto-destroy

### Stamina & Sprint
- Hold `Sprint` (Left Shift) OR toggle with `SprintToggle` (Gamepad L3)
- `StaminaSystem.CanSprint()` gates both paths
- Toggle auto-disables on exhaustion

### XP & Leveling
- Call `ExperienceManager.Instance.GainXP(amount)` from pickups
- Level-up handles multi-level jumps automatically
- Stats: STR (melee damage), AGI (speed + cooldown reduction), INT (future spells)
- AGI bonuses configured in ExperienceManager Inspector:
  - `agiMoveSpeedBonus` — flat speed per AGI point
  - `agiSprintSpeedBonus` — flat sprint speed per AGI point
  - `agiCooldownReduction` — seconds off attack cooldown per AGI point
  - `agiMinAttackCooldown` — hard floor for attack cooldown

### Enemy Lifecycle
- Spawned by `EnemySpawner`, which auto-attaches `EnemyTracker`
- On death, `EnemyTracker.OnDestroy()` calls `spawner.EnemyDestroyed()`

---

## Code Style Rules

1. No custom namespace — all classes are global scope
2. `[Header("Section Name")]` to group Inspector fields
3. `[Tooltip("...")]` on all non-obvious public fields
4. Debug logs formatted as `Debug.Log("[ClassName] message")`
5. Always null-check components from `GetComponent` or `FindGameObjectWithTag`
6. Cache animator params: `private static readonly int Hash = Animator.StringToHash("Param")`
7. Use `// ─── Section ───` banners to separate logical blocks in long files
8. Fields are `private` unless they need Inspector exposure (`public` or `[SerializeField] private`)
9. Always use `TextMeshProUGUI` / `TMP_Text` — never legacy `UnityEngine.UI.Text`

---

## What NOT To Do

- ❌ Never edit `PlayerInputActions.cs` — it is auto-generated
- ❌ Never use `Input.GetAxis` or `Input.GetKey` (legacy Input Manager)
- ❌ Never add `DontDestroyOnLoad` unless explicitly requested
- ❌ Never call `FindObjectOfType` in `Update()` — cache in `Awake()` or `Start()`
- ❌ Never use `UnityEngine.UI.Text` — use TextMeshPro

---

## Player GameObject Tag
The player is tagged `"Player"` — used project-wide for tag comparisons.
