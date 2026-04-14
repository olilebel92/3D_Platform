using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Displays a character stats window when the player presses C.
/// Reads live values from HealthSystem, StaminaSystem, and ExperienceManager.
/// Players can spend unspent stat points on STR, AGI, or INT directly here.
///
/// Setup:
///   1. Create a Canvas panel for the window and assign it to windowRoot.
///   2. Add TextMeshProUGUI labels and wire them in the Inspector.
///   3. Add three "+" Buttons and wire btnSpendSTR / btnSpendAGI / btnSpendINT.
///   4. Optionally assign playerHealthSystem and playerStaminaSystem,
///      or let it auto-find by the "Player" tag.
/// </summary>
public class CharacterWindow : MonoBehaviour
{
    // ─── Inspector — Panel Root ───────────────────────────────────────────────

    [Header("Window Root")]
    [Tooltip("The top-level panel GameObject to show/hide.")]
    public GameObject windowRoot;

    // ─── Inspector — Player Component References ──────────────────────────────

    [Header("Player References (auto-found if blank)")]
    [Tooltip("HealthSystem on the Player. Leave blank to auto-find by 'Player' tag.")]
    public HealthSystem playerHealthSystem;

    [Tooltip("StaminaSystem on the Player. Leave blank to auto-find by 'Player' tag.")]
    public StaminaSystem playerStaminaSystem;

    // ─── Inspector — Stat Labels ──────────────────────────────────────────────

    [Header("Stat Labels — drag your TMP labels here")]
    [Tooltip("Shows the character's current level.")]
    public TextMeshProUGUI levelLabel;

    [Tooltip("Shows current XP and XP required for next level.")]
    public TextMeshProUGUI xpLabel;

    [Tooltip("Shows unspent stat points available to spend.")]
    public TextMeshProUGUI statPointsLabel;

    [Tooltip("Shows current and max HP.")]
    public TextMeshProUGUI hpLabel;

    [Tooltip("Shows max stamina pool.")]
    public TextMeshProUGUI staminaLabel;

    [Tooltip("Displays total HP regeneration per second (base + equipment bonus).")]
    public TextMeshProUGUI regenLabel;

    [Tooltip("Shows the Strength stat.")]
    public TextMeshProUGUI strengthLabel;

    [Tooltip("Shows the Agility stat.")]
    public TextMeshProUGUI agilityLabel;

    [Tooltip("Shows the Intelligence stat.")]
    public TextMeshProUGUI intelligenceLabel;

    [Tooltip("Shows the XP scaling factor (how fast XP requirements grow).")]
    public TextMeshProUGUI xpScalingLabel;

    [Tooltip("Shows attack damage (sourced from PlayerAttack if present).")]
    public TextMeshProUGUI attackDamageLabel;

    [Tooltip("Shows attack radius (sourced from PlayerAttack if present).")]
    public TextMeshProUGUI attackRadiusLabel;

    [Tooltip("Shows attack speed as a percentage of base (AGI-scaled).")]
    public TextMeshProUGUI attackSpeedLabel;

    [Tooltip("Shows spell damage as a percentage of base (INT-scaled).")]
    public TextMeshProUGUI spellDamageLabel;

    [Tooltip("Shows crit rate as a percentage.")]
    public TextMeshProUGUI critRateLabel;

    [Tooltip("Shows crit damage bonus as a percentage.")]
    public TextMeshProUGUI critDamageLabel;

    [Tooltip("Shows move speed (sourced from PlayerController if present).")]
    public TextMeshProUGUI moveSpeedLabel;

    [Tooltip("Shows sprint speed (sourced from PlayerController if present).")]
    public TextMeshProUGUI sprintSpeedLabel;

    // ─── Inspector — Stat Spend Buttons ──────────────────────────────────────

    [Header("Stat Spend Buttons")]
    [Tooltip("'+' button that spends one stat point on STR.")]
    public Button btnSpendSTR;

    [Tooltip("'+' button that spends one stat point on AGI.")]
    public Button btnSpendAGI;

    [Tooltip("'+' button that spends one stat point on INT.")]
    public Button btnSpendINT;

    [Tooltip("'+' button that spends one stat point on Crit Rate.")]
    public Button btnSpendCritRate;

    [Tooltip("'+' button that spends one stat point on Crit Damage.")]
    public Button btnSpendCritDamage;

    // ─── Inspector — Optional XP Bar ─────────────────────────────────────────

    [Header("Optional XP Progress Bar")]
    [Tooltip("A Slider used as a mini XP bar inside the character window.")]
    public Slider xpProgressBar;

    // ─── Inspector — Cursor Behaviour ────────────────────────────────────────

