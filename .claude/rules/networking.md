# Networking Rules - Netcode for GameObjects (NGO)

## Core Requirement
- Always develop with **both single-player and multiplayer** in mind.
- Every change must work seamlessly in Singleplayer (local/host-only) and Multiplayer (NGO networked).
- Prefer patterns that require minimal or no `#if` defines.
- Single-player should behave like a "local host" simulation whenever possible.

## Key NGO Practices
- Use `NetworkBehaviour` for any component that needs synchronization.
- Use `NetworkVariable<T>` for state that must stay in sync across clients.
- Use `ServerRpc` for actions that require server authority.
- Use `ClientRpc` sparingly and only when necessary (e.g. targeted owner-only calls).
- Ownership & Authority: Host has authority on gameplay logic unless specified otherwise.
- Minimize network traffic: avoid frequent RPCs or unnecessary NetworkVariable updates.

## Ownership Checks
- Always guard input processing with `if (!IsOwner) return;` at the top of input-reading methods in any `NetworkBehaviour`.
- **OwnerNetworkTransform** (not vanilla `NetworkTransform`) is used for player movement — it is client-authoritative. The server cannot call `Teleport()` on it directly; use a targeted `ClientRpc` to the owner instead (see `PlayerSpawner.ApplySpawnPositionClientRpc`).

## Project-Specific Authority Rules
- **HealthSystem is a `NetworkBehaviour`** with `NetworkVariable<float>` `currentHealth`/`maxHealth`. `TakeDamage()` / `Heal()` / `IncreaseMaxHealth()` / `ApplyEquipmentHP()` / `InitializeServerHP()` auto-route to the server via ServerRpc when called from a client — call them unconditionally from any context. **Do not add MP/solo branches at the call site.** `Die()` still has an `IsServer` guard for the despawn path (`destroyOnDeath`) — always preserve it when modifying death logic.
- **ExperienceManager.Instance is per-player**, not global. `SetAsLocalInstance()` assigns the singleton after `OnNetworkSpawn()`. Remote clients have their own instances — never assume `Instance` is the local player from a non-owner context.
- **XP / death / per-player rewards still flow server → owning-client**: never apply per-player gameplay state from a non-owning client. Route through `ServerRpc` → owner-targeted `ClientRpc`. (HP is exempt because HealthSystem now self-routes — the rule still applies to managers that are *not* NetworkBehaviours, e.g. `ExperienceManager`.)

## Singleplayer vs Multiplayer Sync
- After any change, mentally verify:
  - Does this work correctly offline in single-player?
  - Does it synchronize properly for remote clients?
  - Are there any authority or ownership issues?
- Keep logic as mode-agnostic as possible.

You must keep single-player and multiplayer behavior consistent unless the user explicitly asks for mode-specific differences.
