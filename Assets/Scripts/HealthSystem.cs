using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Server-authoritative health for any damageable entity (players, enemies, traps).
/// HP and Max HP live in NetworkVariables when NGO is listening, and in local fields
/// when running solo. TakeDamage / Heal auto-route to the server via ServerRpc when
/// called from a client, so existing call sites do not need to know about authority.
/// </summary>
public class HealthSystem : NetworkBehaviour
{
    // ─── Health Settings ──────────────────────────────────────────────────────
    [Header("Health Settings")]
    [Tooltip("Initial maximum HP. Set per prefab.")]
    [SerializeField, FormerlySerializedAs("maxHealth")] private float _inspectorMaxHealth = 5f;

    [Tooltip("Base HP restored per second (server-side only). Stacks with equipment regen.")]
    [SerializeField] private float regenPerSecond = 1f;

    // ─── UI ───────────────────────────────────────────────────────────────────
    [Header("UI (optional — leave blank for enemies)")]
    [Tooltip("Fill Image for the HP bar. Set Image Type to Filled, Fill Method to Horizontal.")]
    public Image hpBarFill;

    [Tooltip("Root panel of the HP bar — shown/hidden like the stamina bar.")]
    public GameObject hpBarPanel;

    // ─── Audio ────────────────────────────────────────────────────────────────
    [Header("Audio")]
    [Tooltip("Sound played when this character takes damage.")]
    public AudioClip hitSound;

    [Tooltip("AudioSource used to play hit sounds. Auto-found if blank.")]
    public AudioSource audioSource;

    [Tooltip("Minimum pitch multiplier for hit sound.")]
    [SerializeField] private float hitPitchMin = 0.9f;
    [Tooltip("Maximum pitch multiplier for hit sound.")]
    [SerializeField] private float hitPitchMax = 1.1f;

    // ─── Death Settings ───────────────────────────────────────────────────────
    [Header("Death Settings")]
    [Tooltip("If true, destroys the GameObject on death. Use for enemies.")]
    public bool destroyOnDeath = false;

    [Tooltip("Seconds before the object is destroyed after death.")]
    public float deathDelay = 0.5f;

    [Tooltip("If true, colliders are NOT disabled on death. Set automatically for bosses.")]
    public bool keepColliderOnDeath = false;

    // ─── Events ───────────────────────────────────────────────────────────────
    /// <summary>Fired when currentHealth drops to 0, before the destroy delay. Server-side only.</summary>
    public System.Action OnDeath;

    /// <summary>Fired on every machine when HP changes (NV broadcast in MP, direct in solo).</summary>
    public event Action<float, float> OnHealthChanged;

    // ─── Networked Storage ────────────────────────────────────────────────────
    private NetworkVariable<float> _netCurrent = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<float> _netMax = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ─── Solo Storage (no NGO) ────────────────────────────────────────────────
    private float _soloCurrent;
    private float _soloMax;

    // ─── State ────────────────────────────────────────────────────────────────
    /// <summary>HP from the Inspector + all STR level-up spends. Never includes equipment.</summary>
    private float _permanentMaxHealth;

    private bool _isDead = false;
    private PlayerInventory _inventory;
    private float _skillTreeRegen = 0f;

    // ─── Facade ───────────────────────────────────────────────────────────────
    /// <summary>True only after OnNetworkSpawn has run on a live NGO instance.</summary>
    private bool IsNetworked => NetworkManager.Singleton != null
                              && NetworkManager.Singleton.IsListening
                              && IsSpawned;

    public float currentHealth => IsNetworked ? _netCurrent.Value : _soloCurrent;
    public float maxHealth     => IsNetworked ? _netMax.Value     : _soloMax;

    public float TotalRegenPerSecond =>
        regenPerSecond + (_inventory != null ? _inventory.TotalBonusHPRegen : 0f) + _skillTreeRegen;

    public float RegenPerSecond => regenPerSecond;

