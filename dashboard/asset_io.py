import re
import uuid
import yaml
from pathlib import Path

import config


# ─── YAML Helpers ───

def _load_unity_yaml(path: Path) -> dict | None:
    """Parse a Unity .asset file, stripping Unity-specific YAML directives."""
    try:
        text = path.read_text(encoding="utf-8")
    except Exception:
        return None
    lines = [
        l for l in text.splitlines()
        if not l.startswith("%YAML") and not l.startswith("%TAG") and not l.startswith("--- !u!")
    ]
    try:
        doc = yaml.safe_load("\n".join(lines))
    except yaml.YAMLError:
        return None
    if not isinstance(doc, dict):
        return None
    return doc.get("MonoBehaviour")


def _yaml_str(s: str) -> str:
    """Return a YAML-safe scalar string (handles colons, braces, newlines)."""
    dumped = yaml.dump(s, default_flow_style=True, allow_unicode=True)
    # yaml.dump appends '\n...\n' document-end marker — strip it
    return dumped.replace("\n...\n", "").replace("...", "").strip()


def _new_guid() -> str:
    return uuid.uuid4().hex


# ─── Meta File ───

_META_TEMPLATE = """\
fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
"""

_FOLDER_META_TEMPLATE = """\
fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def _write_meta(asset_path: Path, guid: str, folder: bool = False) -> None:
    template = _FOLDER_META_TEMPLATE if folder else _META_TEMPLATE
    meta_path = Path(str(asset_path) + ".meta")
    meta_path.write_text(template.format(guid=guid), encoding="utf-8")


def _read_meta_guid(asset_path: Path) -> str | None:
    """Read the `guid:` field from `<asset>.meta`. Returns None if missing/malformed."""
    meta_path = Path(str(asset_path) + ".meta")
    try:
        text = meta_path.read_text(encoding="utf-8")
    except (FileNotFoundError, OSError):
        return None
    m = re.search(r"^guid:\s*([0-9a-fA-F]+)\s*$", text, flags=re.MULTILINE)
    return m.group(1) if m else None


def _make_so_ref(guid: str) -> str:
    """Format a Unity SO reference inline value. Empty guid → unassigned `{fileID: 0}`."""
    if guid:
        return f"{{fileID: 11400000, guid: {guid}, type: 2}}"
    return "{fileID: 0}"


def _ensure_dir_with_meta(directory: Path) -> None:
    """Create a directory and its Unity .meta if it didn't exist."""
    if not directory.exists():
        directory.mkdir(parents=True, exist_ok=True)
        meta_path = Path(str(directory) + ".meta")
        if not meta_path.exists():
            _write_meta(directory, _new_guid(), folder=True)


# ─── Asset Name Sanitization ───

def sanitize_asset_name(name: str) -> str:
    name = name.strip().replace(" ", "_")
    name = re.sub(r"[^A-Za-z0-9_\-]", "", name)
    return name


# ─── In-Place YAML Field Update ──────────────────────────────────────────────
# Edit replaces only the lines the dashboard manages. Reference fields like
# `icon: {fileID: 12345, guid: ..., type: 3}` set in Unity are NOT in the
# updates dict, so they remain untouched. Same for the .meta GUID — we never
# rewrite the .meta on edit, so Unity references survive across saves.

def _update_scalar_fields(text: str, updates: dict[str, str]) -> str:
    """
    Replace each `  key: <existing>` line with `  key: <new>`. Single occurrence per key.

    Uses [ \\t] (horizontal whitespace only) — not \\s — between the colon and end-of-line
    so the match cannot span newlines. Without this, an empty scalar line (e.g.
    `attachBone:` with no value) would greedily eat into the next line and delete it.
    """
    for key, value in updates.items():
        pattern = rf"^(?P<indent>[ \t]+){re.escape(key)}:[ \t]*[^\n\r]*$"
        replacement = rf"\g<indent>{key}: {value}"
        text, n = re.subn(pattern, replacement, text, count=1, flags=re.MULTILINE)
        if n == 0:
            # Field missing from the existing asset (e.g. older format) — append it.
            # Insert before any trailing newline so we don't break the file ending.
            stripped = text.rstrip("\r\n")
            text = stripped + f"\n  {key}: {value}\n"
    return text


def _replace_block_to_eof(text: str, key: str, replacement_block: str) -> str:
    """
    Replace `  key:` and every line that follows it (to EOF) with the given block.
    Used for list fields like statLines that span multiple lines and live at end of file.
    """
    pattern = rf"^(\s+){re.escape(key)}:[\s\S]*\Z"
    return re.sub(pattern, replacement_block.rstrip(), text, count=1, flags=re.MULTILINE)


def _replace_yaml_list_block(text: str, key: str, values: list, insert_after: str | None = None) -> str:
    """
    Replace a YAML block list `  key:\\n  - v1\\n  - v2` with `values`.
    If key is missing, insert the new block immediately after `insert_after` line (or append at end).
    """
    lines = text.splitlines(keepends=True)
    out: list[str] = []
    i = 0
    found = False
    key_pattern = re.compile(rf"^(\s+){re.escape(key)}:\s*$")
    item_pattern = re.compile(r"^\s+-\s")
    while i < len(lines):
        line = lines[i]
        m = key_pattern.match(line)
        if m and not found:
            found = True
            indent = m.group(1)
            out.append(f"{indent}{key}:\n")
            for v in values:
                out.append(f"{indent}- {v}\n")
            i += 1
            while i < len(lines) and item_pattern.match(lines[i]):
                i += 1
        else:
            out.append(line)
            i += 1
    if found:
        return "".join(out)
    # Missing: insert after the entire `insert_after` BLOCK (anchor header + any `- ` items
    # beneath it). Inserting just after the anchor line would split the anchor's own list.
    if insert_after:
        anchor_pattern = re.compile(rf"^(\s+){re.escape(insert_after)}:")
        out2: list[str] = []
        inserted = False
        i2 = 0
        while i2 < len(out):
            line = out[i2]
            out2.append(line)
            i2 += 1
            if not inserted:
                am = anchor_pattern.match(line)
                if am:
                    indent = am.group(1)
                    # Copy any `- ` items that belong to the anchor's block first.
                    while i2 < len(out) and item_pattern.match(out[i2]):
                        out2.append(out[i2])
                        i2 += 1
                    out2.append(f"{indent}{key}:\n")
                    for v in values:
                        out2.append(f"{indent}- {v}\n")
                    inserted = True
        if inserted:
            return "".join(out2)
    # Fallback: append at end
    text_str = "".join(out).rstrip("\r\n")
    block = f"\n  {key}:\n" + "".join(f"  - {v}\n" for v in values)
    return text_str + block


def _remove_yaml_line(text: str, key: str) -> str:
    """Remove any single `  key: ...` scalar line from the YAML body."""
    pattern = rf"^\s+{re.escape(key)}:\s*[^\n\r]*\r?\n"
    return re.sub(pattern, "", text, count=1, flags=re.MULTILINE)


