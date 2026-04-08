using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealthSystem : MonoBehaviour
{
    // ─── Health Settings ──────────────────────────────────────────────────────
    [Header("Health Settings")]
    public int maxHealth = 5;
    public int currentHealth;

    // ─── UI ───────────────────────────────────────────────────────────────────
    [Header("UI (optional — leave blank for enemies)")]
    [Tooltip("Optional text label showing HP as numbers.")]
    public TMP_Text healthText;

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

    // ─── Death Settings ───────────────────────────────────────────────────────
    [Header("Death Settings")]
    [Tooltip("If true, destroys the GameObject on death. Use for enemies.")]
    public bool destroyOnDeath = false;

    [Tooltip("Seconds before the object is destroyed after death.")]
    public float deathDelay = 0.5f;

    // ─── Private State ────────────────────────────────────────────────────────

    /// <summary>HP from the Inspector + all STR level-up spends. Never includes equipment.</summary>
    private int _permanentMaxHealth;

    private bool _isDead = false;
    private float _regenTimer = 0f;
    private PlayerInventory _inventory;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    void Start()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        // Cache own PlayerInventory — each player has their own, no singleton needed.
        _inventory = GetComponent<PlayerInventory>();

        _permanentMaxHealth = maxHealth;
        currentHealth = maxHealth;
        UpdateHealthUI();
    }

    // ─── Unity Lifecycle (regen tick) ─────────────────────────────────────────

    void Update()
    {
        if (!CompareTag("Player")) return;
        if (currentHealth >= maxHealth) return;
        if (_inventory == null) return;

        float regen = _inventory.TotalBonusRegen;
        if (regen <= 0f) return;

        _regenTimer += Time.deltaTime;
        if (_regenTimer >= 1f)
        {
            _regenTimer -= 1f;
            Heal(Mathf.RoundToInt(regen));
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────
    public void TakeDamage(int amount, bool isCrit = false)
    {
        if (_isDead) return;

        currentHealth -= amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        UpdateHealthUI();

        Debug.Log(gameObject.name + " took " + amount + " damage! HP: " + currentHealth + "/" + maxHealth);

        // ── Hit Sound ─────────────────────────────────────────────────────────
        if (audioSource != null && hitSound != null)
            audioSource.PlayOneShot(hitSound);

        // ── Damage Popup ──────────────────────────────────────────────────────
        if (DamagePopupManager.Instance != null)
        {
            bool isPlayer = CompareTag("Player");
            DamagePopupManager.Instance.ShowDamage(transform.position, amount, isPlayer, isCrit);
        }

        if (currentHealth <= 0)
            Die();
    }

    public void Heal(int amount)
    {
        int before = currentHealth;
        currentHealth += amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        int gained = currentHealth - before;

        // Revive dead state — happens when the respawn flow heals the player back
        // above 0. Without this, _isDead stays true after respawn and all subsequent
        // TakeDamage calls are silently ignored.
        if (_isDead && currentHealth > 0)
            _isDead = false;

        UpdateHealthUI();

        Debug.Log(gameObject.name + " healed " + gained + " HP! HP: " + currentHealth + "/" + maxHealth);

        if (gained > 0 && DamagePopupManager.Instance != null)
            DamagePopupManager.Instance.ShowHeal(transform.position, gained);
    }

    /// <summary>Permanently increases max HP (called when spending a STR stat point). Does NOT heal current HP.</summary>
    public void IncreaseMaxHealth(int amount)
    {
        _permanentMaxHealth += amount;
        maxHealth           += amount;
        UpdateHealthUI();

        Debug.Log(gameObject.name + " max HP increased by " + amount + ". HP: " + currentHealth + "/" + maxHealth);
    }

    /// <summary>
    /// Recalculates max HP from equipment STR bonus. Called whenever inventory changes.
    /// Does NOT permanently change HP — unequipping restores the previous max.
    /// </summary>
    public void ApplyEquipmentHP(int equipmentBonus)
    {
        int newMax = _permanentMaxHealth + equipmentBonus;
        maxHealth  = newMax;

        // Only clamp down if current HP now exceeds the new max (e.g. unequipping a high-HP item)
        currentHealth = Mathf.Clamp(currentHealth, 1, maxHealth);
        UpdateHealthUI();

        Debug.Log($"[HealthSystem] Equipment HP bonus applied: +{equipmentBonus} → maxHP {maxHealth}");
    }

    // ─── Internal ─────────────────────────────────────────────────────────────
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
            if (ai != null) ai.enabled = false;

            Destroy(gameObject, deathDelay);
        }
        else if (CompareTag("Player"))
        {
            if (WaveManager.Instance != null)
                WaveManager.Instance.OnPlayerDeath();

            if (DeathScreenManager.Instance != null)
                DeathScreenManager.Instance.ShowDeathScreen();
            else
                Debug.LogWarning("[HealthSystem] Player died but no DeathScreenManager found in scene.");
        }
    }

    /// <summary>
    /// Called by PlayerUILinker after it wires the UI references at runtime.
    /// Forces an immediate bar refresh so the first frame shows the correct value.
    /// </summary>
    public void RefreshUI() => UpdateHealthUI();

    void UpdateHealthUI()
    {
        if (healthText != null)
            healthText.text = "HP: " + currentHealth + "/" + maxHealth;

        if (hpBarFill != null)
            hpBarFill.fillAmount = (float)currentHealth / maxHealth;
    }
}