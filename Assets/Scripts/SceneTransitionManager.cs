using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Handles smooth fade-to-black transitions between scenes.
///
/// Setup (do this in EVERY scene):
///   1. Create a Canvas (Sort Order 999, Screen Space - Overlay)
///   2. Add a child Image that fills the whole canvas (black, Raycast Target OFF)
///   3. Add a CanvasGroup component to that Image
///   4. Attach this script to the Canvas root
///   5. Drag the CanvasGroup into the fadeGroup slot in the Inspector
/// </summary>
public class SceneTransitionManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static SceneTransitionManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Fade Settings")]
    [Tooltip("CanvasGroup on the full-screen black overlay Image.")]
    public CanvasGroup fadeGroup;

    [Tooltip("Duration of the fade-in (black → clear) when a scene starts.")]
    public float fadeInDuration = 0.6f;

    [Tooltip("Duration of the fade-out (clear → black) before a scene loads.")]
    public float fadeOutDuration = 0.4f;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        // Always become the active instance — never destroy the Canvas.
        // During NGO scene transitions the old scene's Instance may still be alive
        // when this Awake fires; destroying gameObject would kill the new HUD Canvas.
        Instance = this;

        // Start fully black so the fade-in plays immediately.
        if (fadeGroup != null)
        {
            fadeGroup.alpha = 1f;
            fadeGroup.blocksRaycasts = true;
        }
    }

    void Start()
    {
        StartCoroutine(DoFadeIn());
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Fade to black then load the given scene by name.</summary>
    public void LoadScene(string sceneName)
    {
        StartCoroutine(FadeOutAndLoad(sceneName));
    }

    /// <summary>Fade to black then load the given scene by build index.</summary>
    public void LoadScene(int sceneIndex)
    {
        StartCoroutine(FadeOutAndLoad(sceneIndex));
    }

    /// <summary>Fade to black then invoke a callback (used by DeathScreenManager).</summary>
    public void FadeOutThen(System.Action onComplete)
    {
        StartCoroutine(FadeOutAndCallback(onComplete));
    }

    /// <summary>Load a scene immediately while keeping the screen black — use when already faded out.</summary>
    public void LoadSceneAlreadyFaded(int sceneIndex)
    {
        if (fadeGroup != null)
        {
            fadeGroup.alpha = 1f;
            fadeGroup.blocksRaycasts = true;
        }
        SceneManager.LoadScene(sceneIndex);
    }

    // ─── Coroutines ───────────────────────────────────────────────────────────

    /// <summary>Fades the screen from black to clear. Safe to call after a respawn.</summary>
    public void FadeIn() => StartCoroutine(DoFadeIn());

    private IEnumerator DoFadeIn()
    {
        if (fadeGroup == null) yield break;

        fadeGroup.blocksRaycasts = true;
        float elapsed = 0f;

        while (elapsed < fadeInDuration)
        {
            // Use unscaledDeltaTime so the fade works even if Time.timeScale is 0
            // (e.g. a panel paused the game before the scene finished loading).
            elapsed += Time.unscaledDeltaTime;
            fadeGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeInDuration);
            yield return null;
        }

        fadeGroup.alpha = 0f;
        fadeGroup.blocksRaycasts = false;

        Debug.Log("[SceneTransitionManager] Fade in complete.");
    }

    private IEnumerator FadeOutAndCallback(System.Action onComplete)
    {
        yield return StartCoroutine(FadeOut());
        onComplete?.Invoke();

        // Release the raycast block so UI elements above the overlay (e.g. death screen) are clickable
        if (fadeGroup != null)
            fadeGroup.blocksRaycasts = false;
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

    private IEnumerator FadeOut()
    {
        if (fadeGroup == null) yield break;

        fadeGroup.blocksRaycasts = true;
        float elapsed = 0f;

        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            fadeGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeOutDuration);
            yield return null;
        }

        fadeGroup.alpha = 1f;

        Debug.Log("[SceneTransitionManager] Fade out complete.");
    }
}
