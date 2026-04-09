using UnityEngine;

[CreateAssetMenu(fileName = "NewSpell", menuName = "Spells/New Spell")]
public class SpellData : ScriptableObject
{
    [Header("Basic Info")]
    public string spellName = "Unnamed Spell";

    [TextArea(2, 4)]
    public string description = "A mysterious spell.";

    [Header("Visuals")]
    public Sprite icon;

    [Tooltip("Optional effect instantiated at the explosion center on impact.")]
    public GameObject hitEffect;

    [Header("Prefab")]
    [Tooltip("Projectile or effect prefab spawned when this spell is cast.")]
    public GameObject prefab;

    [Header("Casting")]
    [Tooltip("Delay in seconds between pressing the button and the spell firing. 0 = instant.")]
    public float castTime = 0f;

    [Tooltip("If true, holding the cast button fires repeatedly at channelTickRate intervals.")]
    public bool isChannelable = false;

    [Tooltip("Seconds between each fire tick while channeling. Ignored if not channelable.")]
    public float channelTickRate = 0.5f;

    [Header("Projectile")]
    [Tooltip("How many projectiles are fired per cast.")]
    public int projectileCount = 1;

    [Tooltip("Total spread cone angle when firing multiple projectiles (degrees).")]
    public float spreadAngle = 20f;
}
