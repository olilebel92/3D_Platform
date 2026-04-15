using UnityEngine;

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
}

[CreateAssetMenu(fileName = "NewSpell", menuName = "Spells/New Spell")]
public class SpellData : ScriptableObject
{
    [Header("School")]
    [Tooltip("Fire: applies fire damage bonuses. Healing: treated as a heal. Arcane: base spell bonuses only.")]
    public SpellSchool school = SpellSchool.Arcane;

    [Header("Basic Info")]
    public string spellName = "Unnamed Spell";

    [TextArea(2, 4)]
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

    [Tooltip("Fill colour of the telegraph decal (alpha controls opacity).")]
    public Color telegraphColor = new Color(1f, 0.5f, 0f, 0.45f);

    [Tooltip("When true, Circle telegraphs follow the cursor (targeted AOE). " +
             "When false, the shape stays centred on the caster (self-cast AOE).")]
    public bool telegraphFollowsCursor = true;

    [Tooltip("How far forward from the caster the Cone or Line telegraph begins (world units). " +
             "Use this to push the shape past the character's feet.")]
    public float telegraphOriginOffset = 0f;
}
