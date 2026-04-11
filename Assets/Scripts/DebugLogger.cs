using UnityEngine;

/// <summary>
/// Centralised, toggleable debug logger.
/// Add this component to a GameObject in your scene to enable logging.
/// If the GameObject is absent (e.g. in a release build), all log calls
/// are no-ops — no code changes needed elsewhere.
///
/// Toggle categories at runtime via the Inspector while the game is running.
/// </summary>
public class DebugLogger : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static DebugLogger Instance { get; private set; }

    // ─── Categories ───────────────────────────────────────────────────────────

    public enum Category
    {
        Damage,
        Heal,
        Regen,
        HealingWave,
        SpellCast,
        XP,
        NetworkSync,
    }

    // ─── Toggles ──────────────────────────────────────────────────────────────

    [Header("Health")]
    [Tooltip("Log every damage hit (TakeDamage calls on players and enemies).")]
    [SerializeField] private bool logDamage = true;

    [Tooltip("Log every heal event (HealingWave, ApplyHealClientRpc, etc.).")]
    [SerializeField] private bool logHeal = true;

    [Tooltip("Log passive HP regen ticks (can be noisy — disabled by default).")]
    [SerializeField] private bool logRegen = false;

    [Header("Spells")]
    [Tooltip("Log HealingWave tick results (how many players healed, total HP restored).")]
    [SerializeField] private bool logHealingWave = true;

    [Tooltip("Log spell cast / channel start / channel end events.")]
    [SerializeField] private bool logSpellCast = false;

    [Header("Progression")]
    [Tooltip("Log XP gains and level-ups.")]
    [SerializeField] private bool logXP = false;

    [Header("Network")]
    [Tooltip("Log server↔client health sync RPCs (SyncServerHealthServerRpc, SyncRegenHealClientRpc).")]
    [SerializeField] private bool logNetworkSync = false;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Logs <paramref name="message"/> only if the matching category toggle is enabled.
    /// Safe to call when DebugLogger is not in the scene — returns silently.
    /// </summary>
    public static void Log(Category category, string message)
    {
        if (Instance == null) return;

        bool enabled = category switch
        {
            Category.Damage       => Instance.logDamage,
            Category.Heal         => Instance.logHeal,
            Category.Regen        => Instance.logRegen,
            Category.HealingWave  => Instance.logHealingWave,
            Category.SpellCast    => Instance.logSpellCast,
            Category.XP           => Instance.logXP,
            Category.NetworkSync  => Instance.logNetworkSync,
            _                     => false,
        };

        if (enabled)
            Debug.Log($"[{category}] {message}");
    }
}
