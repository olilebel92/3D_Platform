// ── DEPRECATED ────────────────────────────────────────────────────────────────
// PickupItem has been split into two focused scripts:
//   - PickupVisual  → bob + spin animation (pure visual, no trigger)
//   - LootPickup    → reward logic + collection trigger + spawner notification
//
// To update your prefabs:
//   1. Remove the PickupItem component from any prefabs in the Inspector.
//   2. Add PickupVisual  for the bob/spin animation.
//   3. Add LootPickup    for the reward (XP / HP / Mana / Material).
//   4. Delete this file once all prefabs are updated.
// ─────────────────────────────────────────────────────────────────────────────
