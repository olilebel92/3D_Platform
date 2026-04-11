using UnityEngine;
using TMPro;

/// <summary>
/// Attach to any GameObject that has a TextMeshProUGUI component.
/// The devlog text is edited directly in this file by Claude when asked.
/// Call RefreshLog() from the Inspector button or on Start to push the text to the TMP component.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class DevLog : MonoBehaviour
{
    // ─── Settings ─────────────────────────────────────────────────────────────

    [Header("Display")]
    [Tooltip("If true, the log is pushed to the TMP component automatically on Start.")]
    [SerializeField] private bool showOnStart = true;

    // ─── Log Text ─────────────────────────────────────────────────────────────

    // Claude updates this string when asked to update the log.
    private const string LOG_TEXT =
@"<b><color=#c9a84c>── HACKNSLASH DEVLOG ──</color></b>

<b><color=#e8920a>v0.06 — Spell System Overhaul</color></b>  <color=#7a7a9a>2026-04-09 — In Progress</color>
• SpellCaster 4-state machine — PreCast, Pending, Channeling, Idle
• Spell types: Cast, Buff, Aura, Channel — defined per SpellData
• Interrupt rules — movement & damage grace windows; stun always cancels
• StatusEffectHandler — stun system, blocks casts and movement
• PlayerNameTag — world-space billboard label, synced via NGO
• SpellCastBarUI event-driven — cast (orange) and channel (blue)

<b><color=#3dba6e>v0.05 — Networking Polish</color></b>  <color=#7a7a9a>2026-04-07</color>
• EnemyColorRandomizer — random hue per enemy on spawn
• LobbyChatManager — real-time chat in the lobby (NGO synced)
• NGO optimization pass — reduced unnecessary RPCs and NetworkVariable updates
• Local networking polish — WIP

<b><color=#3dba6e>v0.04 — Networked Enemy AI</color></b>  <color=#7a7a9a>2026-04-06</color>
• EnemyAI & EnemyReward promoted to NetworkBehaviour
• Server-authoritative AI — all logic runs host-side only
• Enemies retarget nearest player every interval (multi-player)
• LootPickup replaces CoinPickup & ItemPickup — XP, HP, Mana, Items
• Pickups use CollectServerRpc — no double-collect in co-op
• NetworkObject.Despawn() replaces Destroy() on pickups

<b><color=#3dba6e>v0.03 — Co-op Player Prefab</color></b>  <color=#7a7a9a>2026-04-05</color>
• Player converted to spawnable NetworkObject prefab
• OwnerNetworkTransform — client-authoritative movement
• Per-player ExperienceManager & PlayerInventory (no global singletons)
• PlayerUILinker wires HP / XP / Stamina bars after NGO spawn
• Death screen Respawn calls ServerRpc — no scene reload in MP
• Spectator class added to lobby

<b><color=#3dba6e>v0.02 — Networking Foundation</color></b>  <color=#7a7a9a>2026-04-04</color>
• Unity Netcode for GameObjects (NGO) integrated
• Host / Join flow from Main Menu
• Lobby scene — class select, ready system, Force Start
• Spectator free-cam mode

<b><color=#3dba6e>v0.01 — First Build</color></b>  <color=#7a7a9a>2026-04-03</color>
• Player movement, sprint, stamina, melee, fireball
• Enemy AI + spawner, wave manager
• XP / leveling — STR / AGI / INT stats
• Spell bar (10 slots), character window
• Inventory, item drops, damage popups

<color=#7a7a9a>── All assets are placeholders ──</color>";

    // ─── Runtime ──────────────────────────────────────────────────────────────

    private TMP_Text _tmp;

    private void Awake()
    {
        _tmp = GetComponent<TMP_Text>();
    }

    private void Start()
    {
        if (showOnStart) RefreshLog();
    }

    /// <summary>Call this (or press the button in the Inspector) to push the log text to the TMP component.</summary>
    [ContextMenu("Refresh Log")]
    public void RefreshLog()
    {
        if (_tmp == null) _tmp = GetComponent<TMP_Text>();
        _tmp.text = LOG_TEXT;
    }
}
