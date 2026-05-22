using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Displays a short screen-centre message that fades out automatically.
/// Place this component anywhere in the HUD hierarchy and wire _label to a
/// centred TextMeshProUGUI on the Canvas.
/// </summary>
public class HudMessageUI : MonoBehaviour
{
    public static HudMessageUI Instance { get; private set; }

    [SerializeField] private TMP_Text _label;

    [Header("Colors")]
    [SerializeField] private Color _noManaColor = new Color(0.3f, 0.55f, 1f);

    [Header("Timing")]
    [SerializeField] private float _holdDuration = 1f;
    [SerializeField] private float _fadeDuration = 0.4f;

    private Coroutine _fadeRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (_label != null) _label.alpha = 0f;

        int hudLayer = LayerMask.NameToLayer("HudOverlay");
        if (hudLayer >= 0 && _label != null) _label.gameObject.layer = hudLayer;
    }

    public void ShowNoMana() => Show("No Mana", _noManaColor);

    public void Show(string text, Color color)
    {
        if (_label == null) return;
        _label.text  = text;
        _label.color = new Color(color.r, color.g, color.b, 1f);
        _label.alpha = 1f;
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeOut());
    }

    private IEnumerator FadeOut()
    {
        yield return new WaitForSecondsRealtime(_holdDuration);
        float elapsed = 0f;
        while (elapsed < _fadeDuration)
        {
            elapsed    += Time.unscaledDeltaTime;
            _label.alpha = 1f - Mathf.Clamp01(elapsed / _fadeDuration);
            yield return null;
        }
        _label.alpha = 0f;
    }
}
