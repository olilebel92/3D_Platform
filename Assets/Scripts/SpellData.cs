using UnityEngine;

/// <summary>How the telegraph fill colour is resolved at runtime.</summary>
public enum TelegraphColorMode
{
    /// <summary>Green for Healing spells, red for all others.</summary>
    Auto,
    /// <summary>Use the custom colour set on the SpellData asset.</summary>
    Custom,
}

/// <summary>Ground shape drawn as a telegraph before a spell fires.</summary>
public enum TelegraphShape
{
    /// <summary>No telegraph — spell fires without a preview.</summary>
    None,
    /// <summary>Filled circle. Use for targeted AOE (e.g. meteor landing zone).</summary>
    Circle,
    /// <summary>Filled sector/wedge from the caster toward the cursor. Use for frontal cones.</summary>
    Cone,
    /// <summary>Filled rectangle extending forward from the caster. Use for linear beams.</summary>
    Line,
}

/// <summary>Determines which damage school bonuses apply to this spell.</summary>
public enum SpellSchool
{
    /// <summary>Generic magic — benefits from spell damage bonuses only.</summary>
    Arcane,
    /// <summary>Fire magic — benefits from spell damage AND fire damage bonuses.</summary>
    Fire,
    /// <summary>Healing — benefits from spell damage bonuses as heal power; fire bonuses are ignored.</summary>
    Healing,
    /// <summary>Lightning magic — benefits from spell damage AND lightning damage bonuses.</summary>
    Lightning,
}

/// <summary>Where the spell prefab is spawned.</summary>
public enum SpellSpawnOrigin
{
    /// <summary>Spawned at the FirePoint transform (default — projectiles, beams).</summary>
    FirePoint,
    /// <summary>Spawned at the caster's world position (AOE centered on self).</summary>
    Caster,
}

/// <summary>What rotation the spell prefab is given on spawn.</summary>
public enum SpellSpawnRotation
{
    /// <summary>Matches the camera's aim direction (default — projectiles).</summary>
    CameraAim,
    /// <summary>Faces straight up (ground AOE, upward bursts).</summary>
    WorldUp,
    /// <summary>Matches the caster's facing direction, ignoring camera pitch.</summary>
    CasterForward,
    /// <summary>No rotation applied — preserves the prefab's authored orientation (identity).</summary>
    None,
}

/// <summary>Determines how the spell behaves when cast.</summary>
public enum SpellType
{
    /// <summary>Fires once (after castStartDelay + castTime). Movement/damage may interrupt.</summary>
    Cast,
    /// <summary>Applies an effect to the caster. Instant or after a cast time.</summary>
    Buff,
    /// <summary>Toggles a persistent aura effect on the caster.</summary>
    Aura,
    /// <summary>Fires repeatedly while the cast button is held.</summary>
    Channel,
    /// <summary>Fires at a locked enemy target. Requires target selected via TargetSelector (right-click enemy).</summary>
    TargetLocked,
}

[CreateAssetMenu(fileName = "NewSpell", menuName = "Spells/New Spell")]
public class SpellData : ScriptableObject
{
    [Header("School")]
    [Tooltip("Fire: applies fire damage bonuses. Healing: treated as a heal. Arcane: base spell bonuses only.")]
    public SpellSchool school = SpellSchool.Arcane;

    [Header("Combat")]
    [Tooltip("Base damage (or heal) this spell deals. Drives the skill tree tooltip and the damage formula.")]
    public float baseDamage = 0f;

    [Tooltip("Extra damage (or heal) added per skill tree rank above 1. Rank 1 uses baseDamage only.")]
    public float damagePerSkillRank = 0f;

    [Tooltip("Seconds before this spell can be cast again after firing.")]
    public float cooldown = 1f;

    [Header("Basic Info")]
    public string spellName = "Unnamed Spell";

    [TextArea(2, 4)]
    [Tooltip("Supports tokens: {base} base damage, {bonus} rank bonus damage, {total} combined, " +
             "{cooldown} cooldown in seconds, {rankBonus} damagePerSkillRank value.")]
    public string description = "A mysterious spell.";

    [Header("Visuals")]
    public Sprite icon;

