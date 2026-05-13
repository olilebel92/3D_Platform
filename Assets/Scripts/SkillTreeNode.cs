using UnityEngine;

[System.Serializable]
public class NodePrerequisite
{
    [Tooltip("The prerequisite node.")]
    public SkillTreeNode node;

    [Tooltip("Minimum level the prerequisite node must have reached.")]
    [Min(1)]
    public int requiredLevel = 1;
}

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
    [Tooltip("Short description shown in the tooltip panel. " +
             "Tokens (resolved from unlocksSpell if set): {rankBonus} {bonus} {cooldown} {rank} {maxRank} {chains}")]
    public string description = "Describe what this skill does.";

    [Tooltip("Icon displayed on the node button. Leave empty to automatically use the unlocked spell's icon.")]
    public Sprite icon;

    [Header("Cost & Requirements")]
    [Tooltip("How many skill points it costs to learn this node.")]
    public int cost = 1;

    [Tooltip("All of these nodes must reach their required level before this one can be purchased.")]
    public NodePrerequisite[] prerequisites;

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

    [Tooltip("Flat maximum HP added per level.")]
    public int hpBonus = 0;

    [Tooltip("Flat HP regenerated per second added per level.")]
    public float hpRegenBonus = 0f;

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

    [Tooltip("Flat bonus per level added specifically to lightning spell damage.")]
    public float lightningDamageBonus = 0f;

    [Tooltip("Percent bonus per level added specifically to lightning spell damage. (0.10 = +10%)")]
    public float lightningDamagePctBonus = 0f;

    // ─── Description Resolution ───────────────────────────────────────────────

    /// <summary>
    /// Returns the description with tokens replaced by live values.
    /// Tokens: {rank} {maxRank} {base} {rankBonus} {bonus} {total} {cooldown}
    /// </summary>
    public string GetDescription(int rank = 1)
    {
        // If this node has no custom description, use the linked spell's description text.
        bool hasOwnDesc = !string.IsNullOrWhiteSpace(description)
                       && description != "Describe what this skill does.";
        string baseText = (!hasOwnDesc && unlocksSpell != null)
            ? unlocksSpell.description
            : description;

        string desc = baseText
            .Replace("{rank}",    Gold(rank.ToString()))
            .Replace("{maxRank}", Gold(maxLevel.ToString()));

        if (unlocksSpell != null)
        {
            float baseDmg        = unlocksSpell.BaseDamage;
            float bonus          = (rank - 1) * unlocksSpell.damagePerSkillRank;
            float total          = baseDmg + bonus;
            int   effectiveChains = unlocksSpell.chainCount + Mathf.Max(0, rank - 1) * unlocksSpell.chainCountPerRank;
            desc = desc
                .Replace("{base}",      Gold(baseDmg.ToString("0")))
                .Replace("{rankBonus}", Gold(unlocksSpell.damagePerSkillRank.ToString("0")))
                .Replace("{bonus}",     Gold(bonus.ToString("0")))
                .Replace("{total}",     Gold(total.ToString("0")))
                .Replace("{cooldown}",  Gold(unlocksSpell.cooldown.ToString("0.#")))
                .Replace("{chains}",    Gold(effectiveChains.ToString()));
        }

        return desc;
    }

    static string Gold(string value) => $"<color=#FFD700>{value}</color>";

    [Header("Additional Requirements")]
    [Tooltip("If true, the player must have unlocked at least one spell in the skill tree before buying this node. " +
             "Use on spell damage / modifier nodes to prevent spending points before having any spell.")]
    public bool requiresAnySpell = false;

    [Header("Spell Unlock")]
    [Tooltip("If assigned, this spell is added to the first empty spell bar slot when learned.")]
    public SpellData unlocksSpell;
}
