using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Manages the player's Level, XP, and three core stats: STR, AGI, INT.
/// On level-up the player receives a stat point to spend freely in the
/// Character Stats panel (CharacterWindow) — the game never pauses.
/// Attach this to your Player GameObject.
/// </summary>
public class ExperienceManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static ExperienceManager Instance { get; private set; }

    // ─── XP Settings ─────────────────────────────────────────────────────────

    [Header("XP Settings")]
    public int currentLevel = 1;
    public int currentXP = 0;
    public int xpToNextLevel = 10;
    public float xpScalingFactor = 1.25f;

    // ─── Stats ────────────────────────────────────────────────────────────────

    [Header("Stats")]
    [Tooltip("Unspent stat points earned from levelling up.")]
    public int statPoints = 0;

    [Tooltip("STR — increases melee attack damage and max HP.")]
    public int strength = 1;

    [Tooltip("AGI — reduces attack cooldown and boosts move/sprint speed.")]
    public int agility = 1;

    [Tooltip("INT — each point adds spell damage.")]
    public int intelligence = 1;

    // ─── STR Tuning ───────────────────────────────────────────────────────────

    [Header("STR Tuning")]
    [Tooltip("Base minimum damage at STR 1.")]
    public float baseMinDamage = 1f;

    [Tooltip("Base maximum damage at STR 1.")]
    public float baseMaxDamage = 2f;

    [Tooltip("Flat min damage added per STR point above 1.")]
    public float minDamagePerStr = 0.5f;

    [Tooltip("Flat max damage added per STR point above 1.")]
    public float maxDamagePerStr = 1f;

    [Tooltip("Max HP gained per STR point spent.")]
    public int hpPerStr = 5;

    // ─── AGI Tuning ───────────────────────────────────────────────────────────

    // ─── Crit Stats ───────────────────────────────────────────────────────────

    [Header("Crit Stats")]
    [Tooltip("Base critical hit chance. (0.10 = 10%)")]
    public float baseCritRate = 0.10f;

    [Tooltip("Crit rate added per point spent. (0.02 = +2% per point)")]
    public float critRatePerPoint = 0.02f;

    [Tooltip("Base critical hit damage bonus. (0.50 = +50% damage)")]
    public float baseCritDamage = 0.50f;

    [Tooltip("Crit damage added per point spent. (0.10 = +10% per point)")]
    public float critDamagePerPoint = 0.10f;

    [Tooltip("Stat points spent on crit rate.")]
    public int critRatePoints = 0;

    [Tooltip("Stat points spent on crit damage.")]
    public int critDamagePoints = 0;

    // ─── AGI Tuning ───────────────────────────────────────────────────────────

    [Header("AGI Tuning")]
    [Tooltip("Move speed multiplier bonus per AGI point. (0.02 = +2% per point)")]
    public float agiMoveSpeedPct = 0.02f;

    [Tooltip("Sprint speed multiplier bonus per AGI point. (0.02 = +2% per point)")]
    public float agiSprintSpeedPct = 0.02f;

    [Tooltip("Attack cooldown reduction per AGI point. (0.015 = -1.5% per point)")]
    public float agiCooldownReductionPct = 0.015f;

    [Tooltip("Hard floor for attack cooldown regardless of AGI.")]
    public float agiMinAttackCooldown = 0.3f;

    // ─── INT Tuning ───────────────────────────────────────────────────────────

    [Header("INT Tuning")]
    [Tooltip("Spell damage multiplier bonus per INT point. (0.05 = +5% per point)")]
    public float intSpellDamagePct = 0.05f;

    // ─── HUD UI References ────────────────────────────────────────────────────

    [Header("HUD UI")]
    [Tooltip("Drag your Level label (TextMeshProUGUI) here.")]
    public TextMeshProUGUI levelText;

    [Tooltip("Drag your XP label (TextMeshProUGUI) here. (optional)")]
    public TextMeshProUGUI xpText;

    [Tooltip("Drag your STR label here.")]
    public TextMeshProUGUI strengthText;

    [Tooltip("Drag your AGI label here.")]
    public TextMeshProUGUI agilityText;

    [Tooltip("Drag your INT label here.")]
    public TextMeshProUGUI intelligenceText;

    [Tooltip("Drag your XP Slider here.")]
    public Slider xpBar;

    // ─── Audio ────────────────────────────────────────────────────────────────

    [Header("Audio")]
    [Tooltip("Sound played when the player levels up.")]
    public AudioClip levelUpClip;

    private AudioSource _audioSource;

    // ─── Events ──────────────────────────────────────────────────────────────

    /// <summary>Fired when the player levels up. Argument is the new level.</summary>
    public event System.Action<int> OnLevelUp;

    /// <summary>Fired whenever XP changes. Argument is fill ratio (0–1) for the XP bar.</summary>
    public event System.Action<float> OnXPChanged;

    // ─── Private State ────────────────────────────────────────────────────────

    private HealthSystem _playerHealth;
    private PlayerInventory _inventory;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();

        _playerHealth = GetComponent<HealthSystem>();
        if (_playerHealth == null)
            Debug.LogWarning("[ExperienceManager] No HealthSystem found on this GameObject.");

        // Cache own PlayerInventory — each player has their own, no singleton needed.
        _inventory = GetComponent<PlayerInventory>();
        if (_inventory != null)
            _inventory.OnInventoryChanged += OnEquipmentChanged;

        if (currentLevel > 1)
            InitializeStartingLevel();

        UpdateUI();
    }

    /// <summary>
    /// When currentLevel is set above 1 in the Inspector (e.g. for testing),
    /// simulates all preceding level-ups: scales xpToNextLevel correctly and
    /// grants the accumulated stat points and skill points.
    /// </summary>
    private void InitializeStartingLevel()
    {
        int levelsGained = currentLevel - 1;

        // Scale xpToNextLevel as if we levelled up (levelsGained) times from base
        for (int i = 0; i < levelsGained; i++)
            xpToNextLevel = Mathf.RoundToInt(xpToNextLevel * xpScalingFactor);

        statPoints += levelsGained;

        if (SkillTreeManager.Instance != null)
            for (int i = 0; i < levelsGained; i++)
                SkillTreeManager.Instance.AddSkillPoint();
        else
            Debug.LogWarning("[ExperienceManager] SkillTreeManager not found — skill points not granted for starting level.");

        Debug.Log($"[ExperienceManager] Initialized to level {currentLevel}: +{levelsGained} stat points, +{levelsGained} skill points, xpToNextLevel={xpToNextLevel}");
    }

    void OnDestroy()
    {
        if (_inventory != null)
            _inventory.OnInventoryChanged -= OnEquipmentChanged;

        // Clear singleton if this was the local instance
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Called by PlayerController after spawning the local player.
    /// Sets this component as the global Instance so UI and scene pickups
    /// can always reach the local player's XP/stats.
    /// </summary>
    public void SetAsLocalInstance()
    {
        Instance = this;
        Debug.Log("[ExperienceManager] Set as local instance.");
    }

    /// <summary>Called whenever the player equips, unequips, or removes an item.</summary>
    private void OnEquipmentChanged()
    {
        if (_playerHealth == null) return;

        int strHP  = EquipBonusSTR * hpPerStr;
        int flatHP = _inventory != null ? _inventory.TotalBonusHP : 0;
        _playerHealth.ApplyEquipmentHP(strHP + flatHP);
        SyncMaxHealthToServer();
    }

    /// <summary>
    /// Pushes the local player's current maxHealth to the server so server-side regen
    /// and healing checks stay in sync. No-op in singleplayer.
    /// </summary>
    private void SyncMaxHealthToServer()
    {
        if (_playerHealth == null) return;
        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (!networkActive) return;
        GetComponent<PlayerController>()?.SyncMaxHealthServerRpc(_playerHealth.maxHealth);
    }

    // ─── Public XP API ───────────────────────────────────────────────────────

    /// <summary>
    /// Award XP to the player. Grants a stat point for each level threshold crossed.
    /// </summary>
    public void GainXP(int amount)
    {
        currentXP += amount;
        DebugLogger.Log(DebugLogger.Category.XP, $"+{amount} XP  →  {currentXP}/{xpToNextLevel}");

        if (DamagePopupManager.Instance != null)
            DamagePopupManager.Instance.ShowXP(transform.position, amount);

        // Handle multiple level-ups from a single XP grant
        while (currentXP >= xpToNextLevel)
            TriggerLevelUp();

        UpdateUI();
    }

    // ─── Effective Stats (base + equipment bonuses) ───────────────────────────

    private int EquipBonusSTR => _inventory != null ? _inventory.TotalBonusSTR : 0;
    private int EquipBonusAGI => _inventory != null ? _inventory.TotalBonusAGI : 0;
    private int EquipBonusINT => _inventory != null ? _inventory.TotalBonusINT : 0;

    private int SkillTreeBonusSTR => SkillTreeManager.Instance != null ? SkillTreeManager.Instance.TotalStrBonus : 0;
    private int SkillTreeBonusAGI => SkillTreeManager.Instance != null ? SkillTreeManager.Instance.TotalAgiBonus : 0;
    private int SkillTreeBonusINT => SkillTreeManager.Instance != null ? SkillTreeManager.Instance.TotalIntBonus : 0;

    public int EffectiveSTR => strength     + EquipBonusSTR + SkillTreeBonusSTR;
    public int EffectiveAGI => agility      + EquipBonusAGI + SkillTreeBonusAGI;
    public int EffectiveINT => intelligence + EquipBonusINT + SkillTreeBonusINT;

    // ─── Computed Stat Properties ─────────────────────────────────────────────

    /// <summary>Move speed after AGI bonus (includes equipment). +agiMoveSpeedPct% per point.</summary>
    public float ComputedMoveSpeed(float baseMoveSpeed)
        => baseMoveSpeed * (1f + EffectiveAGI * agiMoveSpeedPct);

    /// <summary>Sprint speed after AGI bonus (includes equipment). +agiSprintSpeedPct% per point.</summary>
    public float ComputedSprintSpeed(float baseSprintSpeed)
        => baseSprintSpeed * (1f + EffectiveAGI * agiSprintSpeedPct);

    /// <summary>Attack cooldown after AGI reduction (includes equipment). Floored at agiMinAttackCooldown.</summary>
    public float ComputedAttackCooldown(float baseAttackCooldown)
        => Mathf.Max(agiMinAttackCooldown, baseAttackCooldown * (1f - EffectiveAGI * agiCooldownReductionPct));

    /// <summary>Spell damage multiplier from INT + skill tree % bonus (includes equipment). +intSpellDamagePct% per point.</summary>
    public float SpellDamageMultiplier
        => 1f + EffectiveINT * intSpellDamagePct
             + (SkillTreeManager.Instance != null ? SkillTreeManager.Instance.TotalSpellDamagePctBonus : 0f);

    /// <summary>Min damage using effective STR. Base at STR 1, +minDamagePerStr per point above.</summary>
    public int ComputedMinDamage => Mathf.Max(1, Mathf.RoundToInt(baseMinDamage + (EffectiveSTR - 1) * minDamagePerStr));

    /// <summary>Max damage using effective STR. Base at STR 1, +maxDamagePerStr per point above.</summary>
    public int ComputedMaxDamage => Mathf.Max(ComputedMinDamage, Mathf.RoundToInt(baseMaxDamage + (EffectiveSTR - 1) * maxDamagePerStr));

    /// <summary>Current crit rate (0–1). Base + stat points + equipment.</summary>
    public float ComputedCritRate
        => baseCritRate + critRatePoints * critRatePerPoint
           + (_inventory != null ? _inventory.TotalBonusCritRate : 0f);

    /// <summary>Current crit damage bonus (0–1+). Base + stat points + equipment.</summary>
    public float ComputedCritDamage
        => baseCritDamage + critDamagePoints * critDamagePerPoint
           + (_inventory != null ? _inventory.TotalBonusCritDamage : 0f);

    // ─── Level-Up Flow ────────────────────────────────────────────────────────

    private void TriggerLevelUp()
    {
        currentXP -= xpToNextLevel;
        currentLevel++;
        xpToNextLevel = Mathf.RoundToInt(xpToNextLevel * xpScalingFactor);
        statPoints++;
        OnLevelUp?.Invoke(currentLevel);

        DebugLogger.Log(DebugLogger.Category.XP,
            $"LEVEL UP → Level {currentLevel}! +1 stat point ({statPoints} unspent). Next level: {xpToNextLevel} XP.");

        if (_audioSource != null && levelUpClip != null)
            _audioSource.PlayOneShot(levelUpClip);

        if (SkillTreeManager.Instance != null)
            SkillTreeManager.Instance.AddSkillPoint();
        else
            Debug.LogWarning("[ExperienceManager] SkillTreeManager not found — skill point not awarded.");
    }

    // ─── Stat Spend API (called by CharacterWindow buttons) ──────────────────

    /// <summary>Spend one stat point on Strength. Returns true if successful.</summary>
    public bool SpendOnSTR()
    {
        if (statPoints <= 0) return false;
        statPoints--;
        strength++;
        if (_playerHealth != null)
        {
            _playerHealth.IncreaseMaxHealth(hpPerStr);
            SyncMaxHealthToServer();
        }
        Debug.Log($"[STAT] +1 STR → {strength} (dmg {ComputedMinDamage}-{ComputedMaxDamage}, +{hpPerStr} HP)  |  {statPoints} points remaining");
        UpdateUI();
        return true;
    }

    /// <summary>Spend one stat point on Agility. Returns true if successful.</summary>
    public bool SpendOnAGI()
    {
        if (statPoints <= 0) return false;
        statPoints--;
        agility++;
        Debug.Log($"[STAT] +1 AGI → {agility}  |  {statPoints} points remaining");
        UpdateUI();
        return true;
    }

    /// <summary>Spend one stat point on Crit Rate. Returns true if successful.</summary>
    public bool SpendOnCritRate()
    {
        if (statPoints <= 0) return false;
        statPoints--;
        critRatePoints++;
        Debug.Log($"[STAT] +Crit Rate → {ComputedCritRate * 100f:F0}%  |  {statPoints} points remaining");
        UpdateUI();
        return true;
    }

    /// <summary>Spend one stat point on Crit Damage. Returns true if successful.</summary>
    public bool SpendOnCritDamage()
    {
        if (statPoints <= 0) return false;
        statPoints--;
        critDamagePoints++;
        Debug.Log($"[STAT] +Crit Damage → {ComputedCritDamage * 100f:F0}%  |  {statPoints} points remaining");
        UpdateUI();
        return true;
    }

    /// <summary>Spend one stat point on Intelligence. Returns true if successful.</summary>
    public bool SpendOnINT()
    {
        if (statPoints <= 0) return false;
        statPoints--;
        intelligence++;
        Debug.Log($"[STAT] +1 INT → {intelligence}  (×{SpellDamageMultiplier:F2} spell dmg)  |  {statPoints} points remaining");
        UpdateUI();
        return true;
    }

    // ─── UI Update ────────────────────────────────────────────────────────────

    /// <summary>Called by PlayerUILinker after wiring UI references. Forces a full HUD refresh.</summary>
    public void RefreshXPBar() => UpdateUI();

    private void UpdateUI()
    {
        if (levelText != null)
            levelText.text = $"Level {currentLevel}";

        if (xpText != null)
            xpText.text = $"{currentXP} / {xpToNextLevel} XP";

        float ratio = xpToNextLevel > 0 ? (float)currentXP / xpToNextLevel : 0f;

        if (xpBar != null)
            xpBar.value = ratio;

        // Notify any external listeners (e.g. PlayerUILinker) even if xpBar is null here.
        OnXPChanged?.Invoke(ratio);

        if (strengthText != null)
            strengthText.text = $"STR: {strength}";

        if (agilityText != null)
            agilityText.text = $"AGI: {agility}";

        if (intelligenceText != null)
            intelligenceText.text = $"INT: {intelligence}";
    }
}