    [Header("Cursor")]
    [Tooltip("Unlock and show the cursor while the character window is open.")]
    public bool unlockCursorWhileOpen = true;

    [Header("Pause")]
    [Tooltip("Pause the game (Time.timeScale = 0) while this window is open.")]
    public bool pauseGameWhileOpen = true;

    // ─── Private State ────────────────────────────────────────────────────────

    public bool IsOpen => _isOpen;

    private bool _isOpen = false;
    private PlayerAttack _playerAttack;
    private PlayerController _playerController;
    private InventoryUI _inventoryUI;
    private float _baseMoveSpeed;
    private float _baseSprintSpeed;
    private float _baseAttackCooldown;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (windowRoot != null)
            windowRoot.SetActive(false);
    }

    void Start()
    {
        // Auto-find player components if not assigned
        if (playerHealthSystem == null || playerStaminaSystem == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                if (playerHealthSystem == null)
                    playerHealthSystem = playerObj.GetComponent<HealthSystem>();

                if (playerStaminaSystem == null)
                    playerStaminaSystem = playerObj.GetComponent<StaminaSystem>();

                _playerAttack = playerObj.GetComponent<PlayerAttack>();
                _playerController = playerObj.GetComponent<PlayerController>();
            }
            else
            {
                Debug.LogWarning("[CharacterWindow] No GameObject with tag 'Player' found!");
            }
        }
        else
        {
            _playerAttack = playerHealthSystem.GetComponent<PlayerAttack>();
            _playerController = playerHealthSystem.GetComponent<PlayerController>();
        }

        // Cache base values before any AGI/INT bonus is applied
        if (_playerController != null)
        {
            _baseMoveSpeed   = _playerController.moveSpeed;
            _baseSprintSpeed = _playerController.sprintSpeed;
        }
        if (_playerAttack != null)
            _baseAttackCooldown = _playerAttack.attackCooldown;

        // Wire stat-spend buttons and disable controller navigation on all of them
        Navigation noNav = new Navigation { mode = Navigation.Mode.None };

        if (btnSpendSTR != null)      { btnSpendSTR.onClick.AddListener(OnSpendSTR);           btnSpendSTR.navigation = noNav; }
        if (btnSpendAGI != null)      { btnSpendAGI.onClick.AddListener(OnSpendAGI);           btnSpendAGI.navigation = noNav; }
        if (btnSpendINT != null)      { btnSpendINT.onClick.AddListener(OnSpendINT);           btnSpendINT.navigation = noNav; }
        if (btnSpendCritRate != null)   { btnSpendCritRate.onClick.AddListener(OnSpendCritRate);   btnSpendCritRate.navigation = noNav; }
        if (btnSpendCritDamage != null) { btnSpendCritDamage.onClick.AddListener(OnSpendCritDamage); btnSpendCritDamage.navigation = noNav; }

        _inventoryUI = FindFirstObjectByType<InventoryUI>();
    }

    void Update()
    {
        bool togglePressed =
            (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) ||
            (Gamepad.current  != null && Gamepad.current.selectButton.wasPressedThisFrame);

        if (togglePressed)
        {
            Debug.Log("[CharacterWindow] togglePressed, _isOpen=" + _isOpen);
            ToggleWindow();
        }

        if (_isOpen)
            RefreshStats();
    }

    // ─── Toggle ───────────────────────────────────────────────────────────────

    private void ToggleWindow()
    {
        // Dismiss any active tutorial popup when opening a panel
        if (!_isOpen && PopupManager.IsShowing)
            PopupManager.Instance.Hide();

        _isOpen = !_isOpen;

        if (_isOpen && SkillTreeManager.Instance != null)
            SkillTreeManager.Instance.CloseWindow();

        if (_isOpen && _inventoryUI != null)
            _inventoryUI.CloseInventory();


        if (windowRoot != null)
            windowRoot.SetActive(_isOpen);

        if (_isOpen)
        {
            RefreshStats();
            if (pauseGameWhileOpen) PauseManager.RequestPause();
        }
        else
        {
            if (pauseGameWhileOpen) PauseManager.ReleasePause();
        }

        bool anyPanelOpen = _isOpen || (_inventoryUI != null && _inventoryUI.IsOpen);
        CameraControllerThirdPerson.IsLocked = anyPanelOpen;

        if (unlockCursorWhileOpen)
        {
            Cursor.lockState = anyPanelOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible   = anyPanelOpen;
        }

        Debug.Log("[CharacterWindow] Window " + (_isOpen ? "opened." : "closed."));
    }

    /// <summary>
    /// Called by PlayerUILinker after the player spawns.
    /// Re-wires all player-component references that Start() couldn't find
    /// because the player didn't exist yet at that point.
    /// </summary>
    public void RewirePlayer(GameObject playerObj)
    {
        if (playerObj == null) return;

        playerHealthSystem  = playerObj.GetComponent<HealthSystem>();
        playerStaminaSystem = playerObj.GetComponent<StaminaSystem>();
        _playerAttack       = playerObj.GetComponent<PlayerAttack>();
        _playerController   = playerObj.GetComponent<PlayerController>();

        if (_playerController != null)
        {
            _baseMoveSpeed   = _playerController.moveSpeed;
            _baseSprintSpeed = _playerController.sprintSpeed;
        }
        if (_playerAttack != null)
            _baseAttackCooldown = _playerAttack.attackCooldown;

        Debug.Log("[CharacterWindow] Rewired to player: " + playerObj.name);
    }

    /// <summary>Closes the character window externally (e.g. when another panel opens).</summary>
    public void CloseWindow()
    {
        if (!_isOpen) return;
        _isOpen = false;

        if (windowRoot != null)
            windowRoot.SetActive(false);

        if (pauseGameWhileOpen) PauseManager.ReleasePause();

        bool anyPanelOpen = _inventoryUI != null && _inventoryUI.IsOpen;
        CameraControllerThirdPerson.IsLocked = anyPanelOpen;

        if (unlockCursorWhileOpen)
        {
            Cursor.lockState = anyPanelOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible   = anyPanelOpen;
        }

        Debug.Log("[CharacterWindow] Window closed externally.");
    }

    // ─── Stat Spend Callbacks ─────────────────────────────────────────────────

    private void OnSpendSTR()
    {
        if (ExperienceManager.Instance != null)
            ExperienceManager.Instance.SpendOnSTR();
    }

    private void OnSpendAGI()
    {
        if (ExperienceManager.Instance != null)
            ExperienceManager.Instance.SpendOnAGI();
    }

    private void OnSpendINT()
    {
        if (ExperienceManager.Instance != null)
            ExperienceManager.Instance.SpendOnINT();
    }

    private void OnSpendCritRate()
    {
        if (ExperienceManager.Instance != null)
            ExperienceManager.Instance.SpendOnCritRate();
    }

    private void OnSpendCritDamage()
    {
        if (ExperienceManager.Instance != null)
            ExperienceManager.Instance.SpendOnCritDamage();
    }

    // ─── Stat Refresh ─────────────────────────────────────────────────────────

    /// <summary>
    /// Pulls the latest values from all systems and writes them to the TMP labels.
    /// Also enables or disables the spend buttons based on available stat points.
    /// </summary>
    public void RefreshStats()
    {
        ExperienceManager xp = ExperienceManager.Instance;

        // ── Level, XP & Stat Points ───────────────────────────────────────────
        if (xp != null)
        {
            SetLabel(levelLabel,      "Level",      xp.currentLevel.ToString());
            SetLabel(xpLabel,         "Experience", $"{xp.currentXP} / {xp.xpToNextLevel} XP");
            SetLabel(xpScalingLabel,  "XP Growth",  $"×{xp.xpScalingFactor:F2} per level");
            SetLabel(strengthLabel,     "STR", FormatStat(xp.strength,     xp.EffectiveSTR));
            SetLabel(agilityLabel,      "AGI", FormatStat(xp.agility,      xp.EffectiveAGI));
            SetLabel(intelligenceLabel, "INT", FormatStat(xp.intelligence, xp.EffectiveINT));

            bool hasPoints = xp.statPoints > 0;

            if (statPointsLabel != null)
            {
                statPointsLabel.text = hasPoints
                    ? $"Stat Points:  <color=#FFD700>{xp.statPoints}</color>"
                    : "Stat Points:  0";
            }

            if (xpProgressBar != null)
                xpProgressBar.value = (float)xp.currentXP / xp.xpToNextLevel;

            // Show spend buttons only when there are points to spend
            if (btnSpendSTR != null)        btnSpendSTR.gameObject.SetActive(hasPoints);
            if (btnSpendAGI != null)        btnSpendAGI.gameObject.SetActive(hasPoints);
            if (btnSpendINT != null)        btnSpendINT.gameObject.SetActive(hasPoints);
            if (btnSpendCritRate != null)   btnSpendCritRate.gameObject.SetActive(hasPoints);
            if (btnSpendCritDamage != null) btnSpendCritDamage.gameObject.SetActive(hasPoints);

            // ── Crit Stats ────────────────────────────────────────────────────
            int critRatePct   = Mathf.RoundToInt(xp.ComputedCritRate   * 100f);
            int critDamagePct = Mathf.RoundToInt(xp.ComputedCritDamage * 100f);
            SetLabel(critRateLabel,   "Crit Rate",   $"{critRatePct}%");
            SetLabel(critDamageLabel, "Crit Damage", $"{critDamagePct}%");
        }
        else
        {
            Debug.LogWarning("[CharacterWindow] ExperienceManager instance not found!");
        }

        // ── Health ────────────────────────────────────────────────────────────
        if (playerHealthSystem != null)
        {
            SetLabel(hpLabel, "Max HP", playerHealthSystem.maxHealth.ToString());

            float baseRegen  = playerHealthSystem.RegenPerSecond;
            float totalRegen = playerHealthSystem.TotalRegenPerSecond;
            float equipBonus = totalRegen - baseRegen;
            string regenStr  = equipBonus > 0f
                ? $"{totalRegen:F1} <color=#C9A84C>(+{equipBonus:F1})</color>"
                : $"{totalRegen:F1}";
            SetLabel(regenLabel, "HP Regen/s", regenStr);
        }
        else
            Debug.LogWarning("[CharacterWindow] HealthSystem not found on player!");

        // ── Stamina ───────────────────────────────────────────────────────────
        if (playerStaminaSystem != null)
            SetLabel(staminaLabel, "Max Stamina", playerStaminaSystem.maxStamina.ToString("F0"));
        else
            Debug.LogWarning("[CharacterWindow] StaminaSystem not found on player!");

        // ── Combat (PlayerAttack) ─────────────────────────────────────────────
        if (_playerAttack != null)
        {
            int minDmg = (xp != null) ? xp.ComputedMinDamage : _playerAttack.attackDamage;
            int maxDmg = (xp != null) ? xp.ComputedMaxDamage : _playerAttack.attackDamage;
            SetLabel(attackDamageLabel, "Damage", $"{minDmg} - {maxDmg}");
            SetLabel(attackRadiusLabel, "Attack Radius", _playerAttack.attackRadius.ToString("F1") + " m");

            if (xp != null && _baseAttackCooldown > 0f)
            {
                float computedCooldown = xp.ComputedAttackCooldown(_baseAttackCooldown);
                int attackSpeedPct = Mathf.RoundToInt(_baseAttackCooldown / computedCooldown * 100f);
                SetLabel(attackSpeedLabel, "Attack Speed", $"{attackSpeedPct}%");
            }
        }

        // ── Spell Damage ──────────────────────────────────────────────────────
        if (xp != null)
        {
            int spellPct = Mathf.RoundToInt(xp.SpellDamageMultiplier * 100f);
            SetLabel(spellDamageLabel, "Spell Damage", $"{spellPct}%");
        }

        // ── Movement (PlayerController) ───────────────────────────────────────
        if (_playerController != null && xp != null)
        {
            float equipBonus   = PlayerInventory.Instance != null
                ? PlayerInventory.Instance.TotalBonusMovementSpeed : 0f; // 0-1 fraction
            int agiMovePct    = Mathf.RoundToInt(xp.ComputedMoveSpeed(_baseMoveSpeed)   / _baseMoveSpeed   * 100f);
            int agiSprintPct  = Mathf.RoundToInt(xp.ComputedSprintSpeed(_baseSprintSpeed) / _baseSprintSpeed * 100f);
            int equipBonusPct = Mathf.RoundToInt(equipBonus * 100f);
            string moveStr   = equipBonusPct > 0
                ? $"{agiMovePct + equipBonusPct}% <color=#C9A84C>(+{equipBonusPct}%)</color>"
                : $"{agiMovePct}%";
            string sprintStr = equipBonusPct > 0
                ? $"{agiSprintPct + equipBonusPct}% <color=#C9A84C>(+{equipBonusPct}%)</color>"
                : $"{agiSprintPct}%";
            SetLabel(moveSpeedLabel,   "Move Speed",   moveStr);
            SetLabel(sprintSpeedLabel, "Sprint Speed", sprintStr);
        }

    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void SetLabel(TextMeshProUGUI label, string statName, string value)
    {
        if (label == null) return;
        label.text = $"{statName}:  {value}";
    }

    /// <summary>
    /// Returns the stat value. If equipment is adding a bonus, appends it in gold.
    /// E.g. "5" or "5 (+3)"
    /// </summary>
    private string FormatStat(int baseStat, int effectiveStat)
    {
        int bonus = effectiveStat - baseStat;
        return bonus > 0
            ? $"{baseStat} <color=#FFD700>(+{bonus})</color>"
            : baseStat.ToString();
    }
}
