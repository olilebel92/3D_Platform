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
    """Replace each `  key: <existing>` line with `  key: <new>`. Single occurrence per key."""
    for key, value in updates.items():
        pattern = rf"^(?P<indent>\s+){re.escape(key)}:\s*[^\n\r]*$"
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
    # Missing: insert after `insert_after` line if provided, else append at end
    if insert_after:
        anchor_pattern = re.compile(rf"^(\s+){re.escape(insert_after)}:")
        out2: list[str] = []
        inserted = False
        for line in out:
            out2.append(line)
            if not inserted:
                am = anchor_pattern.match(line)
                if am:
                    indent = am.group(1)
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
                "type": config.STAT_TYPE.get(sl.get("type", 0), "STR"),
                "value": float(sl.get("value", 0)),
            })
        items.append({
            "asset_file": path.stem,
            "itemName":   mb.get("itemName", ""),
            "description": mb.get("description", ""),
            "slot":   config.EQUIPMENT_SLOT.get(mb.get("slot", 0), "Boots"),
            "rarity": config.ITEM_RARITY.get(mb.get("rarity", 0), "Normal"),
            "statLines": stat_lines,
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
  slot: {slot}
  rarity: {rarity}
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
        slot=config.EQUIPMENT_SLOT_INV.get(data.get("slot", "Boots"), 0),
        rarity=config.ITEM_RARITY_INV.get(data.get("rarity", "Normal"), 0),
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
        "slot":        str(config.EQUIPMENT_SLOT_INV.get(data.get("slot", "Boots"), 0)),
        "rarity":      str(config.ITEM_RARITY_INV.get(data.get("rarity", "Normal"), 0)),
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