def _remove_yaml_inline_line(text: str, key: str) -> str:
    """
    Remove only `  key: <value>` lines where there IS a value after the colon
    (e.g. inline `key: 0b000000` left by Unity). Block-style headers like
    bare `  key:` are NOT removed — those belong to a list block.
    Call this before inserting a list block to clean up stale inline forms
    Unity may have written.

    Uses [ \\t] (horizontal whitespace only) — not \\s — so the match cannot
    span newlines and eat the following line by accident.
    """
    pattern = rf"^[ \t]+{re.escape(key)}:[ \t]+\S[^\n\r]*\r?\n"
    return re.sub(pattern, "", text, count=0, flags=re.MULTILINE)


# ─── C# Enum Editing ─────────────────────────────────────────────────────────
# Append a new value to a public enum block in a .cs file. Append-only by design:
# enum values are positionally mapped to integers in Unity .asset YAML, so
# deleting or reordering would silently break every existing asset that
# references the enum. The dashboard refuses to add a value that already exists.

def append_enum_value(cs_path: Path, enum_name: str, new_value: str) -> tuple[bool, str]:
    """Append `new_value` to the body of `enum_name` in cs_path. Returns (ok, message)."""
    if not re.match(r"^[A-Za-z_][A-Za-z0-9_]*$", new_value):
        return False, f"'{new_value}' is not a valid C# identifier."
    try:
        text = cs_path.read_text(encoding="utf-8")
    except FileNotFoundError:
        return False, f"File not found: {cs_path}"

    pattern = re.compile(
        rf"(\benum\s+{re.escape(enum_name)}\s*\{{)([^}}]*)(\}})",
        flags=re.DOTALL,
    )
    m = pattern.search(text)
    if not m:
        return False, f"Enum '{enum_name}' not found in {cs_path.name}."

    body = m.group(2)
    existing = [v.strip().split("=")[0].strip() for v in body.split(",") if v.strip()]
    if new_value in existing:
        return False, f"'{new_value}' already exists in {enum_name}."

    # Preserve the body's whitespace style: if single-line, append with ", "; otherwise add a new comma-separated entry.
    body_stripped = body.strip()
    if "\n" in body_stripped:
        # Multi-line enum body — match indentation of existing entries.
        indent_match = re.search(r"^(\s+)\S", body, flags=re.MULTILINE)
        indent = indent_match.group(1) if indent_match else "    "
        new_body = body.rstrip()
        if not new_body.endswith(","):
            new_body += ","
        new_body += f"\n{indent}{new_value}\n"
    else:
        new_body = f" {body_stripped}, {new_value} "

    new_text = text[: m.start(2)] + new_body + text[m.end(2):]
    cs_path.write_text(new_text, encoding="utf-8")
    return True, f"Added '{new_value}' to {enum_name}."


# ─── Read Items ───

def scan_items() -> list[dict]:
    """Return list of parsed ItemData dicts from the Items directory."""
    rarity_by_guid  = {r["guid"]: r["displayName"] for r in scan_rarities() if r.get("guid")}
    subtype_by_guid = {s["guid"]: s for s in scan_sub_types() if s.get("guid")}

    items = []
    for path in sorted(config.ITEMS_DIR.glob("*.asset")):
        mb = _load_unity_yaml(path)
        if not mb:
            continue
        script = mb.get("m_Script", {})
        if script.get("guid") != config.ITEM_SCRIPT_GUID:
            continue

        stat_lines = []
        for sl in (mb.get("statLines") or []):
            stat_lines.append({
                "type":  config.STAT_TYPE.get(sl.get("type", 0), "STR"),
                "value": float(sl.get("value", 0)),
            })

        rarity_ref  = mb.get("rarity")  or {}
        subtype_ref = mb.get("subType") or {}
        rarity_guid  = rarity_ref.get("guid")  if isinstance(rarity_ref,  dict) else None
        subtype_guid = subtype_ref.get("guid") if isinstance(subtype_ref, dict) else None
        subtype      = subtype_by_guid.get(subtype_guid) if subtype_guid else None

        items.append({
            "asset_file":   path.stem,
            "itemName":     mb.get("itemName", ""),
            "description":  mb.get("description", ""),
            "slot":         subtype["equipSlot"]   if subtype else "—",
            "subType_name": subtype["displayName"] if subtype else "—",
            "subType_guid": subtype_guid or "",
            "rarity":       rarity_by_guid.get(rarity_guid, "—") if rarity_guid else "—",
            "rarity_guid":  rarity_guid or "",
            "statLines":    stat_lines,
        })
    return items


# ─── Write Items ───

_ITEM_ASSET_TEMPLATE = """\
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {script_guid}, type: 3}}
  m_Name: {asset_name}
  m_EditorClassIdentifier: {class_id}
  itemName: {item_name}
  description: {description}
  icon: {{fileID: 0}}
  subType: {subtype_ref}
  rarity: {rarity_ref}
  assetGuid:
  statLines:
{stat_lines}"""


def write_item_asset(data: dict) -> tuple[bool, str]:
    """Write a new ItemData .asset + .meta. Returns (success, message)."""
    asset_name = sanitize_asset_name(data["asset_name"])
    if not asset_name:
        return False, "Asset name is empty after sanitization."

    dest = config.ITEMS_DIR / f"{asset_name}.asset"
    if dest.exists():
        return False, f"Asset '{asset_name}.asset' already exists."

    stat_lines_yaml = ""
    for sl in data.get("stat_lines", []):
        type_int = config.STAT_TYPE_INV.get(sl["type"], 0)
        stat_lines_yaml += f"  - type: {type_int}\n    value: {sl['value']}\n"
    if not stat_lines_yaml:
        stat_lines_yaml = "  []\n"

    content = _ITEM_ASSET_TEMPLATE.format(
        script_guid=config.ITEM_SCRIPT_GUID,
        asset_name=asset_name,
        class_id=config.ITEM_CLASS_ID,
        item_name=_yaml_str(data.get("item_name", asset_name)),
        description=_yaml_str(data.get("description", "")),
        subtype_ref=_make_so_ref(data.get("subtype_guid", "")),
        rarity_ref=_make_so_ref(data.get("rarity_guid", "")),
        stat_lines=stat_lines_yaml,
    )

    _ensure_dir_with_meta(config.ITEMS_DIR)
    dest.write_text(content, encoding="utf-8")
    _write_meta(dest, _new_guid())
    return True, f"Created '{asset_name}.asset' in Items."


def update_item_asset(asset_file: str, data: dict) -> tuple[bool, str]:
    """Edit an existing ItemData .asset in place. Preserves icon refs and .meta GUID."""
    path = config.ITEMS_DIR / f"{asset_file}.asset"
    if not path.exists():
        return False, f"Asset '{asset_file}.asset' not found."

    text = path.read_text(encoding="utf-8")

    updates = {
        "itemName":    _yaml_str(data.get("item_name", asset_file)),
        "description": _yaml_str(data.get("description", "")),
        "subType":     _make_so_ref(data.get("subtype_guid", "")),
        "rarity":      _make_so_ref(data.get("rarity_guid", "")),
    }
    text = _update_scalar_fields(text, updates)

    # statLines is a list at end of file — replace the whole block.
    stat_lines_yaml = "  statLines:\n"
    rendered = ""
    for sl in data.get("stat_lines", []):
        type_int = config.STAT_TYPE_INV.get(sl["type"], 0)
        rendered += f"  - type: {type_int}\n    value: {sl['value']}\n"
    if rendered:
        stat_lines_yaml += rendered
    else:
        stat_lines_yaml += "  []\n"
    text = _replace_block_to_eof(text, "statLines", stat_lines_yaml)

    path.write_text(text, encoding="utf-8")
    return True, f"Updated '{asset_file}.asset'."


