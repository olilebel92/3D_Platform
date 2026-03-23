using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Manages the player's Level, XP, and three core stats: STR, AGI, INT.
/// On level-up the game pauses and a stat-choice panel appears — the player
/// picks which stat to increase before play resumes.
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
    [Tooltip("STR — increases melee attack damage.")]
    public int strength = 1;

    [Tooltip("AGI — reduces attack cooldown and boosts move/sprint speed. " +
             "Tune the per-point bonuses below.")]
    public int agility = 1;

    [Tooltip("INT — reserved for future mana and spell-damage systems.")]
    public int intelligence = 1;

    // ─── AGI Tuning ───────────────────────────────────────────────────────────

    [Header("AGI Tuning")]
    [Tooltip("Flat speed added to base moveSpeed per AGI point.")]
    public float agiMoveSpeedBonus = 0.1f;

    [Tooltip("Flat speed added to base sprintSpeed per AGI point.")]
    public float agiSprintSpeedBonus = 0.15f;

    [Tooltip("Seconds subtracted from base attackCooldown per AGI point. " +
             "Clamped so cooldown never drops below agiMinAttackCooldown.")]
    public float agiCooldownReduction = 0.02f;

    [Tooltip("Hard floor for attack cooldown regardless of AGI.")]
    public float agiMinAttackCooldown = 0.2f;

    // ─── UI References ────────────────────────────────────────────────────────

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

    // ─── Stat-Choice Panel ────────────────────────────────────────────────────

    [Header("Level-Up Panel")]
    [Tooltip("Root panel GameObject that appears when the player levels up. " +
             "Wire the three buttons below to ChooseSTR / ChooseAGI / ChooseINT.")]
    public GameObject levelUpPanel;

    [Tooltip("Button that increases STR. Hook its OnClick → ExperienceManager.ChooseSTR()")]
    public Button btnChooseSTR;

    [Tooltip("Button that increases AGI. Hook its OnClick → ExperienceManager.ChooseAGI()")]
    public Button btnChooseAGI;

    [Tooltip("Button that increases INT. Hook its OnClick → ExperienceManager.ChooseINT()")]
    public Button btnChooseINT;

    // ─── Private State ────────────────────────────────────────────────────────

    private bool _waitingForStatChoice = false;

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

    void Start()
    {
        // Wire buttons in code so the designer doesn't have to drag them manually
        if (btnChooseSTR != null) btnChooseSTR.onClick.AddListener(ChooseSTR);
        if (btnChooseAGI != null) btnChooseAGI.onClick.AddListener(ChooseAGI);
        if (btnChooseINT != null) btnChooseINT.onClick.AddListener(ChooseINT);

        HideLevelUpPanel();
        UpdateUI();
    }

    // ─── Public XP API ───────────────────────────────────────────────────────

    /// <summary>
    /// Award XP to the player. Triggers the level-up panel if a threshold is crossed.
    /// </summary>
    public void GainXP(int amount)
    {
        if (_waitingForStatChoice) return;  // Don't accumulate XP mid-choice

        currentXP += amount;
        Debug.Log($"[XP] +{amount} XP  →  {currentXP}/{xpToNextLevel}");

        if (currentXP >= xpToNextLevel)
            TriggerLevelUp();

        UpdateUI();
    }

    // ─── Computed Stat Properties ─────────────────────────────────────────────

    /// <summary>Move speed after applying AGI bonus. Read by PlayerController.</summary>
    public float ComputedMoveSpeed(float baseMoveSpeed)
        => baseMoveSpeed + agility * agiMoveSpeedBonus;

    /// <summary>Sprint speed after applying AGI bonus. Read by PlayerController.</summary>
    public float ComputedSprintSpeed(float baseSprintSpeed)
        => baseSprintSpeed + agility * agiSprintSpeedBonus;

    /// <summary>Attack cooldown after applying AGI reduction. Read by PlayerAttack.</summary>
    public float ComputedAttackCooldown(float baseAttackCooldown)
        => Mathf.Max(agiMinAttackCooldown, baseAttackCooldown - agility * agiCooldownReduction);

    // ─── Level-Up Flow ────────────────────────────────────────────────────────

    private void TriggerLevelUp()
    {
        currentXP -= xpToNextLevel;
        currentLevel++;
        xpToNextLevel = Mathf.RoundToInt(xpToNextLevel * xpScalingFactor);

        Debug.Log($"[LEVEL UP] Now Level {currentLevel}! Choose a stat. " +
                  $"Next level needs {xpToNextLevel} XP.");

        _waitingForStatChoice = true;
        Time.timeScale = 0f;        // Pause game while choosing
        ShowLevelUpPanel();
    }

    // ─── Stat Choice Callbacks ────────────────────────────────────────────────

    /// <summary>Called by the STR button (or wired in Start).</summary>
    public void ChooseSTR()
    {
        strength++;
        Debug.Log($"[STAT CHOICE] +1 STR → STR is now {strength}");
        FinishStatChoice();
    }

    /// <summary>Called by the AGI button (or wired in Start).</summary>
    public void ChooseAGI()
    {
        agility++;
        Debug.Log($"[STAT CHOICE] +1 AGI → AGI is now {agility}");
        FinishStatChoice();
    }

    /// <summary>Called by the INT button (or wired in Start).</summary>
    public void ChooseINT()
    {
        intelligence++;
        Debug.Log($"[STAT CHOICE] +1 INT → INT is now {intelligence}");
        // TODO: Hook INT into mana pool and spell-damage systems when implemented.
        FinishStatChoice();
    }

    private void FinishStatChoice()
    {
        _waitingForStatChoice = false;
        Time.timeScale = 1f;        // Resume game
        HideLevelUpPanel();

        // If a massive XP grant caused multiple level-ups, handle the next one
        if (currentXP >= xpToNextLevel)
            TriggerLevelUp();

        UpdateUI();
    }

    // ─── Panel Helpers ────────────────────────────────────────────────────────

    private void ShowLevelUpPanel()
    {
        if (levelUpPanel != null)
            levelUpPanel.SetActive(true);
    }

    private void HideLevelUpPanel()
    {
        if (levelUpPanel != null)
            levelUpPanel.SetActive(false);
    }

    // ─── UI Update ────────────────────────────────────────────────────────────

    private void UpdateUI()
    {
        if (levelText != null)
            levelText.text = $"Level {currentLevel}";

        if (xpText != null)
            xpText.text = $"{currentXP} / {xpToNextLevel} XP";

        if (xpBar != null)
            xpBar.value = (float)currentXP / xpToNextLevel;

        if (strengthText != null)
            strengthText.text = $"STR: {strength}";

        if (agilityText != null)
            agilityText.text = $"AGI: {agility}";

        if (intelligenceText != null)
            intelligenceText.text = $"INT: {intelligence}";
    }
}