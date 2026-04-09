using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// HUD bar that shows spell cast progress and channel tick progress.
///
/// During cast time  (Pending)    : bar fills 0 → 1 over the spell's castTime.
/// During channeling (Channeling) : bar fills 0 → 1 between each channel tick, then resets.
/// Idle                           : panel hidden.
///
/// Wire via PlayerUILinker.SetSpellCastBar() — never call FindObjectOfType in Update.
/// </summary>
public class SpellCastBarUI : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Panel")]
    [Tooltip("Root GameObject of the cast bar (shown/hidden based on cast state).")]
    public GameObject panel;

    [Header("Fill")]
    [Tooltip("Image set to fillAmount 0–1 representing cast or channel progress.")]
    public Image fillImage;

    [Header("Labels")]
    [Tooltip("Displays the spell name while casting or channeling.")]
    public TMP_Text spellNameLabel;

    [Tooltip("Displays 'Casting…' or 'Channeling'.")]
    public TMP_Text stateLabel;

    [Header("Icon")]
    [Tooltip("Optional spell icon displayed in the bar.")]
    public Image spellIcon;

    [Header("Colors")]
    [Tooltip("Bar fill color during cast time.")]
    public Color castColor = new Color(0.9f, 0.6f, 0.1f, 1f);   // warm orange

    [Tooltip("Bar fill color while channeling.")]
    public Color channelColor = new Color(0.2f, 0.7f, 1f, 1f);  // cool blue

    // ─── State ────────────────────────────────────────────────────────────────

    private SpellCaster _caster;

    // ─── Wiring ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by PlayerUILinker after the local player spawns.
    /// Subscribes to SpellCaster events and hides the panel.
    /// </summary>
    public void SetSpellCaster(SpellCaster caster)
    {
        if (_caster != null) Unsubscribe(_caster);
        _caster = caster;
        Subscribe(caster);
        HidePanel();
    }

    void OnDestroy()
    {
        if (_caster != null) Unsubscribe(_caster);
    }

    // ─── Event Subscriptions ──────────────────────────────────────────────────

    void Subscribe(SpellCaster caster)
    {
        caster.OnCastBegin      += HandleCastBegin;
        caster.OnCastComplete   += HandleCastComplete;
        caster.OnChannelBegin   += HandleChannelBegin;
        caster.OnChannelTick    += HandleChannelTick;
        caster.OnChannelEnd     += HandleChannelEnd;
        caster.OnCastCancelled  += HandleCastCancelled;
    }

    void Unsubscribe(SpellCaster caster)
    {
        caster.OnCastBegin      -= HandleCastBegin;
        caster.OnCastComplete   -= HandleCastComplete;
        caster.OnChannelBegin   -= HandleChannelBegin;
        caster.OnChannelTick    -= HandleChannelTick;
        caster.OnChannelEnd     -= HandleChannelEnd;
        caster.OnCastCancelled  -= HandleCastCancelled;
    }

    // ─── Event Handlers ───────────────────────────────────────────────────────

    void HandleCastBegin(SpellData spell, float castDuration)
    {
        ShowPanel(spell, "Casting…", castColor);
    }

    void HandleCastComplete() { }  // panel stays; Update() drives fill until channel/idle

    void HandleChannelBegin(SpellData spell)
    {
        ShowPanel(spell, "Channeling", channelColor);
    }

    void HandleChannelTick()
    {
        if (fillImage != null) fillImage.fillAmount = 0f;
    }

    void HandleChannelEnd()   => HidePanel();
    void HandleCastCancelled() => HidePanel();

    // ─── Update ───────────────────────────────────────────────────────────────

    void Update()
    {
        if (_caster == null || fillImage == null) return;
        if (!_caster.IsLocked) return;  // idle — events handle show/hide

        if (_caster.IsCasting)
            fillImage.fillAmount = _caster.CastProgress;
        else if (_caster.IsChanneling)
            fillImage.fillAmount = _caster.ChannelTickProgress;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    void ShowPanel(SpellData spell, string state, Color color)
    {
        if (panel != null)      panel.SetActive(true);
        if (fillImage != null)  { fillImage.fillAmount = 0f; fillImage.color = color; }
        if (spellNameLabel != null) spellNameLabel.text = spell != null ? spell.spellName : "";
        if (stateLabel != null)     stateLabel.text     = state;
        if (spellIcon  != null)
        {
            spellIcon.sprite  = spell?.icon;
            spellIcon.enabled = spell?.icon != null;
        }
    }

    void HidePanel()
    {
        if (panel != null) panel.SetActive(false);
    }
}