# ─── Read Rarities ───

def scan_rarities() -> list[dict]:
    """Return list of parsed RarityData dicts from the Rarities directory, sorted by sortOrder."""
    rarities = []
    for path in sorted(config.RARITIES_DIR.glob("*.asset")):
        mb = _load_unity_yaml(path)
        if not mb:
            continue
        script = mb.get("m_Script", {})
        if script.get("guid") != config.RARITY_SCRIPT_GUID:
            continue

        def gf(key, default=0.0):
            v = mb.get(key, default)
            return float(v) if v is not None else default

        def gi(key, default=0):
            v = mb.get(key, default)
            return int(v) if v is not None else default

        color = mb.get("color") or {}
        glow  = mb.get("glowColor") or {}

        raw_banned = mb.get("bannedStats")
        if isinstance(raw_banned, list):
            banned_labels = [config.STAT_TYPE.get(int(v), "STR") for v in raw_banned]
        else:
            banned_labels = []

        rarities.append({
            "asset_file":          path.stem,
            "guid":                _read_meta_guid(path),
            "displayName":         mb.get("displayName", path.stem),
            "sortOrder":           gi("sortOrder", 0),
            "color":               {"r": float(color.get("r", 1.0)), "g": float(color.get("g", 1.0)),
                                    "b": float(color.get("b", 1.0)), "a": float(color.get("a", 1.0))},
            "glowColor":           {"r": float(glow.get("r", 1.0)),  "g": float(glow.get("g", 1.0)),
                                    "b": float(glow.get("b", 1.0)),  "a": float(glow.get("a", 1.0))},
            "glowIntensity":       gf("glowIntensity", 1.0),
            "dropWeight":          gf("dropWeight", 10.0),
            "waveUnlockThreshold": gi("waveUnlockThreshold", 1),
            "statLineCountMin":    gi("statLineCountMin", 1),
            "statLineCountMax":    gi("statLineCountMax", 2),
            "statValueMultiplier": gf("statValueMultiplier", 1.0),
            "bannedStats":         banned_labels,
        })
    return sorted(rarities, key=lambda r: r["sortOrder"])


# ─── Write Rarities ───

_RARITY_ASSET_TEMPLATE = """\
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {script_guid}, type: 3}}
  m_Name: {asset_name}
  m_EditorClassIdentifier: {class_id}
  displayName: {display_name}
  sortOrder: {sort_order}
  color: {{r: {cr}, g: {cg}, b: {cb}, a: {ca}}}
  glowColor: {{r: {gr}, g: {gg}, b: {gb}, a: {ga}}}
  glowIntensity: {glow_intensity}
  particlePrefab: {{fileID: 0}}
  dropWeight: {drop_weight}
  waveUnlockThreshold: {wave_unlock}
  statLineCountMin: {stat_min}
  statLineCountMax: {stat_max}
  statValueMultiplier: {stat_mult}
  bannedStats:{banned_stats_block}
"""


def write_rarity_asset(data: dict) -> tuple[bool, str]:
    """Write a new RarityData .asset + .meta. Returns (success, message)."""
    asset_name = sanitize_asset_name(data["asset_name"])
    if not asset_name:
        return False, "Asset name is empty after sanitization."

    _ensure_dir_with_meta(config.RARITIES_DIR)
    dest = config.RARITIES_DIR / f"{asset_name}.asset"
    if dest.exists():
        return False, f"Asset '{asset_name}.asset' already exists."

    def f4(v): return round(float(v), 6)

    color = data.get("color", {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0})
    glow  = data.get("glow_color", color)

    banned_labels = data.get("banned_stats") or []
    banned_ints = [config.STAT_TYPE_INV.get(label, 0) for label in banned_labels]
    banned_block = ("\n" + "\n".join(f"  - {i}" for i in banned_ints)) if banned_ints else " []"

    content = _RARITY_ASSET_TEMPLATE.format(
        script_guid=config.RARITY_SCRIPT_GUID,
        asset_name=asset_name,
        class_id=config.RARITY_CLASS_ID,
        display_name=_yaml_str(data.get("display_name", asset_name)),
        sort_order=int(data.get("sort_order", 0)),
        cr=f4(color.get("r", 1.0)), cg=f4(color.get("g", 1.0)),
        cb=f4(color.get("b", 1.0)), ca=f4(color.get("a", 1.0)),
        gr=f4(glow.get("r", 1.0)),  gg=f4(glow.get("g", 1.0)),
        gb=f4(glow.get("b", 1.0)),  ga=f4(glow.get("a", 1.0)),
        glow_intensity=f4(data.get("glow_intensity", 1.0)),
        drop_weight=f4(data.get("drop_weight", 10.0)),
        wave_unlock=int(data.get("wave_unlock", 1)),
        stat_min=int(data.get("stat_min", 1)),
        stat_max=int(data.get("stat_max", 2)),
        stat_mult=f4(data.get("stat_mult", 1.0)),
        banned_stats_block=banned_block,
    )

    dest.write_text(content, encoding="utf-8")
    _write_meta(dest, _new_guid())
    return True, f"Created '{asset_name}.asset' in Rarities/."


def update_rarity_asset(asset_file: str, data: dict) -> tuple[bool, str]:
    """Edit an existing RarityData .asset in place. Preserves particlePrefab ref and .meta GUID."""
    path = config.RARITIES_DIR / f"{asset_file}.asset"
    if not path.exists():
        return False, f"Asset '{asset_file}.asset' not found."

    text = path.read_text(encoding="utf-8")

    def f4(v): return f"{round(float(v), 6)}"

    color = data.get("color", {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0})
    glow  = data.get("glow_color", color)
    color_inline = f"{{r: {f4(color.get('r', 1.0))}, g: {f4(color.get('g', 1.0))}, b: {f4(color.get('b', 1.0))}, a: {f4(color.get('a', 1.0))}}}"
    glow_inline  = f"{{r: {f4(glow.get('r', 1.0))}, g: {f4(glow.get('g', 1.0))}, b: {f4(glow.get('b', 1.0))}, a: {f4(glow.get('a', 1.0))}}}"

    updates = {
        "displayName":          _yaml_str(data.get("display_name", asset_file)),
        "sortOrder":            str(int(data.get("sort_order", 0))),
        "color":                color_inline,
        "glowColor":            glow_inline,
        "glowIntensity":        f4(data.get("glow_intensity", 1.0)),
        "dropWeight":           f4(data.get("drop_weight", 10.0)),
        "waveUnlockThreshold":  str(int(data.get("wave_unlock", 1))),
        "statLineCountMin":     str(int(data.get("stat_min", 1))),
        "statLineCountMax":     str(int(data.get("stat_max", 2))),
        "statValueMultiplier":  f4(data.get("stat_mult", 1.0)),
    }
    text = _update_scalar_fields(text, updates)

    banned_labels = data.get("banned_stats") or []
    banned_ints = [config.STAT_TYPE_INV.get(label, 0) for label in banned_labels]
    text = _remove_yaml_inline_line(text, "bannedStats")
    text = _replace_yaml_list_block(text, "bannedStats", banned_ints, insert_after="statValueMultiplier")

    path.write_text(text, encoding="utf-8")
    return True, f"Updated '{asset_file}.asset'."


