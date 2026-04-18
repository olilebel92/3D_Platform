Update the roadmap, changelog, and in-game DevLog for a new or updated version entry.

Arguments: $ARGUMENTS
Format expected: `<version> <status> - <title> | bullet1 | bullet2 | ...`
Example: `v0.05 completed - Networking Polish | Enemy color randomizer | Lobby chat | NGO optimization`
Status values: `active` (in progress), `completed`, `planned`

Steps:

1. Read `docs/roadmap.html` and `Assets/Scripts/DevLog.cs`.

2. **docs/roadmap.html — VERSIONS array:**
   - If the version already exists: update its `status`, `date`, `bullets`, `details`, and `note` in place.
   - If it does not exist: insert a new entry at the correct position (sorted by version number).
   - Status mapping: `active` → `"active"`, `completed` → `"completed"`, `planned` → `"planned"`.
   - For `completed`, set `date` to today's date. For `active`, prefix date with `"In Progress — "`.

3. **docs/roadmap.html — Changelog section:**
   - If a `cl-entry` for this version already exists: update it in place (bullets, status badge, date, WIP note).
   - If it does not exist: insert a new `cl-entry` block at the top of `.changelog-wrapper`, above all existing entries.
   - Badge class: `badge-completed` for completed, `badge-active` for active, `badge-planned` for planned.
   - If status is `active`, add a `<div class="cl-note"><strong>WIP:</strong> ...</div>` describing what remains.

4. **Assets/Scripts/DevLog.cs — LOG_TEXT:**
   - If an entry for this version already exists: update it in place.
   - If it does not exist: insert a new block at the top of LOG_TEXT, below the header line.
   - Color: `#3dba6e` (green) for completed, `#e8920a` (orange) for active/in-progress.
   - For active versions, append `— In Progress` after the date.
   - Format:
     ```
     <b><color=#XXXXXX>vX.XX — Title</color></b>  <color=#7a7a9a>YYYY-MM-DD</color>
     • bullet one
     • bullet two
     ```

5. Do all edits as large single replacements — never make more than one edit per file.
