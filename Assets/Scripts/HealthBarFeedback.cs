using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the HP bar panel. Wire via PlayerUILinker.WireHealthSystem().
///
/// Reads HealthSystem fields each frame. On damage the ghost fill holds at the
/// pre-damage value for DamageDelay seconds, then shrinks to the current HP
/// with a DamageColor → EmptyColor fade. Each new hit resets the delay so the
/// ghost correctly trails across rapid hits.
/// </summary>
public class HealthBarFeedback : MonoBehaviour
{
    // ─── Inspector — Bar References ───────────────────────────────────────────

    [Header("Bar References")]
    [Tooltip("Ghost (damage trail) fill Image — must sit BEHIND the main fill in the hierarchy.")]
    public Image ghostFill;

    [Tooltip("Main fill Image (owned by HealthSystem). Optional — only used for the flash effect.")]
    public Image mainFill;

    // ─── Inspector — Ghost Bar ────────────────────────────────────────────────

    [Header("Ghost Bar")]
    [Tooltip("Seconds the ghost holds at the pre-damage fill before it starts shrinking.")]
    public float DamageDelay = 0.5f;

    [Tooltip("fillAmount units per second the ghost shrinks toward the current HP.")]
    public float ShrinkSpeed = 2f;

    [Tooltip("Ghost color while waiting (during the DamageDelay window).")]
    public Color DamageColor = new Color(1f, 0.45f, 0.45f, 1f);

    [Tooltip("Ghost fades toward this color as it finishes shrinking.")]
    public Color EmptyColor = Color.black;

    // ─── Inspector — Main Fill Flash ──────────────────────────────────────────

    [Header("Main Fill Flash (optional)")]
    [Tooltip("How much the main fill brightens on damage. 0 = disabled.")]
    [Range(0f, 1f)]
    public float FlashIntensity = 0.6f;

    [Tooltip("Seconds the flash fades back to normal.")]
    public float FlashDuration = 0.12f;

    // ─── Inspector — Shake ────────────────────────────────────────────────────

    [Header("Heavy Damage Shake (optional)")]
    [Tooltip("Pixel amplitude of the RectTransform shake. 0 = disabled.")]
    public float PulseStrength = 4f;

    [Tooltip("Single hit must deal more than this fraction of maxHP to trigger the shake.")]
    [Range(0f, 1f)]
    public float HeavyDamageThreshold = 0.15f;

    // ─── Private State ────────────────────────────────────────────────────────

    private HealthSystem _health;
    private float        _ghostFillAmount;
    private float        _prevRatio;
    private float        _delayTimer;
    private bool         _delayActive;
    private float        _shrinkStartGap;

    private Color         _mainFillBaseColor;
    private RectTransform _rectTransform;
    private Vector2       _rectAnchorBase;

    private Coroutine _flashCoroutine;
    private Coroutine _pulseCoroutine;

    // ─── Public Wiring API ────────────────────────────────────────────────────