# ─── Read MainTypes ───

def scan_main_types() -> list[dict]:
    """Return list of parsed MainTypeData dicts from the MainTypes directory."""
    items = []
    for path in sorted(config.MAINTYPES_DIR.glob("*.asset")):
        mb = _load_unity_yaml(path)
        if not mb:
            continue
        if mb.get("m_Script", {}).get("guid") != config.MAINTYPE_SCRIPT_GUID:
            continue
        items.append({
            "asset_file":  path.stem,
            "displayName": mb.get("displayName", path.stem),
            "isWeapon":    bool(int(mb.get("isWeapon", 0) or 0)),
            "guid":        _read_meta_guid(path),
        })
    return items


# ─── Write MainTypes ───

_MAINTYPE_ASSET_TEMPLATE = """\
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {script_guid}, type: 3}}
  m_Name: {asset_name}
  m_EditorClassIdentifier: {class_id}
  displayName: {display_name}
  icon: {{fileID: 0}}
  isWeapon: {is_weapon}
"""


def write_main_type_asset(data: dict) -> tuple[bool, str]:
    """Write a new MainTypeData .asset + .meta. Returns (success, message)."""
    asset_name = sanitize_asset_name(data["asset_name"])
    if not asset_name:
        return False, "Asset name is empty after sanitization."

    _ensure_dir_with_meta(config.MAINTYPES_DIR)
    dest = config.MAINTYPES_DIR / f"{asset_name}.asset"
    if dest.exists():
        return False, f"Asset '{asset_name}.asset' already exists."

    content = _MAINTYPE_ASSET_TEMPLATE.format(
        script_guid=config.MAINTYPE_SCRIPT_GUID,
        asset_name=asset_name,
        class_id=config.MAINTYPE_CLASS_ID,
        display_name=_yaml_str(data.get("display_name", asset_name)),
        is_weapon=1 if data.get("is_weapon") else 0,
    )
    dest.write_text(content, encoding="utf-8")
    _write_meta(dest, _new_guid())
    return True, f"Created '{asset_name}.asset' in MainTypes/."


def update_main_type_asset(asset_file: str, data: dict) -> tuple[bool, str]:
    """Edit an existing MainTypeData .asset in place. Preserves icon ref and .meta GUID."""
    path = config.MAINTYPES_DIR / f"{asset_file}.asset"
    if not path.exists():
        return False, f"Asset '{asset_file}.asset' not found."

    text = path.read_text(encoding="utf-8")
    updates = {
        "displayName": _yaml_str(data.get("display_name", asset_file)),
        "isWeapon":    "1" if data.get("is_weapon") else "0",
    }
    text = _update_scalar_fields(text, updates)
    path.write_text(text, encoding="utf-8")
    return True, f"Updated '{asset_file}.asset'."


# ─── Read SubTypes ───

def scan_sub_types() -> list[dict]:
    """Return list of parsed SubTypeData dicts from the SubTypes directory."""
    # Build a guid -> MainType displayName map so we can resolve mainType refs.
    main_by_guid = {mt["guid"]: mt["displayName"] for mt in scan_main_types() if mt["guid"]}

    items = []
    for path in sorted(config.SUBTYPES_DIR.glob("*.asset")):
        mb = _load_unity_yaml(path)
        if not mb:
            continue
        if mb.get("m_Script", {}).get("guid") != config.SUBTYPE_SCRIPT_GUID:
            continue

        mt_ref = mb.get("mainType") or {}
        mt_guid = mt_ref.get("guid") if isinstance(mt_ref, dict) else None
        mt_name = main_by_guid.get(mt_guid, "(missing)")

        def _stats(key: str) -> list[str]:
            raw = mb.get(key)
            if isinstance(raw, list):
                return [config.STAT_TYPE.get(int(v), "STR") for v in raw]
            return []

        allowed_labels  = _stats("allowedStats")
        reserved_labels = _stats("reservedStats")

        materials = mb.get("nameMaterials")
        materials = list(materials) if isinstance(materials, list) else []

        items.append({
            "asset_file":     path.stem,
            "guid":           _read_meta_guid(path),
            "displayName":    mb.get("displayName", path.stem),
            "mainTypeName":   mt_name,
            "mainTypeGuid":   mt_guid,
            "equipSlot":      config.EQUIPMENT_SLOT.get(int(mb.get("equipSlot", 0) or 0), "Boots"),
            "attachBone":     mb.get("attachBone", "") or "",
            "allowedStats":   allowed_labels,
            "reservedStats":  reserved_labels,
            "nameMaterials":  materials,
        })
    return items


# ─── Write SubTypes ───

_SUBTYPE_ASSET_TEMPLATE = """\
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {script_guid}, type: 3}}
  m_Name: {asset_name}
  m_EditorClassIdentifier: {class_id}
  displayName: {display_name}
  mainType: {{fileID: 11400000, guid: {main_type_guid}, type: 2}}
  equipSlot: {equip_slot}
  attachBone: {attach_bone}
  worldModelPrefab: {{fileID: 0}}
  defaultIcon: {{fileID: 0}}
  iconPool: []
  allowedStats:{allowed_stats_block}
  reservedStats:{reserved_stats_block}
  nameMaterials:{name_materials_block}
"""


def _stat_list_block(labels: list[str]) -> str:
    """Format a stat-int list as a YAML block. Empty → ' []' so the line stays on the parent."""
    ints = [config.STAT_TYPE_INV.get(label, 0) for label in (labels or [])]
    return ("\n" + "\n".join(f"  - {i}" for i in ints)) if ints else " []"


