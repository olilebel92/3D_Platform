using UnityEngine;

/// <summary>
/// ScriptableObject that defines a single node in the skill tree.
///
/// Create via: Assets → right-click → Create → Skills → Skill Tree Node
/// </summary>
[CreateAssetMenu(fileName = "NewSkillNode", menuName = "Skills/Skill Tree Node")]
public class SkillTreeNode : ScriptableObject
{
    [Header("Info")]
    [Tooltip("Display name shown in the UI.")]
    public string nodeName = "New Skill";

    [TextArea(2, 4)]
    [Tooltip("Short description shown in the tooltip panel.")]
    public string description = "Describe what this skill does.";

    [Tooltip("Icon displayed on the node button. Leave empty to automatically use the unlocked spell's icon.")]
    public Sprite icon;

    [Header("Cost & Requirements")]
    [Tooltip("How many skill points it costs to learn this node.")]
    public int cost = 1;

    [Tooltip("All of these nodes must be learned before this one can be purchased.")]
    public SkillTreeNode[] prerequisites;

    [Header("Levels")]
    [Tooltip("Maximum number of times this node can be leveled up.")]
    [Min(1)]
    public int maxLevel = 1;

    [Tooltip("Multiplier applied to each bonus per level. " +
             "At level N, bonus = baseStat * scalingFactor * N.")]
    public float scalingFactor = 1f;

    [Header("Passive Effects — Flat Stats")]
    [Tooltip("Flat STR added per level.")]
    public int strBonus = 0;

    [Tooltip("Flat AGI added per level.")]
    public int agiBonus = 0;

    [Tooltip("Flat INT added per level.")]
    public int intBonus = 0;

    [Tooltip("Flat bonus per level added to all spell damage.")]
    public float spellDamageBonus = 0f;

    [Tooltip("Flat bonus per level added specifically to fire spell damage.")]
    public float fireDamageBonus = 0f;

    [Tooltip("Flat bonus per level added specifically to healing spells.")]
    public float healBonus = 0f;

    [Header("Passive Effects — Percent Bonuses")]
    [Tooltip("Percent bonus per level added to all spell damage. (0.10 = +10%)")]
    public float spellDamagePctBonus = 0f;

    [Tooltip("Percent bonus per level added specifically to fire spell damage. (0.10 = +10%)")]
    public float fireDamagePctBonus = 0f;

    [Tooltip("Percent bonus per level added specifically to healing spells. (0.10 = +10%)")]
    public float healPctBonus = 0f;

    [Header("Spell Unlock")]
    [Tooltip("If assigned, this spell is added to the first empty spell bar slot when learned.")]
    public SpellData unlocksSpell;
}
