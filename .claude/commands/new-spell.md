Create a new spell for HackNSLASH.

Spell name: $ARGUMENTS

Steps:
1. Read `Assets/Scripts/Spells/Fireball.cs` as the reference implementation.
2. Read `Assets/Scripts/SpellData.cs` to understand the data contract.
3. Create `Assets/Scripts/Spells/<SpellName>.cs` following the same structure.
4. The spell must work in both singleplayer and multiplayer (see `.claude/rules/networking.md`).
5. Damage must be applied server-side only via the existing damage pipeline.
6. Respect INT scaling: use `ExperienceManager.Instance.SpellDamageMultiplier` for damage calculation.