def write_sub_type_asset(data: dict) -> tuple[bool, str]:
    """Write a new SubTypeData .asset + .meta. Requires a valid MainType GUID."""
    asset_name = sanitize_asset_name(data["asset_name"])
    if not asset_name:
        return False, "Asset name is empty after sanitization."

    main_type_guid = (data.get("main_type_guid") or "").strip()
    if not main_type_guid:
        return False, "MainType is required — create a MainType first."

    _ensure_dir_with_meta(config.SUBTYPES_DIR)
    dest = config.SUBTYPES_DIR / f"{asset_name}.asset"
    if dest.exists():
        return False, f"Asset '{asset_name}.asset' already exists."

    allowed_block  = _stat_list_block(data.get("allowed_stats"))
    reserved_block = _stat_list_block(data.get("reserved_stats"))

    materials = [m.strip() for m in (data.get("name_materials") or []) if m and m.strip()]
    if materials:
        materials_block = "\n" + "\n".join(f"  - {_yaml_str(m)}" for m in materials)
    else:
        materials_block = " []"

    attach_bone = data.get("attach_bone", "") or ""
    attach_bone_yaml = _yaml_str(attach_bone) if attach_bone else ""

    content = _SUBTYPE_ASSET_TEMPLATE.format(
        script_guid=config.SUBTYPE_SCRIPT_GUID,
        asset_name=asset_name,
        class_id=config.SUBTYPE_CLASS_ID,
        display_name=_yaml_str(data.get("display_name", asset_name)),
        main_type_guid=main_type_guid,
        equip_slot=config.EQUIPMENT_SLOT_INV.get(data.get("equip_slot", "Boots"), 0),
        attach_bone=attach_bone_yaml,
        allowed_stats_block=allowed_block,
        reserved_stats_block=reserved_block,
        name_materials_block=materials_block,
    )
    dest.write_text(content, encoding="utf-8")
    _write_meta(dest, _new_guid())
    return True, f"Created '{asset_name}.asset' in SubTypes/."


def update_sub_type_asset(asset_file: str, data: dict) -> tuple[bool, str]:
    """
    Edit an existing SubTypeData .asset in place. Preserves worldModelPrefab/defaultIcon/iconPool
    refs and the .meta GUID. mainType can be reassigned by passing a new GUID.
    """
    path = config.SUBTYPES_DIR / f"{asset_file}.asset"
    if not path.exists():
        return False, f"Asset '{asset_file}.asset' not found."

    main_type_guid = (data.get("main_type_guid") or "").strip()
    if not main_type_guid:
        return False, "MainType is required."

    text = path.read_text(encoding="utf-8")

    attach_bone = data.get("attach_bone", "") or ""
    attach_bone_yaml = _yaml_str(attach_bone) if attach_bone else ""

    updates = {
        "displayName": _yaml_str(data.get("display_name", asset_file)),
        "mainType":    f"{{fileID: 11400000, guid: {main_type_guid}, type: 2}}",
        "equipSlot":   str(config.EQUIPMENT_SLOT_INV.get(data.get("equip_slot", "Boots"), 0)),
        "attachBone":  attach_bone_yaml,
    }
    text = _update_scalar_fields(text, updates)

    allowed_labels  = data.get("allowed_stats") or []
    reserved_labels = data.get("reserved_stats") or []
    allowed_ints  = [config.STAT_TYPE_INV.get(label, 0) for label in allowed_labels]
    reserved_ints = [config.STAT_TYPE_INV.get(label, 0) for label in reserved_labels]
    # Strip any inline-form leftovers (e.g. Unity-written `reservedStats: 0b000000`) so the
    # block insert is the only copy in the file.
    text = _remove_yaml_inline_line(text, "allowedStats")
    text = _remove_yaml_inline_line(text, "reservedStats")
    text = _remove_yaml_inline_line(text, "nameMaterials")
    text = _replace_yaml_list_block(text, "allowedStats",  allowed_ints,  insert_after="iconPool")
    text = _replace_yaml_list_block(text, "reservedStats", reserved_ints, insert_after="allowedStats")

    materials = [m.strip() for m in (data.get("name_materials") or []) if m and m.strip()]
    materials_yaml = [_yaml_str(m) for m in materials]
    text = _replace_yaml_list_block(text, "nameMaterials", materials_yaml, insert_after="reservedStats")

    path.write_text(text, encoding="utf-8")
    return True, f"Updated '{asset_file}.asset'."


# ─── Read Spells ───

def scan_spells() -> list[dict]:
    """Return list of parsed SpellData dicts from the Spells directory."""
    spells = []
    for path in sorted(config.SPELLS_DIR.rglob("*.asset")):
        mb = _load_unity_yaml(path)
        if not mb:
            continue
        script = mb.get("m_Script", {})
        if script.get("guid") != config.SPELL_SCRIPT_GUID:
            continue

        def gf(key, default=0.0):
            v = mb.get(key, default)
            return float(v) if v is not None else default

        def gi(key, default=0):
            v = mb.get(key, default)
            return int(v) if v is not None else default

        def gb(key):
            return bool(gi(key))

        color = mb.get("telegraphColor") or {}
        if isinstance(color, dict):
            r = color.get("r", 1.0); g = color.get("g", 1.0)
            b = color.get("b", 1.0); a = color.get("a", 0.45)
        else:
            r = g = b = 1.0; a = 0.45

        spells.append({
            "asset_file":  path.stem,
            "spellName":   mb.get("spellName", ""),
            "description": mb.get("description", ""),
            "school":  config.SPELL_SCHOOL.get(gi("school"), "Arcane"),
            "spellType": config.SPELL_TYPE.get(gi("spellType"), "Cast"),
            "baseDamage":         gf("baseDamage"),
            "damagePerSkillRank": gf("damagePerSkillRank"),
            "chainCountPerRank":  gi("chainCountPerRank"),
            "cooldown":           gf("cooldown"),
            "castStartDelay":     gf("castStartDelay"),
            "castTime":           gf("castTime"),
            "throwAnimLeadTime":  gf("throwAnimLeadTime"),
            "lockMovementDuringCast": gb("lockMovementDuringCast"),
            "movementInterruptGrace": gf("movementInterruptGrace"),
            "damageInterruptGrace":   gf("damageInterruptGrace"),
            "channelTickRate":    gf("channelTickRate"),
            "fireOnChannelStart": gb("fireOnChannelStart"),
            "lockMovementDuringChannel": gb("lockMovementDuringChannel"),
            "spawnOrigin":   config.SPAWN_ORIGIN.get(gi("spawnOrigin"), "FirePoint"),
            "spawnRotation": config.SPAWN_ROTATION.get(gi("spawnRotation"), "CameraAim"),
            "projectileCount": gi("projectileCount", 1),
            "spreadAngle":     gf("spreadAngle"),
            "telegraphShape": config.TELEGRAPH_SHAPE.get(gi("telegraphShape"), "None"),
            "telegraphRadius": gf("telegraphRadius"),
            "telegraphAngle":  gf("telegraphAngle"),
            "telegraphLength": gf("telegraphLength"),
            "telegraphWidth":  gf("telegraphWidth"),
            "telegraphColorMode": config.COLOR_MODE.get(gi("telegraphColorMode"), "Auto"),
            "telegraphColor": {"r": r, "g": g, "b": b, "a": a},
            "telegraphFollowsCursor": gb("telegraphFollowsCursor"),
            "telegraphOriginOffset":  gf("telegraphOriginOffset"),
            "castRange":         gf("castRange"),
            "chainCount":        gi("chainCount"),
            "chainRadius":       gf("chainRadius"),
            "chainDamageFalloff": gf("chainDamageFalloff", 0.6),
            "chainTravelTime":   gf("chainTravelTime"),
            "chainJumpDelay":    gf("chainJumpDelay"),
        })
    return spells


# ─── Write Spells ───

