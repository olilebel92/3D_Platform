from pathlib import Path

from cs_parser import parse_cs_file

PROJECT_ROOT = Path(__file__).parent.parent
ITEMS_DIR    = PROJECT_ROOT / "Assets/ScriptableObjects/Item"
SPELLS_DIR   = PROJECT_ROOT / "Assets/ScriptableObjects/Spells"
ENEMIES_DIR  = PROJECT_ROOT / "Assets/ScriptableObjects/Enemies"

# Confirmed from .cs.meta files
ITEM_SCRIPT_GUID  = "9c5f8a9e4be4b6c42a10ba05afe871e9"
SPELL_SCRIPT_GUID = "4378247a87f53c34aa130d1b75a0dc31"
ITEM_CLASS_ID     = "Assembly-CSharp::ItemData"
SPELL_CLASS_ID    = "Assembly-CSharp::SpellData"

# Filled automatically after Unity imports EnemyData.cs — see step 9 of setup
ENEMY_SCRIPT_GUID = "3dbeaa33d044b3849959fdc32f1563ee"
ENEMY_CLASS_ID    = "Assembly-CSharp::EnemyData"

# ─── Parse enums directly from C# source ───────────────────────────────────
_item_enums  = parse_cs_file(PROJECT_ROOT / "Assets/Scripts/ItemData.cs")
_spell_enums = parse_cs_file(PROJECT_ROOT / "Assets/Scripts/SpellData.cs")
_enemy_enums = parse_cs_file(PROJECT_ROOT / "Assets/Scripts/EnemyData.cs")

# ItemData enums
EQUIPMENT_SLOT = _item_enums.get("EquipmentSlot", {0: "Boots", 1: "Helm", 2: "Pants", 3: "Chest"})
ITEM_RARITY    = _item_enums.get("ItemRarity",    {0: "Normal", 1: "Uncommon", 2: "Rare", 3: "Epic", 4: "Legendary", 5: "Godly"})
STAT_TYPE      = _item_enums.get("StatType",      {0: "STR", 1: "AGI", 2: "INT"})

# SpellData enums
SPELL_SCHOOL    = _spell_enums.get("SpellSchool",        {0: "Arcane", 1: "Fire", 2: "Healing", 3: "Lightning"})
SPELL_TYPE      = _spell_enums.get("SpellType",          {0: "Cast", 1: "Buff", 2: "Aura", 3: "Channel", 4: "TargetLocked"})
TELEGRAPH_SHAPE = _spell_enums.get("TelegraphShape",     {0: "None", 1: "Circle", 2: "Cone", 3: "Line"})
COLOR_MODE      = _spell_enums.get("TelegraphColorMode", {0: "Auto", 1: "Custom"})
SPAWN_ORIGIN    = _spell_enums.get("SpellSpawnOrigin",   {0: "FirePoint", 1: "Caster"})
SPAWN_ROTATION  = _spell_enums.get("SpellSpawnRotation", {0: "CameraAim", 1: "WorldUp", 2: "CasterForward", 3: "None"})

# ─── Inverse maps (label → int) ─────────────────────────────────────────────
EQUIPMENT_SLOT_INV = {v: k for k, v in EQUIPMENT_SLOT.items()}
ITEM_RARITY_INV    = {v: k for k, v in ITEM_RARITY.items()}
STAT_TYPE_INV      = {v: k for k, v in STAT_TYPE.items()}
SPELL_SCHOOL_INV   = {v: k for k, v in SPELL_SCHOOL.items()}
SPELL_TYPE_INV     = {v: k for k, v in SPELL_TYPE.items()}
TELEGRAPH_SHAPE_INV = {v: k for k, v in TELEGRAPH_SHAPE.items()}
COLOR_MODE_INV     = {v: k for k, v in COLOR_MODE.items()}
SPAWN_ORIGIN_INV   = {v: k for k, v in SPAWN_ORIGIN.items()}
SPAWN_ROTATION_INV = {v: k for k, v in SPAWN_ROTATION.items()}

# EnemyData enums
CREATURE_TYPE  = _enemy_enums.get("CreatureType",  {0: "Undead", 1: "Human", 2: "Orc", 3: "Slime"})
ENEMY_CATEGORY = _enemy_enums.get("EnemyCategory", {0: "Normal", 1: "Elite", 2: "Boss", 3: "Magic", 4: "Hunter"})

CREATURE_TYPE_INV  = {v: k for k, v in CREATURE_TYPE.items()}
ENEMY_CATEGORY_INV = {v: k for k, v in ENEMY_CATEGORY.items()}

# ─── Dashboard-only display constants ───────────────────────────────────────
# These are visual hints only — unknown values fall back to grey.
RARITY_COLORS: dict[str, str] = {
    "Normal":    "#CCCCCC",
    "Uncommon":  "#1EFF00",
    "Rare":      "#0070DD",
    "Epic":      "#A335EE",
    "Legendary": "#FF8000",
    "Godly":     "#FF1A1A",
}

ENEMY_CATEGORY_COLORS: dict[str, str] = {
    "Normal":  "#CCCCCC",
    "Elite":   "#0070DD",
    "Boss":    "#FF1A1A",
    "Magic":   "#A335EE",
    "Hunter":  "#1EFF00",
}

SPELL_SCHOOL_COLORS: dict[str, str] = {
    "Arcane":    "#A335EE",
    "Fire":      "#FF4500",
    "Healing":   "#1EFF00",
    "Lightning": "#FFD700",
}
