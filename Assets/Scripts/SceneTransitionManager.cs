using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;

/// <summary>
/// Handles smooth fade-to-black transitions between scenes.
///
/// uGUI setup: assign a CanvasGroup on a full-screen black Image (Canvas Sort Order 999).
/// UI Toolkit setup: assign the scene's UIDocument — the overlay is injected automatically.
/// Only one mode is needed per scene; uGUI takes priority if both are assigned.
/// </summary>
public class SceneTransitionManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static SceneTransitionManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Fade Settings")]
    [Tooltip("Seconds to hold black before the fade-in begins.")]
    public float fadeInDelay = 0f;

    [Tooltip("Duration of the fade-in (black → clear) when a scene starts.")]
    public float fadeInDuration = 0.6f;

    [Tooltip("Duration of the fade-out (clear → black) before a scene loads.")]
    public float fadeOutDuration = 0.4f;

    [Header("uGUI Mode")]
    [Tooltip("CanvasGroup on the full-screen black overlay Image.")]
    public CanvasGroup fadeGroup;

    [Header("UI Toolkit Mode")]
    [Tooltip("The scene's UIDocument — a black overlay VisualElement is injected automatically.")]
    [SerializeField] private UIDocument uiDocument;

    // ─── State ────────────────────────────────────────────────────────────────

    private VisualElement _tkOverlay;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;

        if (fadeGroup != null)
        {
            fadeGroup.alpha = 1f;
            fadeGroup.blocksRaycasts = true;
        }
    }

    void Start()
    {
        if (fadeGroup == null && uiDocument != null)
            InjectTKOverlay();

        StartCoroutine(DoFadeIn());
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public void LoadScene(string sceneName)  => StartCoroutine(FadeOutAndLoad(sceneName));
    public void LoadScene(int sceneIndex)    => StartCoroutine(FadeOutAndLoad(sceneIndex));
    public void FadeOutThen(System.Action onComplete) => StartCoroutine(FadeOutAndCallback(onComplete));
    public void FadeIn() => StartCoroutine(DoFadeIn());

    public void LoadSceneAlreadyFaded(int sceneIndex)
    {
        SetAlpha(1f);
        SetBlocking(true);
        SceneManager.LoadScene(sceneIndex);
    }

    // ─── Internal ─────────────────────────────────────────────────────────────

    private void InjectTKOverlay()
    {
        _tkOverlay = new VisualElement();
        _tkOverlay.style.position = Position.Absolute;
        _tkOverlay.style.width    = new Length(100, LengthUnit.Percent);
        _tkOverlay.style.height   = new Length(100, LengthUnit.Percent);
        _tkOverlay.pickingMode    = PickingMode.Position;
        SetAlpha(1f);
        uiDocument.rootVisualElement.Add(_tkOverlay);
    }

    private void SetAlpha(float a)
    {
        if (fadeGroup != null)
        {
            fadeGroup.alpha = a;
        }
        else if (_tkOverlay != null)
        {
            _tkOverlay.style.backgroundColor = new Color(0f, 0f, 0f, a);
        }
    }

    private void SetBlocking(bool block)
    {
        if (fadeGroup != null)
            fadeGroup.blocksRaycasts = block;
        else if (_tkOverlay != null)
            _tkOverlay.pickingMode = block ? PickingMode.Position : PickingMode.Ignore;
    }

    // ─── Coroutines ───────────────────────────────────────────────────────────

    private IEnumerator DoFadeIn()
    {
        if (fadeGroup == null && _tkOverlay == null) yield break;

        SetBlocking(true);
        SetAlpha(1f);

        if (fadeInDelay > 0f)
            yield return new WaitForSecondsRealtime(fadeInDelay);

        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(1f, 0f, elapsed / fadeInDuration));
            yield return null;
        }

        SetAlpha(0f);
        SetBlocking(false);
        Debug.Log("[SceneTransitionManager] Fade in complete.");
    }

    private IEnumerator FadeOut()
    {
        if (fadeGroup == null && _tkOverlay == null) yield break;

        SetBlocking(true);
        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(0f, 1f, elapsed / fadeOutDuration));
            yield return null;
        }

        SetAlpha(1f);
        Debug.Log("[SceneTransitionManager] Fade out complete.");
    }

    private IEnumerator FadeOutAndLoad(string sceneName)
    {
        yield return StartCoroutine(FadeOut());
        SceneManager.LoadScene(sceneName);
    }

    private IEnumerator FadeOutAndLoad(int sceneIndex)
    {
        yield return StartCoroutine(FadeOut());
        SceneManager.LoadScene(sceneIndex);
    }

    private IEnumerator FadeOutAndCallback(System.Action onComplete)
    {
        yield return StartCoroutine(FadeOut());
        onComplete?.Invoke();
        SetBlocking(false);
    }
}