_SPELL_ASSET_TEMPLATE = """\
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {script_guid}, type: 3}}
  m_Name: {asset_name}
  m_EditorClassIdentifier: {class_id}
  school: {school}
  baseDamage: {baseDamage}
  damagePerSkillRank: {damagePerSkillRank}
  chainCountPerRank: {chainCountPerRank}
  cooldown: {cooldown}
  manaCost: {manaCost}
  spellName: {spellName}
  description: {description}
  icon: {{fileID: 0}}
  hitEffect: {{fileID: 0}}
  prefab: {{fileID: 0}}
  spellType: {spellType}
  castStartDelay: {castStartDelay}
  castTime: {castTime}
  throwAnimLeadTime: {throwAnimLeadTime}
  lockMovementDuringCast: {lockMovementDuringCast}
  movementInterruptGrace: {movementInterruptGrace}
  damageInterruptGrace: {damageInterruptGrace}
  channelTickRate: {channelTickRate}
  fireOnChannelStart: {fireOnChannelStart}
  lockMovementDuringChannel: {lockMovementDuringChannel}
  castSound: {{fileID: 0}}
  hitSound: {{fileID: 0}}
  spawnOrigin: {spawnOrigin}
  spawnRotation: {spawnRotation}
  projectileCount: {projectileCount}
  spreadAngle: {spreadAngle}
  telegraphShape: {telegraphShape}
  telegraphRadius: {telegraphRadius}
  telegraphAngle: {telegraphAngle}
  telegraphLength: {telegraphLength}
  telegraphWidth: {telegraphWidth}
  telegraphColorMode: {telegraphColorMode}
  telegraphColor: {{r: {tc_r}, g: {tc_g}, b: {tc_b}, a: {tc_a}}}
  telegraphFollowsCursor: {telegraphFollowsCursor}
  telegraphOriginOffset: {telegraphOriginOffset}
  castRange: {castRange}
  chainCount: {chainCount}
  chainRadius: {chainRadius}
  chainDamageFalloff: {chainDamageFalloff}
  chainTravelTime: {chainTravelTime}
  chainJumpDelay: {chainJumpDelay}"""


def write_spell_asset(data: dict) -> tuple[bool, str]:
    """Write a new SpellData .asset + .meta. Returns (success, message)."""
    asset_name = sanitize_asset_name(data["asset_name"])
    if not asset_name:
        return False, "Asset name is empty after sanitization."

    school_label = data.get("school", "Arcane")
    school_dir = config.SPELLS_DIR / "T2" / school_label
    _ensure_dir_with_meta(config.SPELLS_DIR / "T2")
    _ensure_dir_with_meta(school_dir)

    dest = school_dir / f"{asset_name}.asset"
    if dest.exists():
        return False, f"Asset '{asset_name}.asset' already exists."

    color = data.get("telegraphColor", {"r": 1.0, "g": 1.0, "b": 1.0, "a": 0.45})

    def b(val): return 1 if val else 0
    def f4(val): return round(float(val), 6)

    content = _SPELL_ASSET_TEMPLATE.format(
        script_guid=config.SPELL_SCRIPT_GUID,
        asset_name=asset_name,
        class_id=config.SPELL_CLASS_ID,
        school=config.SPELL_SCHOOL_INV.get(school_label, 0),
        spellName=_yaml_str(data.get("spellName", asset_name)),
        description=_yaml_str(data.get("description", "")),
        spellType=config.SPELL_TYPE_INV.get(data.get("spellType", "Cast"), 0),
        baseDamage=f4(data.get("baseDamage", 0)),
        damagePerSkillRank=f4(data.get("damagePerSkillRank", 0)),
        chainCountPerRank=int(data.get("chainCountPerRank", 0)),
        cooldown=f4(data.get("cooldown", 1.0)),
        manaCost=f4(data.get("manaCost", 0)),
        castStartDelay=f4(data.get("castStartDelay", 0)),
        castTime=f4(data.get("castTime", 0)),
        throwAnimLeadTime=f4(data.get("throwAnimLeadTime", 0)),
        lockMovementDuringCast=b(data.get("lockMovementDuringCast", False)),
        movementInterruptGrace=f4(data.get("movementInterruptGrace", 0)),
        damageInterruptGrace=f4(data.get("damageInterruptGrace", 0)),
        channelTickRate=f4(data.get("channelTickRate", 0.5)),
        fireOnChannelStart=b(data.get("fireOnChannelStart", False)),
        lockMovementDuringChannel=b(data.get("lockMovementDuringChannel", False)),
        spawnOrigin=config.SPAWN_ORIGIN_INV.get(data.get("spawnOrigin", "FirePoint"), 0),
        spawnRotation=config.SPAWN_ROTATION_INV.get(data.get("spawnRotation", "CameraAim"), 0),
        projectileCount=int(data.get("projectileCount", 1)),
        spreadAngle=f4(data.get("spreadAngle", 0)),
        telegraphShape=config.TELEGRAPH_SHAPE_INV.get(data.get("telegraphShape", "None"), 0),
        telegraphRadius=f4(data.get("telegraphRadius", 3)),
        telegraphAngle=f4(data.get("telegraphAngle", 90)),
        telegraphLength=f4(data.get("telegraphLength", 6)),
        telegraphWidth=f4(data.get("telegraphWidth", 0.5)),
        telegraphColorMode=config.COLOR_MODE_INV.get(data.get("telegraphColorMode", "Auto"), 0),
        tc_r=f4(color.get("r", 1.0)), tc_g=f4(color.get("g", 1.0)),
        tc_b=f4(color.get("b", 1.0)), tc_a=f4(color.get("a", 0.45)),
        telegraphFollowsCursor=b(data.get("telegraphFollowsCursor", False)),
        telegraphOriginOffset=f4(data.get("telegraphOriginOffset", 0)),
        castRange=f4(data.get("castRange", 10)),
        chainCount=int(data.get("chainCount", 0)),
        chainRadius=f4(data.get("chainRadius", 6)),
        chainDamageFalloff=f4(data.get("chainDamageFalloff", 0.6)),
        chainTravelTime=f4(data.get("chainTravelTime", 0.2)),
        chainJumpDelay=f4(data.get("chainJumpDelay", 0.1)),
    )

    dest.write_text(content, encoding="utf-8")
    _write_meta(dest, _new_guid())
    return True, f"Created '{asset_name}.asset' in Spells/T2/{school_label}/."


def _find_spell_asset(asset_file: str) -> Path | None:
    """Spells live under T2/<School>/. Search recursively for the asset by filename."""
    for path in config.SPELLS_DIR.rglob(f"{asset_file}.asset"):
        return path
    return None


