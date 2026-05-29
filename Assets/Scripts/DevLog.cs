using UnityEngine;
using UnityEngine.UIElements;

// Claude updates LOG_TEXT when asked to update the devlog.
// MainMenuManager calls Populate() when opening the DevLog panel.
public class DevLog : MonoBehaviour
{
    // ─── Log Text ─────────────────────────────────────────────────────────────

    private const string LOG_TEXT =
@"DoldiGAME

<b><color=#e8920a>v0.3 - Online Multiplayer (Unity Relay)</color></b>  <color=#7a7a9a>2026-05-29 — In Progress</color>
• Unity Relay integration — play over the internet without port forwarding
• Join via a short code instead of an IP address
• Unity Gaming Services: Relay + Lobby packages
• Changes to OnHost() / OnConnect() in MainMenuManager

<b><color=#3dba6e>v0.25 - Combat Sync & Co-op Polish</color></b>  <color=#7a7a9a>2026-05-29</color>
• HealthSystem NetworkBehaviour — HP synced via NetworkVariable to all clients
• TakeDamage() auto-routes to server — no IsServer guard needed at call sites
• MP co-op spectator mode — dead players spectate teammates, auto-respawn on wave clear
• InputManager single shared PlayerInputActions instance (Player + UI + Spell maps)
• Item SO catalog — 36 items, Warrior / Mage / Rogue, Epic through Godly
• LootTableSO ItemPool drops + loot drop animation polish

<b><color=#3dba6e>v0.2 - Spells, Skill Tree & Polish</color></b>  <color=#7a7a9a>2026-05-08</color>
• HealingWave spell - AoE heal, server-authoritative
• Stun system (StatusEffectHandler) - blocks casts & movement
• Chain lightning walk-to-cast + rank-up by kill chain count
• Skill tree multi-level nodes - HP/regen bonuses, STR/AGI/INT flat + spell/fire/heal %
• Skill tree connector UI, passive HP & Stats nodes
• Enemy stun on attack (configurable chance + duration)
• WaveManager custom rewards - per-wave item drops
• Thor armor set (Chest, Helm, Pants, Boots)
• Settings panel - master & music volume
• FPS overlay (F3) - smoothed FPS, ms, memory usage
• GC pass - NonAlloc raycasts, static buffers, PlayerController.All registry
• Shader preload via ShaderVariantCollection

<b><color=#3dba6e>v0.15 - UI Toolkit & Polish</color></b>  <color=#7a7a9a>2026-04-19</color>
• SceneTransition
• LobbyManager - SoundManager.PlayUI() wired on all button
• MainMenuManager
• ExperienceManager
• EnemyReward - fix drop spawn during Editor stop-play teardown
• CursorManager - CursorMode.Auto
• PlayMode - 2-Players test scenario
• Regenerated Cinzel + JetBrains Mono SDF font

<b><color=#3dba6e>v0.1 - Foundation</color></b>  <color=#7a7a9a>2026-04-11</color>
• Player movement, sprint, stamina, melee, fireball
• Enemy AI + spawner, wave manager, XP / leveling (STR / AGI / INT)
• Spell bar (10 slots), character window, inventory, damage popups
• Unity NGO integrated - Host / Join, lobby, spectator free-cam
• Player as NetworkObject with OwnerNetworkTransform
• Per-player ExperienceManager & PlayerInventory
• Networked enemy AI (server-authoritative) & LootPickup
• EnemyColorRandomizer, LobbyChatManager, NGO optimization
• SpellCaster state machine - PreCast, Pending, Channeling, Idle

All assets are placeholders";

    // ─── Public API ───────────────────────────────────────────────────────────

    // Called by MainMenuManager when the DevLog panel opens.
    public void Populate(ScrollView scrollView)
    {
        if (scrollView == null) return;

        scrollView.Clear();

        var label = new Label(LOG_TEXT);
        label.AddToClassList("text-body");
        label.style.whiteSpace = WhiteSpace.Normal;

        scrollView.Add(label);
    }
}
