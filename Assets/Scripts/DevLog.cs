using UnityEngine;
using UnityEngine.UIElements;

// Claude updates LOG_TEXT when asked to update the devlog.
// MainMenuManager calls Populate() when opening the DevLog panel.
public class DevLog : MonoBehaviour
{
    // ─── Log Text ─────────────────────────────────────────────────────────────

    private const string LOG_TEXT =
@"── HACKNSLASH DEVLOG ──

<b><color=#3dba6e>v0.15 — UI Toolkit Transitions & Polish</color></b>  <color=#7a7a9a>2026-04-19 — Latest</color>
• SceneTransitionManager — UI Toolkit + uGUI, FadeOutThen / FadeIn API
• LobbyManager — SoundManager.PlayUI() wired on all button callbacks
• MainMenuManager — host startup wrapped in fade-transition callback
• ExperienceManager — IsOwner guard on SyncMaxHealthServerRpc
• EnemyReward — fix drop spawn during Editor stop-play teardown
• CursorManager — CursorMode.Auto
• PlayMode — 2-Players test scenario
• Regenerated Cinzel + JetBrains Mono SDF font atlases

<b><color=#e8920a>v0.2 — Spells, Skill Tree & Polish — In Progress</color></b>  <color=#7a7a9a>2026-04-12 – 13</color>
• HealingWave spell — AoE heal, server-authoritative
• Stun system (StatusEffectHandler) — blocks casts & movement
• PlayerNameTag — world-space billboard labels, NGO-synced
• Skill tree multi-level nodes — maxLevel, scalingFactor
• Skill tree stat bonuses — STR/AGI/INT flat + spell/fire/heal %
• WaveManager custom rewards — per-wave item drops
• PlayerController.All registry — GC-free player lookup
• Settings panel — master & music volume, saved via PlayerPrefs
• FPS overlay (F3) — smoothed FPS, ms, memory usage

<b><color=#3dba6e>v0.1 — Foundation</color></b>  <color=#7a7a9a>2026-04-03 – 11</color>
• Player movement, sprint, stamina, melee, fireball
• Enemy AI + spawner, wave manager, XP / leveling (STR / AGI / INT)
• Spell bar (10 slots), character window, inventory, damage popups
• Unity NGO integrated — Host / Join, lobby, spectator free-cam
• Player as NetworkObject with OwnerNetworkTransform
• Per-player ExperienceManager & PlayerInventory
• Networked enemy AI (server-authoritative) & LootPickup
• EnemyColorRandomizer, LobbyChatManager, NGO optimization
• SpellCaster state machine — PreCast, Pending, Channeling, Idle

── All assets are placeholders ──";

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