def update_spell_asset(asset_file: str, data: dict) -> tuple[bool, str]:
    """
    Edit an existing SpellData .asset in place. Preserves icon/prefab/hitEffect/sound refs
    and the .meta GUID. If the school changes, moves both .asset and .meta to the new
    school directory.
    """
    path = _find_spell_asset(asset_file)
    if path is None or not path.exists():
        return False, f"Asset '{asset_file}.asset' not found in any spell school."

    text = path.read_text(encoding="utf-8")

    def b(v): return "1" if v else "0"
    def f4(v): return f"{round(float(v), 6)}"

    school_label = data.get("school", "Arcane")
    color = data.get("telegraphColor", {"r": 1.0, "g": 1.0, "b": 1.0, "a": 0.45})

    updates = {
        "school":      str(config.SPELL_SCHOOL_INV.get(school_label, 0)),
        "baseDamage":  f4(data.get("baseDamage", 0)),
        "damagePerSkillRank": f4(data.get("damagePerSkillRank", 0)),
        "chainCountPerRank":  str(int(data.get("chainCountPerRank", 0))),
        "cooldown":    f4(data.get("cooldown", 1.0)),
        "manaCost":    f4(data.get("manaCost", 0)),
        "spellName":   _yaml_str(data.get("spellName", asset_file)),
        "description": _yaml_str(data.get("description", "")),
        "spellType":   str(config.SPELL_TYPE_INV.get(data.get("spellType", "Cast"), 0)),
        "castStartDelay":     f4(data.get("castStartDelay", 0)),
        "castTime":           f4(data.get("castTime", 0)),
        "throwAnimLeadTime":  f4(data.get("throwAnimLeadTime", 0)),
        "lockMovementDuringCast":     b(data.get("lockMovementDuringCast", False)),
        "movementInterruptGrace":     f4(data.get("movementInterruptGrace", 0)),
        "damageInterruptGrace":       f4(data.get("damageInterruptGrace", 0)),
        "channelTickRate":            f4(data.get("channelTickRate", 0.5)),
        "fireOnChannelStart":         b(data.get("fireOnChannelStart", False)),
        "lockMovementDuringChannel":  b(data.get("lockMovementDuringChannel", False)),
        "spawnOrigin":   str(config.SPAWN_ORIGIN_INV.get(data.get("spawnOrigin", "FirePoint"), 0)),
        "spawnRotation": str(config.SPAWN_ROTATION_INV.get(data.get("spawnRotation", "CameraAim"), 0)),
        "projectileCount": str(int(data.get("projectileCount", 1))),
        "spreadAngle":     f4(data.get("spreadAngle", 0)),
        "telegraphShape":     str(config.TELEGRAPH_SHAPE_INV.get(data.get("telegraphShape", "None"), 0)),
        "telegraphRadius":    f4(data.get("telegraphRadius", 3)),
        "telegraphAngle":     f4(data.get("telegraphAngle", 90)),
        "telegraphLength":    f4(data.get("telegraphLength", 6)),
        "telegraphWidth":     f4(data.get("telegraphWidth", 0.5)),
        "telegraphColorMode": str(config.COLOR_MODE_INV.get(data.get("telegraphColorMode", "Auto"), 0)),
        "telegraphColor":     f"{{r: {f4(color.get('r', 1.0))}, g: {f4(color.get('g', 1.0))}, "
                              f"b: {f4(color.get('b', 1.0))}, a: {f4(color.get('a', 0.45))}}}",
        "telegraphFollowsCursor": b(data.get("telegraphFollowsCursor", False)),
        "telegraphOriginOffset":  f4(data.get("telegraphOriginOffset", 0)),
        "castRange":         f4(data.get("castRange", 10)),
        "chainCount":        str(int(data.get("chainCount", 0))),
        "chainRadius":       f4(data.get("chainRadius", 6)),
        "chainDamageFalloff": f4(data.get("chainDamageFalloff", 0.6)),
        "chainTravelTime":   f4(data.get("chainTravelTime", 0.2)),
        "chainJumpDelay":    f4(data.get("chainJumpDelay", 0.1)),
    }
    text = _update_scalar_fields(text, updates)

    # School change → move .asset + .meta to the new school folder.
    new_dir = config.SPELLS_DIR / "T2" / school_label
    moved = False
    if path.parent.resolve() != new_dir.resolve():
        _ensure_dir_with_meta(config.SPELLS_DIR / "T2")
        _ensure_dir_with_meta(new_dir)
        new_path = new_dir / path.name
        if new_path.exists():
            return False, f"Cannot move: '{new_path.name}' already exists in {school_label}/."
        # Write new content to the new location, move the .meta alongside, delete originals.
        new_path.write_text(text, encoding="utf-8")
        meta_src = Path(str(path) + ".meta")
        meta_dst = Path(str(new_path) + ".meta")
        if meta_src.exists():
            meta_dst.write_text(meta_src.read_text(encoding="utf-8"), encoding="utf-8")
            meta_src.unlink()
        path.unlink()
        moved = True
        msg_suffix = f" (moved to Spells/T2/{school_label}/)"
    else:
        path.write_text(text, encoding="utf-8")
        msg_suffix = ""

    return True, f"Updated '{asset_file}.asset'.{msg_suffix}"


# ─── Read Enemies ───

def scan_enemies() -> list[dict]:
    """Return list of parsed EnemyData dicts from the Enemies directory."""
    enemies = []
    for path in sorted(config.ENEMIES_DIR.glob("*.asset")):
        mb = _load_unity_yaml(path)
        if not mb:
            continue
        script = mb.get("m_Script", {})
        if script.get("guid") != config.ENEMY_SCRIPT_GUID:
            continue

        def gf(key, default=0.0):
            v = mb.get(key, default)
            return float(v) if v is not None else default

        def gi(key, default=0):
            v = mb.get(key, default)
            return int(v) if v is not None else default

        def gb(key):
            return bool(gi(key))

        # creatureTypes is a list of strings; falls back to the old single creatureType field for unmigrated assets.
        raw_types = mb.get("creatureTypes")
        if isinstance(raw_types, list) and raw_types:
            types_list = [config.CREATURE_TYPE.get(int(v), "Undead") for v in raw_types]
        else:
            types_list = [config.CREATURE_TYPE.get(gi("creatureType"), "Undead")]

        enemies.append({
            "asset_file":       path.stem,
            "enemyName":        mb.get("enemyName", ""),
            "description":      mb.get("description", ""),
            "level":            gi("level", 1),
            "creatureTypes":    types_list,
            "category":         config.ENEMY_CATEGORY.get(gi("category"), "Normal"),
            "maxHealth":        gf("maxHealth", 10.0),
            "moveSpeed":        gf("moveSpeed", 3.0),
            "attackDamageMin":  gi("attackDamageMin", 1),
            "attackDamageMax":  gi("attackDamageMax", 2),
            "attackCooldown":   gf("attackCooldown", 1.5),
            "attackRange":      gf("attackRange", 2.0),
            "detectionRange":   gf("detectionRange", 10.0),
            "attackStunChance": gf("attackStunChance", 0.2),
            "attackStunDuration": gf("attackStunDuration", 1.0),
            "retargetInterval": gf("retargetInterval", 1.0),
            "angularSpeed":     gf("angularSpeed", 200.0),
            "rotationSpeed":    gf("rotationSpeed", 200.0),
            "xpReward":         gi("xpReward", 50),
            "giveHPOnDeath":    gb("giveHPOnDeath"),
            "hpRewardOnDeath":  gi("hpRewardOnDeath", 1),
        })
    return enemies