    [Tooltip("Optional effect instantiated at the explosion center on impact.")]
    public GameObject hitEffect;

    [Header("Prefab")]
    [Tooltip("Projectile or effect prefab spawned when this spell fires.")]
    public GameObject prefab;

    [Header("Spell Type")]
    [Tooltip("Cast: fires once after cast time. Buff: applies to caster. Aura: toggles persistent effect. Channel: fires repeatedly while held.")]
    public SpellType spellType = SpellType.Cast;

    [Header("Cast Timing")]
    [Tooltip("Brief delay before the cast bar appears — use for windup animation windows. 0 = cast bar starts immediately.")]
    public float castStartDelay = 0f;

    [Tooltip("Time in seconds to charge before the spell fires. 0 = fires immediately after castStartDelay.")]
    public float castTime = 0f;

    [Tooltip("How many seconds before the cast completes to trigger the throw animation. Should match the throw clip duration.")]
    public float throwAnimLeadTime = 0.5f;

    [Header("Interrupt Rules")]
    [Tooltip("When true, player movement is completely suppressed for the full cast duration (PreCast + cast bar). " +
             "When false, the player can move freely while casting — movement may still interrupt the cast unless covered by movementInterruptGrace.")]
    public bool lockMovementDuringCast = true;

    [Tooltip("Seconds from cast start during which movement will NOT interrupt this cast. Stun always interrupts regardless.")]
    public float movementInterruptGrace = 0f;

    [Tooltip("Seconds from cast start during which taking damage will NOT interrupt this cast. Stun always interrupts regardless.")]
    public float damageInterruptGrace = 0f;

    [Header("Channel Settings")]
    [Tooltip("Seconds between each fire tick while channeling. Only used when spellType = Channel.")]
    public float channelTickRate = 0.5f;

    [Tooltip("When true, fires once immediately when channeling begins (in addition to regular ticks). " +
             "Leave unchecked to have the first effect fire on the first tick only.")]
    public bool fireOnChannelStart = false;

    [Tooltip("When true, player movement is completely suppressed for the full duration of the channel — " +
             "the character is rooted in place while the button is held. " +
             "The movementInterruptGrace window still applies at the start of the cast bar phase.")]
    public bool lockMovementDuringChannel = false;

    [Header("Spawn")]
    [Tooltip("FirePoint: spawned at the hand/muzzle. Caster: spawned at the player's pivot (AOE centered on self).")]
    public SpellSpawnOrigin spawnOrigin = SpellSpawnOrigin.FirePoint;

    [Tooltip("CameraAim: follows camera direction. WorldUp: faces straight up. CasterForward: uses player facing, ignoring pitch.")]
    public SpellSpawnRotation spawnRotation = SpellSpawnRotation.CameraAim;

    [Header("Projectile")]
    [Tooltip("How many projectiles are fired per cast or channel tick.")]
    public int projectileCount = 1;

    [Tooltip("Total spread cone angle when firing multiple projectiles (degrees).")]
    public float spreadAngle = 20f;

    [Header("Audio")]
    [Tooltip("Sound played locally when the player initiates this spell's cast.")]
    public AudioClip castSound;

    [Tooltip("Sound played on impact or when the spell effect lands.")]
    public AudioClip hitSound;

    // ─── Telegraph ────────────────────────────────────────────────────────────

    [Header("Telegraph")]
    [Tooltip("Shape drawn on the ground to preview the spell's area before it fires. None = no preview.")]
    public TelegraphShape telegraphShape = TelegraphShape.None;

    [Tooltip("Radius of the circle telegraph (TelegraphShape.Circle only).")]
    public float telegraphRadius = 3f;

    [Tooltip("Full opening angle in degrees of the cone telegraph (TelegraphShape.Cone only). E.g. 90 = ±45° each side.")]
    public float telegraphAngle = 90f;

    [Tooltip("Length of the cone or line telegraph in world units.")]
    public float telegraphLength = 6f;

    [Tooltip("Width of the line telegraph in world units (TelegraphShape.Line only).")]
    public float telegraphWidth = 1.5f;

    [Tooltip("Auto: green for Healing, red for damage spells. Custom: use the colour below.")]
    public TelegraphColorMode telegraphColorMode = TelegraphColorMode.Auto;

