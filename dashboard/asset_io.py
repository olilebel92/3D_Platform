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
