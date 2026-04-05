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

    [Tooltip("Icon displayed on the node button.")]
    public Sprite icon;

    [Header("Cost & Requirements")]
    [Tooltip("How many skill points it costs to learn this node.")]
    public int cost = 1;

    [Tooltip("All of these nodes must be learned before this one can be purchased.")]
    public SkillTreeNode[] prerequisites;

    [Header("Passive Effects")]
    [Tooltip("Flat bonus added to all spell damage when this node is learned.")]
    public float spellDamageBonus = 0f;

    [Tooltip("Flat bonus added specifically to fire spell damage when this node is learned.")]
    public float fireDamageBonus = 0f;

    [Header("Spell Unlock")]
    [Tooltip("If assigned, this spell is added to the first empty spell bar slot when learned.")]
    public SpellData unlocksSpell;
}
