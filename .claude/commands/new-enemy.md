Create a new enemy type for HackNSLASH.

Enemy name: $ARGUMENTS

Steps:
1. Read `Assets/Scripts/EnemyAI.cs` as the reference implementation.
2. Read `Assets/Scripts/Enemyreward.cs` to understand the XP/loot reward contract.
3. Create any new script needed in `Assets/Scripts/` following the same patterns.
4. Enemy must use `HealthSystem` with `destroyOnDeath = true`.
5. Death destruction must only happen server-side — the `IsServer` guard in `HealthSystem.Die()` handles this; do not bypass it.
6. Enemy reward calls `ExperienceManager.Instance.GainXP(amount)` — confirm Instance is valid before calling.
