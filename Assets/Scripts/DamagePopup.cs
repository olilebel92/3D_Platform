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
    private Vector3 _drift;
    private Color _color;

    // ─── Public Setup ─────────────────────────────────────────────────────────

    /// <summary>
    /// Called by DamagePopupManager immediately after instantiation.
    /// </summary>
    /// <param name="amount">Value to display.</param>
    /// <param name="color">Text color.</param>
    /// <param name="prefix">Optional prefix prepended to the number, e.g. "+".</param>
    /// <param name="suffix">Optional suffix appended after the number, e.g. " EXP".</param>
    public void Initialize(int amount, Color color, string prefix = "", string suffix = "")
    {
        _label = GetComponent<TextMeshPro>();
        if (_label == null)
        {
            Debug.LogWarning("[DamagePopup] No TextMeshPro component found!");
            Destroy(gameObject);
            return;
        }

        _mainCam = Camera.main;

        _label.text = prefix + amount.ToString() + suffix;
        _color = color;
        _label.color = _color;

        float dx = Random.Range(-horizontalDrift, horizontalDrift);
        _drift = new Vector3(dx, riseSpeed, 0f);
        _timer = 0f;
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Update()
    {
        _timer += Time.deltaTime;

        // Rise
        transform.position += _drift * Time.deltaTime;

        // Billboard — always face the camera
        if (_mainCam != null)
            transform.forward = _mainCam.transform.forward;

        // Fade out after hold phase
        float fadeProgress = Mathf.InverseLerp(holdDuration, holdDuration + fadeDuration, _timer);
        _label.color = new Color(_color.r, _color.g, _color.b, 1f - fadeProgress);

        if (_timer >= holdDuration + fadeDuration)
            Destroy(gameObject);
    }
}