    [Tooltip("Fill colour used only when Telegraph Color Mode is set to Custom (alpha controls opacity).")]
    public Color telegraphColor = new Color(1f, 0.5f, 0f, 0.45f);

    // ─── Chain / Target-Locked ────────────────────────────────────────────────

    [Header("Chain / Target-Locked")]
    [Tooltip("Maximum cast range for TargetLocked spells (world units). 0 = unlimited.")]
    public float castRange = 0f;

    [Tooltip("Number of targets hit, including the primary target. Only used when spellType = TargetLocked.")]
    public int chainCount = 3;

    [Tooltip("Max world-unit distance between chain jumps. Only used when spellType = TargetLocked.")]
    public float chainRadius = 6f;

    [Tooltip("Damage multiplier per chain jump after the first (0.6 = 60% of previous). Only used when spellType = TargetLocked.")]
    [Range(0.1f, 1f)]
    public float chainDamageFalloff = 0.6f;

    [Tooltip("Seconds the bolt takes to travel from its current position to the next target. Only used when spellType = TargetLocked.")]
    public float chainTravelTime = 0.2f;

    [Tooltip("Seconds to wait at a target after hitting it before jumping to the next one. Only used when spellType = TargetLocked.")]
    public float chainJumpDelay = 0.3f;

    /// <summary>
    /// Returns the base damage for this spell.
    /// Prefers the SpellData baseDamage field if set (> 0).
    /// Falls back to reading the prefab component for legacy Fireball / HealingWave assets
    /// that have not yet had their base damage migrated to SpellData.
    /// </summary>
    public float BaseDamage
    {
        get
        {
            if (baseDamage > 0f) return baseDamage;
            if (prefab == null) return 0f;
            if (prefab.TryGetComponent<Fireball>(out var fb))        return fb.baseDamage;
            if (prefab.TryGetComponent<HealingWave>(out var hw))     return hw.baseHeal;
            if (prefab.TryGetComponent<ChainLightning>(out var cl))  return cl.baseDamage;
            return 0f;
        }
    }

    /// <summary>
    /// Returns the description with tokens replaced by live values.
    /// <para>Available tokens: {base} {bonus} {total} {cooldown} {rankBonus}</para>
    /// </summary>
    /// <param name="baseDamage">The spell prefab's base damage (pass 0 if unknown).</param>
    /// <param name="skillRank">Current skill tree rank of this spell (1 = no bonus).</param>
    public string GetDescription(float baseDamage = 0f, int skillRank = 1)
    {
        float bonus = (skillRank - 1) * damagePerSkillRank;
        float total = baseDamage + bonus;
        return description
            .Replace("{base}",      Gold(baseDamage.ToString("0")))
            .Replace("{bonus}",     Gold(bonus.ToString("0")))
            .Replace("{total}",     Gold(total.ToString("0")))
            .Replace("{cooldown}",  Gold(cooldown.ToString("0.#")))
            .Replace("{rankBonus}", Gold(damagePerSkillRank.ToString("0")));
    }

    static string Gold(string value) => $"<color=#FFD700>{value}</color>";

    /// <summary>
    /// Resolved telegraph fill colour. Auto mode: green for Healing, red for all other schools.
    /// Custom mode: returns <see cref="telegraphColor"/> as-is.
    /// </summary>
    public Color ResolvedTelegraphColor
    {
        get
        {
            if (telegraphColorMode == TelegraphColorMode.Custom) return telegraphColor;
            return school switch
            {
                SpellSchool.Healing   => new Color(0.2f, 0.9f, 0.2f, 0.45f),  // green
                SpellSchool.Lightning => new Color(0.9f, 0.9f, 0.1f, 0.45f),  // yellow
                _                     => new Color(0.9f, 0.1f, 0.1f, 0.45f),  // red
            };
        }
    }

    [Tooltip("When true, Circle telegraphs follow the cursor (targeted AOE). " +
             "When false, the shape stays centred on the caster (self-cast AOE).")]
    public bool telegraphFollowsCursor = true;

    [Tooltip("How far forward from the caster the Cone or Line telegraph begins (world units). " +
             "Use this to push the shape past the character's feet.")]
    public float telegraphOriginOffset = 0f;
}
