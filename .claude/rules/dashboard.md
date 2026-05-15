# Dashboard Rules — HackNSLASH Asset Dashboard

## Overview
A local Streamlit-based desktop dashboard for browsing and creating Unity ScriptableObjects (Items, Spells). Opens as a native desktop window (pywebview). Starts via double-click on the desktop shortcut.

**This is a desktop application — all features must work in the pywebview window. Browser-only behaviour is not acceptable.**

## Location
```
dashboard/
├── app.py              # Entry point — tabs + Refresh All button
├── config.py           # Paths, script GUIDs, enums loaded live from C# source
├── cs_parser.py        # Parses enum declarations from .cs files at startup
├── asset_io.py         # Read/write .asset YAML + .meta generation
├── items_tab.py        # Items library table + create form
├── spells_tab.py       # Spells library table + create form
├── launcher.pyw        # Desktop app entry point (pywebview, no console window)
├── dashboard.ico       # Desktop shortcut icon
├── requirements.txt    # streamlit, pyyaml, pandas, pywebview, pythonnet
└── .streamlit/
    └── config.toml     # Dark theme, no usage stats
```

## Running
```bash
cd dashboard
python launcher.pyw         # canonical: desktop window (maximized, dark, port 8501)
```
Double-click **"HackNSLASH Dashboard"** shortcut on the desktop to launch.

> **Debug only:** `streamlit run app.py` opens a raw browser tab — use this only to isolate Streamlit rendering issues, never as a supported launch path.

## Desktop App Rules (Hard)
- The canonical entry point is `launcher.pyw` — always test there, never in a raw browser tab.
- Streamlit runs headless on port 8501; do NOT open a browser or use `--server.headless false`.
- Window starts maximised, min size 900 × 600 — all layouts must be usable at that minimum.
- Never use Streamlit features that rely on browser context: `st.experimental_get_query_params`, external `target="_blank"` links, JS `window.open()`, etc.
- Use `st.button` / `st.link_button` for navigation; never instruct the user to open a browser URL manually.
- On window close, `launcher.pyw` sends SIGINT then force-kills the Streamlit process — do not leave background threads or file locks that survive that.

## How Sync Works
- **Unity → Dashboard**: Dashboard reads `.asset` files from disk on page load. Click "Refresh All" to re-read after changes made in Unity.
- **Dashboard → Unity**: Writing a new asset creates `.asset` + `.asset.meta` directly on disk. Unity auto-detects and imports within seconds — no restart needed.

## ScriptableObject Asset Paths
| Type     | Directory                                      |
|----------|------------------------------------------------|
| Items    | `Assets/ScriptableObjects/Item/`               |
| Spells   | `Assets/ScriptableObjects/Spells/T2/{School}/` |

New spell folders are created automatically with the correct `.meta` if the school doesn't exist yet.

## Script GUIDs (from .cs.meta files — do not change)
```python
ITEM_SCRIPT_GUID  = "9c5f8a9e4be4b6c42a10ba05afe871e9"  # ItemData.cs
SPELL_SCRIPT_GUID = "4378247a87f53c34aa130d1b75a0dc31"  # SpellData.cs
```
These are embedded in every `.asset` file as `m_Script.guid`. If a script is deleted and re-created in Unity, its GUID changes and must be updated in `config.py`.

## Automatic Enum Sync
`config.py` calls `cs_parser.parse_cs_file()` at startup to read all enum values live from:
- `Assets/Scripts/ItemData.cs` → `EquipmentSlot`, `ItemRarity`, `StatType`
- `Assets/Scripts/SpellData.cs` → `SpellSchool`, `SpellType`, `TelegraphShape`, `TelegraphColorMode`, `SpellSpawnOrigin`, `SpellSpawnRotation`

**Adding a new stat or enum value:** just add it to the C# enum — the dashboard picks it up automatically on next launch. No changes to `config.py` needed.

**Exception — color badges:** `RARITY_COLORS` and `SPELL_SCHOOL_COLORS` in `config.py` are hardcoded for display only. New rarity/school values will appear grey until you add a hex color for them there.

## .asset File Format
Unity YAML with a custom `%TAG` directive. Reading strips `%YAML`, `%TAG`, and `--- !u!` lines before passing to PyYAML. Writing uses string templates (never PyYAML dump — it strips the Unity header).

Key quirks:
- Booleans are `0`/`1`, not `true`/`false`
- Colors: `{r: 0.72, g: 0, b: 0, a: 0.54}`
- `yaml.dump()` appends `\n...\n` — always strip it when writing string scalars
- Asset/prefab references (icon, hitEffect, prefab, sounds) are written as `{fileID: 0}` (unassigned) — cannot be set from outside Unity

## Creating a New Asset Type
To add a new ScriptableObject type (e.g. EnemyData) to the dashboard:

1. Add its script GUID to `config.py` from `Assets/Scripts/EnemyData.cs.meta`
2. Add its enums to the `parse_cs_file()` calls in `config.py` (automatic if defined in the same file)
3. Add its directory constant to `config.py`
4. Add `scan_*()` and `write_*_asset()` functions to `asset_io.py` following the Item/Spell pattern
5. Create a new `enemy_tab.py` following `items_tab.py`
6. Add the tab to `app.py`
7. Verify the new tab renders correctly in the desktop window (`launcher.pyw`) — check minimum 900 × 600 layout

## Dependencies
```
pip install -r dashboard/requirements.txt
```
Requires Python 3.x and .NET (for pythonnet/pywebview on Windows). On first install, use `pip install pythonnet --pre` if the stable build fails.