    /// <summary>
    /// Called by ExperienceManager when the skill tree changes. Sets the local field
    /// for CharacterWindow display and forwards to the server so HpRegenTick (server-side)
    /// also applies the bonus.
    /// </summary>
    public void ApplySkillTreeRegen(float bonus)
    {
        _skillTreeRegen = bonus;
        if (IsNetworked && !IsServer)
            ApplySkillTreeRegenRpc(bonus);
    }

    [Rpc(SendTo.Server)]
    void ApplySkillTreeRegenRpc(float bonus)
    {
        _skillTreeRegen = bonus;
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        _soloMax            = _inspectorMaxHealth;
        _soloCurrent        = _inspectorMaxHealth;
        _permanentMaxHealth = _inspectorMaxHealth;
    }

    void Start()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        _inventory = GetComponent<PlayerInventory>();
        UpdateHealthUI();
    }

    // ─── NGO Lifecycle ────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Server seeds the NVs from whatever the solo storage currently holds. This
        // preserves any state mutated between Awake and OnNetworkSpawn (e.g. an
        // EnemyAI.ApplyData or WaveManager.ApplyDifficultyScaling that fired before
        // spawn completed).
        if (IsServer)
        {
            _netMax.Value     = _soloMax;
            _netCurrent.Value = _soloCurrent;
        }

        _netCurrent.OnValueChanged += OnNetCurrentChanged;
        _netMax.OnValueChanged     += OnNetMaxChanged;

        UpdateHealthUI();
    }

    public override void OnNetworkDespawn()
    {
        _netCurrent.OnValueChanged -= OnNetCurrentChanged;
        _netMax.OnValueChanged     -= OnNetMaxChanged;
        base.OnNetworkDespawn();
    }

    void OnNetCurrentChanged(float prev, float current)
    {
        UpdateHealthUI();
        OnHealthChanged?.Invoke(prev, current);
    }

    void OnNetMaxChanged(float prev, float current)
    {
        UpdateHealthUI();
    }

    // ─── Storage Helpers ──────────────────────────────────────────────────────

    void WriteCurrentHealth(float value)
    {
        value = Mathf.Clamp(value, 0f, maxHealth);
        if (IsNetworked)
        {
            _netCurrent.Value = value;
        }
        else
        {
            float prev   = _soloCurrent;
            _soloCurrent = value;
            OnHealthChanged?.Invoke(prev, value);
            UpdateHealthUI();
        }
    }

    void WriteMaxHealth(float value)
    {
        value = Mathf.Max(0f, value);
        if (IsNetworked)
        {
            _netMax.Value = value;
        }
        else
        {
            _soloMax = value;
            UpdateHealthUI();
        }
    }

    // ─── Public API — Damage ──────────────────────────────────────────────────

    /// <summary>
    /// Apply damage. Auto-routes through ServerRpc when called from a client in MP.
    /// In solo or on the server, applies immediately and broadcasts feedback.
    /// </summary>
    public void TakeDamage(int amount, bool isCrit = false)
    {
        // Client in MP: forward to server. The server-side call broadcasts feedback
        // back to every machine, including this one — popup and sound arrive ~1 RTT later.
        if (IsNetworked && !IsServer)
        {
            TakeDamageServerRpc(amount, isCrit);
            return;
        }

        if (_isDead) return;

        float newHealth = Mathf.Clamp(currentHealth - amount, 0f, maxHealth);
        WriteCurrentHealth(newHealth);

        DebugLogger.Log(DebugLogger.Category.Damage,
            $"{gameObject.name} took {amount} damage — HP: {currentHealth}/{maxHealth}");

        if (IsNetworked)
            ShowDamageFeedbackClientRpc(amount, isCrit);
        else
            ShowDamageFeedbackLocal(amount, isCrit);

        if (currentHealth <= 0f)
            Die();
    }

    [Rpc(SendTo.Server)]
    public void TakeDamageServerRpc(int amount, bool isCrit)
    {
        TakeDamage(amount, isCrit);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void ShowDamageFeedbackClientRpc(int amount, bool isCrit)
    {
        ShowDamageFeedbackLocal(amount, isCrit);
    }

    void ShowDamageFeedbackLocal(int amount, bool isCrit)
    {
        if (audioSource != null && hitSound != null)
        {
            audioSource.pitch = UnityEngine.Random.Range(hitPitchMin, hitPitchMax);
            audioSource.PlayOneShot(hitSound);
        }

        if (DamagePopupManager.Instance != null)
        {
            bool isPlayer = CompareTag("Player");
            DamagePopupManager.Instance.ShowDamage(transform.position, amount, isPlayer, isCrit);
        }
    }

    // ─── Public API — Heal ────────────────────────────────────────────────────

    /// <param name="suppressPopup">
    /// When true, the heal popup is suppressed. Use this from regen ticks or when the
    /// caller will fire its own popup via a separate ClientRpc.
    /// </param>
    public void Heal(float amount, bool suppressPopup = false)
    {
        if (IsNetworked && !IsServer)
        {
            HealServerRpc(amount, suppressPopup);
            return;
        }

        float before    = currentHealth;
        float newHealth = Mathf.Clamp(before + amount, 0f, maxHealth);
        WriteCurrentHealth(newHealth);
        float gained = newHealth - before;

        // Revive dead state — happens when the respawn flow heals the player back
        // above 0. Without this, _isDead stays true after respawn and all subsequent
        // TakeDamage calls are silently ignored.
        if (_isDead && newHealth > 0f)
            _isDead = false;

        if (gained > 0f)
            DebugLogger.Log(DebugLogger.Category.Heal,
                $"{gameObject.name} healed +{gained} — HP: {currentHealth}/{maxHealth}");

        if (gained > 0f && !suppressPopup)
        {
            int amountInt = Mathf.RoundToInt(gained);
            if (IsNetworked)
                ShowHealPopupOwnerRpc(amountInt);
            else
                ShowHealPopupLocal(amountInt);
        }
    }

    [Rpc(SendTo.Server)]
    public void HealServerRpc(float amount, bool suppressPopup)
    {
        Heal(amount, suppressPopup);
    }

    [Rpc(SendTo.Owner)]
    void ShowHealPopupOwnerRpc(int amount)
    {
        ShowHealPopupLocal(amount);
    }

    void ShowHealPopupLocal(int amount)
    {
        if (DamagePopupManager.Instance != null)
            DamagePopupManager.Instance.ShowHeal(transform.position, amount);
    }

    // ─── Server-Only Init / Set Helpers ───────────────────────────────────────

    /// <summary>
    /// Set both max and current HP in one call. Used by EnemyAI.ApplyData and
    /// WaveManager.ApplyDifficultyScaling at spawn time, and by GodMode.
    /// Auto-routes to the server in MP.
    /// </summary>
    public void InitializeServerHP(float max, float current)
    {
        if (IsNetworked && !IsServer)
        {
            InitializeServerHPRpc(max, current);
            return;
        }
        WriteMaxHealth(max);
        WriteCurrentHealth(Mathf.Clamp(current, 0f, max));
        _permanentMaxHealth = max;
    }

    [Rpc(SendTo.Server)]
    void InitializeServerHPRpc(float max, float current)
    {
        InitializeServerHP(max, current);
    }

    /// <summary>
    /// Set max HP only (clamping current down if it now exceeds the new cap).
    /// Server-only — used by GodMode and any future tooling that needs raw max-HP edits.
    /// </summary>
    public void SetMaxHealthServer(float newMax)
    {
        if (IsNetworked && !IsServer)
        {
            Debug.LogWarning($"[HealthSystem] SetMaxHealthServer called on a client for {gameObject.name} — ignored.");
            return;
        }
        WriteMaxHealth(newMax);
        if (currentHealth > newMax)
            WriteCurrentHealth(newMax);
    }

    /// <summary>
    /// Permanently increases max HP (called when spending a STR stat point). Does NOT heal current HP.
    /// Auto-routes to the server in MP — ExperienceManager runs on the owner client.
    /// </summary>
    public void IncreaseMaxHealth(float amount)
    {
        if (IsNetworked && !IsServer)
        {
            IncreaseMaxHealthRpc(amount);
            return;
        }
        _permanentMaxHealth += amount;
        WriteMaxHealth(maxHealth + amount);
        Debug.Log(gameObject.name + " max HP increased by " + amount + ". HP: " + currentHealth + "/" + maxHealth);
    }

    [Rpc(SendTo.Server)]
    void IncreaseMaxHealthRpc(float amount)
    {
        IncreaseMaxHealth(amount);
    }

    /// <summary>
    /// Recalculates max HP from equipment STR bonus. Called whenever inventory changes.
    /// Does NOT permanently change HP — unequipping restores the previous max.
    /// Auto-routes to the server in MP — ExperienceManager runs on the owner client.
    /// </summary>
    public void ApplyEquipmentHP(float equipmentBonus)
    {
        if (IsNetworked && !IsServer)
        {
            ApplyEquipmentHPRpc(equipmentBonus);
            return;
        }
        float newMax = _permanentMaxHealth + equipmentBonus;
        WriteMaxHealth(newMax);
        // Only clamp down if current HP now exceeds the new max (e.g. unequipping a high-HP item)
        if (currentHealth > newMax)
            WriteCurrentHealth(Mathf.Max(1f, newMax));

        Debug.Log($"[HealthSystem] Equipment HP bonus applied: +{equipmentBonus} → maxHP {maxHealth}");
    }

    [Rpc(SendTo.Server)]
    void ApplyEquipmentHPRpc(float equipmentBonus)
    {
        ApplyEquipmentHP(equipmentBonus);
    }

    // ─── Death ────────────────────────────────────────────────────────────────

    void Die()
    {
        if (_isDead) return;
        _isDead = true;

        Debug.Log(gameObject.name + " died!");

        if (destroyOnDeath)
        {
            // Guard: NetworkObjects must only be destroyed by the server.
            // If this runs on a non-server client it means damage was applied
            // locally by mistake — log a warning and bail out.
            var netObj = GetComponent<Unity.Netcode.NetworkObject>();
            bool networkActive = Unity.Netcode.NetworkManager.Singleton != null
                              && Unity.Netcode.NetworkManager.Singleton.IsListening;
            if (networkActive && netObj != null && netObj.IsSpawned
                && !Unity.Netcode.NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning($"[HealthSystem] Die() blocked — {gameObject.name} is a " +
                                 "NetworkObject and can only be destroyed by the server.");
                return;
            }

            EnemyAI ai = GetComponent<EnemyAI>();
            if (ai != null)
            {
                ai.PlayDeathAnimation();
                ai.enabled = false;
            }

            if (!keepColliderOnDeath)
            {
                foreach (var col in GetComponents<Collider>())
                    col.enabled = false;
            }

            OnDeath?.Invoke();

            Destroy(gameObject, deathDelay);
        }
        else if (CompareTag("Player"))
        {
            if (WaveManager.Instance != null)
                WaveManager.Instance.OnPlayerDeath();

            // Death screen must appear on the dying player's machine — not the host.
            // In MP, target the owner; in solo, show locally.
            if (IsNetworked)
            {
                ShowDeathScreenOwnerRpc();
            }
            else if (DeathScreenManager.Instance != null)
            {
                DeathScreenManager.Instance.ShowDeathScreen();
            }
            else
            {
                Debug.LogWarning("[HealthSystem] Player died but no DeathScreenManager found in scene.");
            }
        }
    }

    [Rpc(SendTo.Owner)]
    void ShowDeathScreenOwnerRpc()
    {
        if (DeathScreenManager.Instance != null)
            DeathScreenManager.Instance.ShowDeathScreen();
        else
            Debug.LogWarning("[HealthSystem] Player died but no DeathScreenManager found in scene.");
    }

    /// <summary>Called by PlayerUILinker after it wires the UI references at runtime.</summary>
    public void RefreshUI() => UpdateHealthUI();

    void UpdateHealthUI()
    {
        if (hpBarFill != null)
            hpBarFill.fillAmount = maxHealth > 0f ? currentHealth / maxHealth : 0f;
    }
}
