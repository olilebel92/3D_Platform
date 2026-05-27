using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tracks the player's mana pool and regenerates it over time.
/// SpellCaster reserves mana on BeginCast and spends it on FireSpell — the bar shows
/// the reserved chunk in a lighter blue during the windup. The nameplate and HUD bar
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

    [Tooltip("Optional lighter-blue overlay showing the mana cost of the spell currently being cast. Same anchor/size as manaBarFill, rendered behind it.")]
    public Image manaBarReservedFill;

    [Tooltip("Tint applied to the reserved-mana overlay (visible during a pending cast). Click the swatch to open Unity's color picker — RGB or HSV sliders.")]
    public Color reservedColor = new Color(0.49f, 0.78f, 1f, 1f);

    [Tooltip("Root panel GameObject for the standalone HUD mana bar.")]
    public GameObject manaBarPanel;

    // ─── State ────────────────────────────────────────────────────────────────

    private float _currentMana;
    private float _reservedMana;
    private float _permanentMaxMana;
    private float _equipmentManaRegen;
    private float _skillTreeManaRegen;

    /// <summary>Read-only current mana — polled by PlayerNameplateUI and SpellCaster.</summary>
    public float CurrentMana => _currentMana;

    /// <summary>Amount of mana currently reserved by a pending cast (visualised as a lighter-blue chunk).</summary>
    public float ReservedMana => _reservedMana;

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
        if (_reservedMana > _currentMana) _reservedMana = _currentMana;
        UpdateUI();
        return true;
    }

    /// <summary>
    /// Marks <paramref name="cost"/> mana as "reserved" by a pending cast.
    /// The mana bar renders this as a lighter-blue chunk; the value is not deducted yet.
    /// </summary>
    public void ReserveMana(float cost)
    {
        _reservedMana = Mathf.Clamp(cost, 0f, _currentMana);
        UpdateUI();
    }

    /// <summary>Clears any pending reservation without spending. Called when a cast is cancelled.</summary>
    public void ClearReservedMana()
    {
        if (_reservedMana == 0f) return;
        _reservedMana = 0f;
        UpdateUI();
    }

    /// <summary>
    /// Deducts the currently-reserved amount from the mana pool and clears the reservation.
    /// Returns true if anything was spent, false if no reservation was active.
    /// </summary>
    public bool SpendReservedMana()
    {
        if (_reservedMana <= 0f) return false;
        float cost = _reservedMana;
        _reservedMana = 0f;
        _currentMana = Mathf.Max(_currentMana - cost, 0f);
        UpdateUI();
        return true;
    }

    /// <summary>Restores <paramref name="amount"/> mana, capped at maxMana.</summary>
    public void RestoreMana(float amount)
    {
        _currentMana = Mathf.Min(_currentMana + amount, maxMana);
        UpdateUI();
    }

    /// <summary>Restores a fraction of maxMana (0–1).</summary>
    public void RestoreManaPercent(float fraction) => RestoreMana(maxMana * fraction);

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
        if (_reservedMana > _currentMana) _reservedMana = _currentMana;
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
        if (maxMana <= 0f)
        {
            if (manaBarFill         != null) manaBarFill.fillAmount         = 0f;
            if (manaBarReservedFill != null) manaBarReservedFill.fillAmount = 0f;
            return;
        }

        // The reserved overlay shows total current mana (including the reserved chunk).
        // The main fill shows what's left AFTER the reservation is spent.
        // The visible difference between the two is the lighter "about to be spent" chunk.
        if (manaBarReservedFill != null)
        {
            manaBarReservedFill.color      = reservedColor;
            manaBarReservedFill.fillAmount = _currentMana / maxMana;
        }

        if (manaBarFill != null)
            manaBarFill.fillAmount = Mathf.Max(0f, _currentMana - _reservedMana) / maxMana;
    }
}
