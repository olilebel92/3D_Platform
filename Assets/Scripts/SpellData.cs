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
}