    /// <summary>Called by PlayerUILinker after the local player spawns.</summary>
    public void WireHealthSystem(HealthSystem health)
    {
        StopAllCoroutines();

        _health = health;

        float ratio = health.maxHealth > 0f
            ? health.currentHealth / health.maxHealth
            : 1f;

        _prevRatio       = ratio;
        _ghostFillAmount = ratio;
        _delayActive     = false;
        _delayTimer      = 0f;
        _shrinkStartGap  = 0f;

        if (ghostFill != null)
        {
            ghostFill.fillAmount = ratio;
            ghostFill.color      = DamageColor;
        }

        if (mainFill != null)
            _mainFillBaseColor = mainFill.color;

        _rectTransform = GetComponent<RectTransform>();
        if (_rectTransform != null)
            _rectAnchorBase = _rectTransform.anchoredPosition;
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    void Update()
    {
        if (_health == null || ghostFill == null) return;

        float ratio = _health.maxHealth > 0f
            ? _health.currentHealth / _health.maxHealth
            : 0f;
        ratio = Mathf.Clamp01(ratio);

        // ── Damage ────────────────────────────────────────────────────────────
        if (ratio < _prevRatio - 0.0001f)
        {
            float damageFraction = _prevRatio - ratio;

            // Ghost holds at the highest fill seen — never let a new hit drag it down.
            // Max() handles both: delay phase (ghost at peak) and shrink phase
            // (ghost mid-way down but still above the new _prevRatio).
            _ghostFillAmount = Mathf.Max(_ghostFillAmount, _prevRatio);

            _delayActive = true;
            _delayTimer  = DamageDelay;
            ghostFill.color = DamageColor;

            if (FlashIntensity > 0f && FlashDuration > 0f && mainFill != null)
            {
                if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
                _flashCoroutine = StartCoroutine(FlashRoutine());
            }

            if (damageFraction >= HeavyDamageThreshold && PulseStrength > 0f && _rectTransform != null)
            {
                if (_pulseCoroutine != null) StopCoroutine(_pulseCoroutine);
                _pulseCoroutine = StartCoroutine(ShakeRoutine());
            }
        }

        // ── Heal: only cancel delay if HP recovers PAST the ghost position ───
        // Passive regen ticking below the ghost fill must not cancel the delay.
        // A potion/heal that brings HP above the ghost's current fill should
        // snap the ghost and clear the trail immediately.
        if (ratio > _prevRatio + 0.0001f && ratio >= _ghostFillAmount)
        {
            _ghostFillAmount = ratio;
            _delayActive     = false;
            _delayTimer      = 0f;
        }

        // ── Death guard ───────────────────────────────────────────────────────
        if (ratio <= 0f)
        {
            _ghostFillAmount = 0f;
            _delayActive     = false;
            _delayTimer      = 0f;
        }

        // ── Delay countdown ───────────────────────────────────────────────────
        if (_delayActive)
        {
            _delayTimer -= Time.deltaTime;
            if (_delayTimer <= 0f)
            {
                _delayActive    = false;
                _shrinkStartGap = Mathf.Max(_ghostFillAmount - ratio, 0f);
            }
        }

        // ── Shrink phase ──────────────────────────────────────────────────────
        if (!_delayActive && _ghostFillAmount > ratio + 0.0001f)
        {
            _ghostFillAmount -= ShrinkSpeed * Time.deltaTime;
            _ghostFillAmount  = Mathf.Clamp(_ghostFillAmount, ratio, 1f);

            // Fade DamageColor → EmptyColor as ghost closes in on the target fill.
            float gap    = Mathf.Max(_ghostFillAmount - ratio, 0f);
            float colorT = 1f - Mathf.Clamp01(gap / Mathf.Max(_shrinkStartGap, 0.001f));
            ghostFill.color = Color.Lerp(DamageColor, EmptyColor, colorT);
        }

        // ── Write ghost ───────────────────────────────────────────────────────
        _ghostFillAmount     = Mathf.Clamp(_ghostFillAmount, ratio, 1f);
        ghostFill.fillAmount = _ghostFillAmount;

        _prevRatio = ratio;
    }

    // ─── Coroutines ───────────────────────────────────────────────────────────

    IEnumerator FlashRoutine()
    {
        Color flashPeak = Color.Lerp(_mainFillBaseColor, Color.white, FlashIntensity);
        mainFill.color  = flashPeak;

        float elapsed = 0f;
        while (elapsed < FlashDuration)
        {
            elapsed       += Time.deltaTime;
            mainFill.color = Color.Lerp(flashPeak, _mainFillBaseColor, elapsed / FlashDuration);
            yield return null;
        }

        mainFill.color  = _mainFillBaseColor;
        _flashCoroutine = null;
    }

    IEnumerator ShakeRoutine()
    {
        const float Duration     = 0.25f;
        const float Oscillations = 3f;

        float elapsed = 0f;
        while (elapsed < Duration)
        {
            elapsed += Time.deltaTime;
            float t      = elapsed / Duration;
            float decay  = 1f - t;
            float offset = Mathf.Sin(t * Oscillations * 2f * Mathf.PI) * PulseStrength * decay;
            _rectTransform.anchoredPosition = _rectAnchorBase + new Vector2(offset, 0f);
            yield return null;
        }

        _rectTransform.anchoredPosition = _rectAnchorBase;
        _pulseCoroutine = null;
    }
}
