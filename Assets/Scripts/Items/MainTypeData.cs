using UnityEngine;

/// <summary>
/// Top-level category for items (Weapon, Armor, future: Trinket, Consumable).
/// Create assets in Assets/ScriptableObjects/Items/MainTypes/.
/// </summary>
[CreateAssetMenu(fileName = "NewMainType", menuName = "RPG/Items/Main Type Data")]
public class MainTypeData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Display name shown in UI (e.g. \"Weapon\", \"Armor\").")]
    public string displayName = "Armor";

    [Tooltip("Icon used in dashboard / UI category headers.")]
    public Sprite icon;

    [Header("Behavior")]
    [Tooltip("True for weapon categories — items of this main type should be created as WeaponItemData assets.")]
    public bool isWeapon = false;
}
