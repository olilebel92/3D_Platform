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

<b><color=#e8920a>v0.2 — Spells, Skill Tree & Polish</color></b>  <color=#7a7a9a>2026-04-12 – 13 — Latest</color>
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
