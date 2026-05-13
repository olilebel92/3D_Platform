# Dashboard Rules — HackNSLASH Asset Dashboard

## Overview
A local Streamlit-based desktop dashboard for browsing and creating Unity ScriptableObjects (Items, Spells). Opens as a native desktop window (pywebview). Starts via double-click on the desktop shortcut.

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
streamlit run app.py        # browser mode
python launcher.pyw         # desktop window mode (maximized, dark)
```
Double-click **"HackNSLASH Dashboard"** shortcut on the desktop to launch.

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

## Dependencies
```
pip install -r dashboard/requirements.txt
```
Requires Python 3.x and .NET (for pythonnet/pywebview on Windows). On first install, use `pip install pythonnet --pre` if the stable build fails.
