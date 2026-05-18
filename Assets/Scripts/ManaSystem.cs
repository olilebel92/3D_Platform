using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tracks the player's mana pool and regenerates it over time.
/// SpellCaster calls SpendMana() before each cast; the nameplate and HUD bar
/// are driven by polling CurrentMana every frame.
/// </summary>
public class ManaSystem : MonoBehaviour
{
    // ─── Settings ─────────────────────────────────────────────────────────────

    [Header("Mana Settings")]
    [Tooltip("Maximum mana pool.")]
    public float maxMana = 100f;

    [Tooltip("Mana regenerated per second while out of combat (always active for now).")]
    [SerializeField] private float regenPerSecond = 5f;

    // ─── UI ───────────────────────────────────────────────────────────────────

    [Header("UI (optional — wired by PlayerUILinker)")]
    [Tooltip("Fill Image for the mana bar. Image Type: Filled, Fill Method: Horizontal.")]
    public Image manaBarFill;

    [Tooltip("Root panel GameObject for the standalone HUD mana bar.")]
    public GameObject manaBarPanel;

    // ─── State ────────────────────────────────────────────────────────────────

    private float _currentMana;
    private float _permanentMaxMana;
    private float _equipmentManaRegen;
    private float _skillTreeManaRegen;

    /// <summary>Read-only current mana — polled by PlayerNameplateUI and SpellCaster.</summary>
    public float CurrentMana => _currentMana;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        _permanentMaxMana = maxMana;
        _currentMana = maxMana;
        UpdateUI();
    }

    void Update()
    {
        if (_currentMana < maxMana)
        {
            _currentMana = Mathf.Min(_currentMana + (regenPerSecond + _equipmentManaRegen + _skillTreeManaRegen) * Time.deltaTime, maxMana);
            UpdateUI();
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Returns true if the player has at least <paramref name="cost"/> mana.</summary>
    public bool HasMana(float cost) => _currentMana >= cost;

    /// <summary>
    /// Deducts <paramref name="cost"/> from the mana pool.
    /// Returns true on success, false if not enough mana (pool unchanged).
    /// </summary>
    public bool SpendMana(float cost)
    {
        if (_currentMana < cost) return false;
        _currentMana -= cost;
        _currentMana  = Mathf.Max(_currentMana, 0f);
        UpdateUI();
        return true;
    }

    /// <summary>Called by ExperienceManager whenever equipment changes to apply mana regen bonuses.</summary>
    public void ApplyEquipmentManaRegen(float regenBonus) => _equipmentManaRegen = regenBonus;

    /// <summary>Called by ExperienceManager whenever skill tree changes to apply mana regen bonuses.</summary>
    public void ApplySkillTreeManaRegen(float regenBonus) => _skillTreeManaRegen = regenBonus;

    /// <summary>Called by ExperienceManager whenever equipment changes to apply flat mana bonuses.</summary>
    public void ApplyEquipmentMana(float equipmentBonus)
    {
        float newMax = _permanentMaxMana + equipmentBonus;
        maxMana = newMax;
        _currentMana = Mathf.Clamp(_currentMana, 0f, maxMana);
        UpdateUI();
    }

    /// <summary>Called by PlayerUILinker after it wires the UI refs at runtime.</summary>
    public void RefreshUI()
    {
        if (manaBarPanel != null) manaBarPanel.SetActive(true);
        UpdateUI();
    }

    // ─── Internal ─────────────────────────────────────────────────────────────

    void UpdateUI()
    {
        if (manaBarFill != null)
            manaBarFill.fillAmount = maxMana > 0f ? _currentMana / maxMana : 0f;
    }
}
