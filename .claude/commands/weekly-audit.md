You are a senior Unity developer performing a weekly code audit on HackNSLASH (Unity 6, URP, Netcode for GameObjects).

**Think carefully before writing each section.** The value of this audit is not in listing files — it is in architectural judgment: spotting coupling that will hurt next month, NGO authority bugs that will only manifest in multiplayer, and prioritizing what to fix *this week* vs. later. Rushing the analysis produces generic output. Take your time on Coupling Issues, Networking Safety, and the Priority Fix List.

## Step 1 — Gather context (run in parallel)

Accept an optional `--since` argument (default: `7 days ago`). Examples: `14 days ago`, `2026-04-01`.

Run these in a single parallel batch:

```bash
find Assets/Scripts -name "*.cs" | sort
```

```bash
git log --since="7 days ago" --name-only --pretty=format: -- "Assets/Scripts/**/*.cs" | sort -u | grep "\.cs$"
```

```bash
git status --short Assets/Scripts/
```

```bash
git log --since="7 days ago" --stat -- "Assets/Scripts/**/*.cs"
```

```bash
git diff "@{7 days ago}" HEAD -- "Assets/Scripts/**/*.cs"
```

The `--stat` output gives you a change-volume sense (which files churned most). The `git diff` output is your **primary signal** — read it fully before reading any whole file. Most smells are visible in the diff alone.

## Step 2 — Read changed scripts (batched)

After absorbing the diff, decide which changed scripts need a full read for context (e.g. you need to see the surrounding class to judge if a new method belongs there). **Read all of them in a single parallel batch** — do not read one, reason, then read the next. You need the full picture before writing *any* section of the report.

Also read any unchanged script whose API is referenced by a changed script **only if** the diff alone is ambiguous about the contract.

Do NOT read unchanged scripts for general context. Do NOT read binary/asset files.

## Step 3 — Produce the audit report

Output a structured report with the following sections, in order.

---

### 🗂 All Scripts (inventory)
List every `.cs` file found, one per line. No description — just the filename (no path).

---

### 🔨 This Week's Changes
List scripts added or modified in the window. For each, write one sentence: which system it belongs to and what the change does (not what the file does — what *changed*).

---

### 📡 Networking Safety (NGO-specific)
Scan the diff for any of the following. Flag each with file + line reference. This section is the highest-priority class of bugs in this project — be thorough.

- `ServerRpc` methods missing authority validation or `RequireOwnership` misuse
- `ClientRpc` called from a non-server context
- `NetworkVariable<T>` writes from a client that doesn't own authority
- Input-reading methods in a `NetworkBehaviour` missing `if (!IsOwner) return;`
- `HealthSystem.TakeDamage()` / `Heal()` called without a server-side guard at the call site
- Spell / damage scripts calling `HealthSystem.TakeDamage(...)` **without an explicit `school:` argument** when the spell has an elemental school (Fire / Frost / Lightning). Defaulting to `SpellSchool.Arcane` silently bypasses elemental resist + affinity. Grep for `TakeDamage(` in `Assets/Scripts/Spells/` and the diff; flag any call where the spell's school is non-Arcane but `school:` is missing.
- Server-spawned spell projectiles / AOE scripts (`Fireball`, `ChainLightning`, `DamageZone`, etc.) reading per-player singletons (`ExperienceManager.Instance`, `SkillTreeManager.Instance`) inside `OnNetworkSpawn` / `Explode` / `OnTriggerEnter` / any authoritative path. Required pattern: `SpellCaster` pre-computes damage owner-side and assigns it to a `[HideInInspector]` runtime field on the prefab **before** `NetworkObject.Spawn()` (see `Fireball.precomputedDamage`). Host-local singletons would otherwise leak host stats into every caster's damage in MP.
- `ExperienceManager.Instance` accessed before `OnNetworkSpawn()` / `SetAsLocalInstance()`
- `Die()` modifications that remove or bypass the `IsServer` guard for `destroyOnDeath`
- Movement/teleport calls against `OwnerNetworkTransform` from the server (must go through an owner-targeted `ClientRpc`)
- `Start()` logic that does NGO-aware work without a single-player fallback (project rule: `OnNetworkSpawn` only fires when NGO is active)

If nothing is flagged, say **"No networking safety issues detected in this window."** — don't invent items.

---

### 🔗 Coupling Issues
Scripts that look too tightly coupled — direct field references across systems, `GetComponent` chains, or heavy cross-manager dependencies introduced or worsened this week. For each, explain the concrete risk (what breaks when you change what).

---

### ❓ Missing Links
Managers, components, or systems referenced in the changed code that don't appear in the file list (e.g. a `RewardManager` referenced but not present). For each, note the referencing script and line.

---

### ⚠️ Design Smells & Runtime Risks
Flag any of the following **found in the diff** (not in unchanged code):
- `FindObjectOfType<>()` / `FindObjectsOfType<>()` — especially in `Update()`
- Missing null checks after `GetComponent<>()` or tag searches
- Singleton abuse (many `.Instance` calls in one class)
- Logic running on all clients that should be server-only (overlap with Networking Safety — cross-reference, don't duplicate)
- `Start()` / `Awake()` logic assuming execution order without enforcement
- Allocations in hot paths (`Update`, `FixedUpdate`, per-frame loops): `new List<>()`, `string` concat, LINQ, `GetComponent` without caching
- Animator string lookups not cached via `Animator.StringToHash`
- Legacy Input API usage (`Input.GetKey`, `Input.GetAxis`, Input Manager) — hard rule violation
- **New** direct-device polling introduced in the diff (`Keyboard.current.*`, `Mouse.current.*`, `Gamepad.current.*`). The existing ~15 UI scripts using these are tracked legacy debt pending a `UI` action map migration — adding new ones is banned per CLAUDE.md. Flag any net-new occurrence in changed files; do NOT flag pre-existing usage in unchanged files.
- Legacy `UnityEngine.UI.Text` usage — must be `TMP_Text` / `TextMeshProUGUI`

---

### 🔄 Event Suggestions
For each direct manager-to-manager or script-to-script call found in the diff, evaluate whether it should instead use a C# event, UnityEvent, or NGO NetworkVariable. Format:
- **`ScriptA → ScriptB.Method()`** → suggest event `OnSomethingHappened` on ScriptA, subscribed by ScriptB.

Only suggest an event when it would meaningfully reduce coupling. Do not suggest events for calls that are inherently one-directional setup (e.g. manager bootstrapping).

---

### 🗺 Dependency Map
Flat list of inferred dependencies **from the changed scripts only**:
```
ScriptA → ScriptB
ScriptA → ScriptC
ScriptB → ScriptD
```

---

### 🚨 Priority Fix List (Top 3–5)
Order by risk to stability and multiplayer correctness. Networking safety issues outrank design smells unless the design smell is a guaranteed crash. For each:
- **Title**: one-line description
- **Risk**: concrete failure mode — what breaks, when, and for whom (host? client? both?)
- **Fix**: specific action with file + line. Not "refactor X" — say what to write.

---

### ⚡ Quick Wins (< 10 minutes each)
Separate from the Priority Fix List. Tiny fixes worth doing before the main work: missing null check, cache a `StringToHash`, rename a confusing field, remove a dead `using`, swap a `FindObjectOfType` for an existing cached reference. File + line for each. If there are none, omit the section.
