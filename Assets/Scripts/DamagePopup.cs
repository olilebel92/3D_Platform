using UnityEngine;
using TMPro;

/// <summary>
/// A single floating damage number that rises, fades, then destroys itself.
/// Spawned by DamagePopupManager — do not place this directly in the scene.
/// </summary>
public class DamagePopup : MonoBehaviour
{
    // ─── Settings ─────────────────────────────────────────────────────────────

    [Header("Movement")]
    [Tooltip("How fast the popup rises per second.")]
    public float riseSpeed = 1.5f;

    [Tooltip("Small random horizontal drift so stacked hits don't overlap.")]
    public float horizontalDrift = 0.4f;

    [Header("Timing")]
    [Tooltip("How long the popup is fully visible before it starts fading.")]
    public float holdDuration = 0.4f;

    [Tooltip("How long the fade-out takes after the hold phase.")]
    public float fadeDuration = 0.5f;

    // ─── Private State ────────────────────────────────────────────────────────

    private TextMeshPro _label;
    private Camera _mainCam;
    private float _timer;
    private float _moveDelay;
    private Vector3 _drift;
    private Color _color;

    // ─── Public Setup ─────────────────────────────────────────────────────────

    /// <summary>
    /// Called by DamagePopupManager for free-text popups (e.g. "LEVEL UP!").
    /// </summary>
    public void InitializeText(string text, Color color, float fontSize = 6f,
        TMP_FontAsset font = null, float moveDelay = 0f, float holdDuration = -1f, float fadeDuration = -1f,
        Color outlineColor = default, float outlineWidth = 0f)
    {
        _label = GetComponent<TextMeshPro>();
        if (_label == null) { Destroy(gameObject); return; }

        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = 20000;

        _mainCam       = Camera.main;
        _label.fontSize = fontSize;
        _label.text    = text;
        _color         = color;
        _label.color   = _color;
        _moveDelay     = moveDelay;
        if (holdDuration >= 0f) this.holdDuration = holdDuration;
        if (fadeDuration >= 0f) this.fadeDuration = fadeDuration;
        if (font != null) _label.font = font;
        if (outlineWidth > 0f) { _label.outlineColor = outlineColor; _label.outlineWidth = outlineWidth; }

        float dx = Random.Range(-horizontalDrift, horizontalDrift);
        _drift = new Vector3(dx, riseSpeed, 0f);
        _timer = 0f;
    }

    /// <summary>
    /// Called by DamagePopupManager immediately after instantiation.
    /// </summary>
    /// <param name="amount">Value to display.</param>
    /// <param name="color">Text color.</param>
    /// <param name="prefix">Optional prefix prepended to the number, e.g. "+".</param>
    /// <param name="suffix">Optional suffix appended after the number, e.g. " EXP".</param>
    public void Initialize(int amount, Color color, float fontSize = 6f, string prefix = "", string suffix = "",
        TMP_FontAsset font = null, float moveDelay = 0f, float holdDuration = -1f, float fadeDuration = -1f,
        Color outlineColor = default, float outlineWidth = 0f)
    {
        _label = GetComponent<TextMeshPro>();
        if (_label == null)
        {
            Debug.LogWarning("[DamagePopup] No TextMeshPro component found!");
            Destroy(gameObject);
            return;
        }

        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = 20000;

        _mainCam = Camera.main;

        _label.fontSize = fontSize;
        _label.text     = prefix + amount.ToString() + suffix;
        _color          = color;
        _label.color    = _color;

        _moveDelay = moveDelay;
        if (holdDuration >= 0f) this.holdDuration = holdDuration;
        if (fadeDuration >= 0f) this.fadeDuration = fadeDuration;
        if (font != null) _label.font = font;
        if (outlineWidth > 0f)
        {
            _label.outlineColor = outlineColor;
            _label.outlineWidth = outlineWidth;
        }

        float dx = Random.Range(-horizontalDrift, horizontalDrift);
        _drift = new Vector3(dx, riseSpeed, 0f);
        _timer = 0f;
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Update()
    {
        _timer += Time.deltaTime;

        // Rise only after the immobile delay
        if (_timer > _moveDelay)
            transform.position += _drift * Time.deltaTime;

        // Billboard — always face the camera
        if (_mainCam != null)
            transform.forward = _mainCam.transform.forward;

        // Fade starts after (moveDelay + holdDuration), finishes after fadeDuration
        float visibleElapsed = _timer - _moveDelay;
        float fadeProgress = Mathf.InverseLerp(holdDuration, holdDuration + fadeDuration, visibleElapsed);
        _label.color = new Color(_color.r, _color.g, _color.b, 1f - fadeProgress);

        if (_timer >= _moveDelay + holdDuration + fadeDuration)
            Destroy(gameObject);
    }
}