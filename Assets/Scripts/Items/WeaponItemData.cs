using UnityEngine;

/// <summary>
/// Weapon-flavoured ItemData. Adds combat-only fields (base damage, attack speed, range)
/// on top of the standard stat-line system. Combat systems should test:
///     if (item is WeaponItemData w) { use w.baseDamage; ... }
/// </summary>
[CreateAssetMenu(fileName = "NewWeapon", menuName = "RPG/Items/Weapon Item")]
public class WeaponItemData : ItemData
{
    [Header("Weapon Stats")]
    [Tooltip("Base damage applied per hit before stat bonuses.")]
    public float baseDamage = 10f;

    [Tooltip("Attacks per second (1 = one hit/sec). Higher is faster.")]
    public float attackSpeed = 1f;

    [Tooltip("Effective range in world units. Melee = ~2, ranged weapons higher.")]
    public float range = 2f;
}