def migrate_enemy_assets() -> int:
    """One-time migration: convert legacy `creatureType: N` to `creatureTypes:\\n- N`.
    Returns the number of assets migrated. Safe to call repeatedly — no-op if already migrated."""
    count = 0
    for path in sorted(config.ENEMIES_DIR.glob("*.asset")):
        mb = _load_unity_yaml(path)
        if not mb:
            continue
        script = mb.get("m_Script", {})
        if script.get("guid") != config.ENEMY_SCRIPT_GUID:
            continue
        if isinstance(mb.get("creatureTypes"), list):
            continue  # already migrated
        old_value = mb.get("creatureType", 0)
        text = path.read_text(encoding="utf-8")
        text = _remove_yaml_line(text, "creatureType")
        text = _replace_yaml_list_block(text, "creatureTypes", [int(old_value)], insert_after="icon")
        path.write_text(text, encoding="utf-8")
        count += 1
    return count


# ─── Write Enemies ───

_ENEMY_ASSET_TEMPLATE = """\
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {script_guid}, type: 3}}
  m_Name: {asset_name}
  m_EditorClassIdentifier: {class_id}
  enemyName: {enemy_name}
  description: {description}
  icon: {{fileID: 0}}
  creatureTypes:
{creature_types_block}  category: {category}
  level: {level}
  maxHealth: {max_health}
  moveSpeed: {move_speed}
  attackDamageMin: {attack_damage_min}
  attackDamageMax: {attack_damage_max}
  attackCooldown: {attack_cooldown}
  attackRange: {attack_range}
  detectionRange: {detection_range}
  attackStunChance: {attack_stun_chance}
  attackStunDuration: {attack_stun_duration}
  retargetInterval: {retarget_interval}
  angularSpeed: {angular_speed}
  rotationSpeed: {rotation_speed}
  xpReward: {xp_reward}
  giveHPOnDeath: {give_hp_on_death}
  hpRewardOnDeath: {hp_reward_on_death}"""


def write_enemy_asset(data: dict) -> tuple[bool, str]:
    """Write a new EnemyData .asset + .meta. Returns (success, message)."""
    asset_name = sanitize_asset_name(data["asset_name"])
    if not asset_name:
        return False, "Asset name is empty after sanitization."

    _ensure_dir_with_meta(config.ENEMIES_DIR)
    dest = config.ENEMIES_DIR / f"{asset_name}.asset"
    if dest.exists():
        return False, f"Asset '{asset_name}.asset' already exists."

    def f4(val): return round(float(val), 6)
    def b(val): return 1 if val else 0

    # creatureTypes -> indented "  - N\n" lines; defaults to [Undead] if empty.
    type_labels = data.get("creature_types") or ["Undead"]
    type_ints = [config.CREATURE_TYPE_INV.get(t, 0) for t in type_labels]
    creature_types_block = "".join(f"  - {i}\n" for i in type_ints)

    content = _ENEMY_ASSET_TEMPLATE.format(
        script_guid=config.ENEMY_SCRIPT_GUID,
        asset_name=asset_name,
        class_id=config.ENEMY_CLASS_ID,
        enemy_name=_yaml_str(data.get("enemy_name", asset_name)),
        description=_yaml_str(data.get("description", "")),
        creature_types_block=creature_types_block,
        category=config.ENEMY_CATEGORY_INV.get(data.get("category", "Normal"), 0),
        level=int(data.get("level", 1)),
        max_health=f4(data.get("max_health", 10.0)),
        move_speed=f4(data.get("move_speed", 3.0)),
        attack_damage_min=int(data.get("attack_damage_min", 1)),
        attack_damage_max=int(data.get("attack_damage_max", 2)),
        attack_cooldown=f4(data.get("attack_cooldown", 1.5)),
        attack_range=f4(data.get("attack_range", 2.0)),
        detection_range=f4(data.get("detection_range", 10.0)),
        attack_stun_chance=f4(data.get("attack_stun_chance", 0.2)),
        attack_stun_duration=f4(data.get("attack_stun_duration", 1.0)),
        retarget_interval=f4(data.get("retarget_interval", 1.0)),
        angular_speed=f4(data.get("angular_speed", 200.0)),
        rotation_speed=f4(data.get("rotation_speed", 200.0)),
        xp_reward=int(data.get("xp_reward", 50)),
        give_hp_on_death=b(data.get("give_hp_on_death", False)),
        hp_reward_on_death=int(data.get("hp_reward_on_death", 1)),
    )

    dest.write_text(content, encoding="utf-8")
    _write_meta(dest, _new_guid())
    return True, f"Created '{asset_name}.asset' in Enemies/."


def update_enemy_asset(asset_file: str, data: dict) -> tuple[bool, str]:
    """Edit an existing EnemyData .asset in place. Preserves icon/prefab refs and .meta GUID."""
    path = config.ENEMIES_DIR / f"{asset_file}.asset"
    if not path.exists():
        return False, f"Asset '{asset_file}.asset' not found."

    text = path.read_text(encoding="utf-8")

    def b(v): return "1" if v else "0"
    def f4(v): return f"{round(float(v), 6)}"

    updates = {
        "enemyName":         _yaml_str(data.get("enemy_name", asset_file)),
        "description":       _yaml_str(data.get("description", "")),
        "category":          str(config.ENEMY_CATEGORY_INV.get(data.get("category", "Normal"), 0)),
        "level":             str(int(data.get("level", 1))),
        "maxHealth":         f4(data.get("max_health", 10.0)),
        "moveSpeed":         f4(data.get("move_speed", 3.0)),
        "attackDamageMin":   str(int(data.get("attack_damage_min", 1))),
        "attackDamageMax":   str(int(data.get("attack_damage_max", 2))),
        "attackCooldown":    f4(data.get("attack_cooldown", 1.5)),
        "attackRange":       f4(data.get("attack_range", 2.0)),
        "detectionRange":    f4(data.get("detection_range", 10.0)),
        "attackStunChance":  f4(data.get("attack_stun_chance", 0.2)),
        "attackStunDuration": f4(data.get("attack_stun_duration", 1.0)),
        "retargetInterval":  f4(data.get("retarget_interval", 1.0)),
        "angularSpeed":      f4(data.get("angular_speed", 200.0)),
        "rotationSpeed":     f4(data.get("rotation_speed", 200.0)),
        "xpReward":          str(int(data.get("xp_reward", 50))),
        "giveHPOnDeath":     b(data.get("give_hp_on_death", False)),
        "hpRewardOnDeath":   str(int(data.get("hp_reward_on_death", 1))),
    }
    text = _update_scalar_fields(text, updates)

    # creatureTypes list: replace the block, dropping any legacy single creatureType scalar.
    type_labels = data.get("creature_types") or ["Undead"]
    type_ints = [config.CREATURE_TYPE_INV.get(t, 0) for t in type_labels]
    text = _remove_yaml_line(text, "creatureType")
    text = _replace_yaml_list_block(text, "creatureTypes", type_ints, insert_after="icon")

    path.write_text(text, encoding="utf-8")
    return True, f"Updated '{asset_file}.asset'."
