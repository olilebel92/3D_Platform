using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Singleton screen-space popup used for tutorial hints and dialogue.
///
/// Usage:
///   PopupManager.Instance.Show(entries);                      // sequence of lines
///   PopupManager.Instance.Show("Single message", duration);   // quick one-liner
///   PopupManager.Instance.Hide();                             // force-close
///
/// Wire up in the Inspector:
///   - popupPanel        → root Panel GameObject
///   - messageText       → TMP label for the message body
///   - continueButton    → (optional) "Continue / Skip" button
///   - progressBar       → (optional) Image set to Filled / Horizontal for the timer bar
/// </summary>
public class PopupManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static PopupManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("UI References")]
    [Tooltip("Root panel to show/hide.")]
    public GameObject popupPanel;

    [Tooltip("TMP label that displays the message.")]
    public TextMeshProUGUI messageText;

    [Tooltip("(Optional) Button the player can click to advance / skip.")]
    public Button continueButton;

    [Tooltip("(Optional) Filled Image used as a countdown progress bar.")]
    public Image progressBar;

    [Header("Timing")]
    [Tooltip("Default seconds each entry stays visible before auto-advancing.")]
    public float defaultDuration = 4f;

    [Tooltip("Seconds to wait before input / auto-advance is accepted (prevents accidental skips).")]
    public float inputDelay = 0.3f;

    [Header("Animation")]
    [Tooltip("Fade in/out duration in seconds. Set to 0 to disable.")]
    public float fadeDuration = 0.15f;

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fired when the entire sequence finishes (all entries shown).</summary>
    public event Action OnSequenceComplete;

    // ─── State ────────────────────────────────────────────────────────────────

    /// <summary>
    /// True while a popup is visible. Panels check this before allowing themselves to open.
    /// </summary>
    public static bool IsShowing { get; private set; }

    // Reset static state on each play session so IsShowing never leaks across domain reloads.
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() => IsShowing = false;

    // ─── Private State ────────────────────────────────────────────────────────

    private Queue<DialogueEntry> _queue = new Queue<DialogueEntry>();
    private Coroutine _activeCoroutine;
    private Coroutine _fadePanelCoroutine;  // tracked separately — nested coroutines survive StopCoroutine on the outer one
    private CanvasGroup _canvasGroup;
    private bool _inputReady = false;
    private bool _skipRequested = false;
    private bool _pauseRequested = false;   // tracks whether we've called RequestPause so we can always pair it with ReleasePause
    private bool _pauseGameOnShow = true;   // set to false for popups that should not freeze the game (e.g. movement tutorial)

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Cache or add CanvasGroup for fading
        if (popupPanel != null)
        {
            _canvasGroup = popupPanel.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = popupPanel.AddComponent<CanvasGroup>();
        }

        // Wire the continue button
        if (continueButton != null)
            continueButton.onClick.AddListener(OnContinuePressed);

        // Start hidden
        SetPanelVisible(false, instant: true);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Show a sequence of DialogueEntry lines one after another.</summary>
    /// <param name="pauseGame">
    /// When true (default) the game is paused while the popup is visible.
    /// Pass false for popups where the player should remain in control (e.g. movement tutorials).
    /// </param>
    public void Show(IEnumerable<DialogueEntry> entries, bool pauseGame = true)
    {
        StopActive();

        _queue.Clear();
        foreach (var e in entries)
            _queue.Enqueue(e);

        _pauseGameOnShow = pauseGame;

        // Close all open panels so camera lock and pause state are clean
        CloseAllPanels();

        // RequestPause is deferred to RunSequence() (after one frame yield) so that
        // NetworkManager.IsListening is stable before PauseManager checks it.
        // Calling it here risks setting Time.timeScale=0 in MP if the popup fires
        // before the NGO session is fully established (e.g. start-of-game tutorial).
        IsShowing = true;

        _activeCoroutine = StartCoroutine(RunSequence());
    }

    /// <summary>Show a single quick message with an optional custom duration.</summary>
    /// <param name="pauseGame">
    /// When true (default) the game is paused while the popup is visible.
    /// Pass false for popups where the player should remain in control (e.g. movement tutorials).
    /// </param>
    public void Show(string message, float duration = 0f, bool pauseGame = true)
    {
        Show(new[]
        {
            new DialogueEntry
            {
                message        = message,
                customDuration = duration
            }
        },
        pauseGame: pauseGame);
    }

    /// <summary>Force-close the popup immediately.</summary>
    public void Hide()
    {
        StopActive();   // stops RunSequence AND any in-progress fade; releases pause if needed
        _queue.Clear();
        IsShowing = false;
        _fadePanelCoroutine = StartCoroutine(FadePanel(false));
    }

    // ─── Panel Management ─────────────────────────────────────────────────────
    // Closes all game panels before showing a popup to prevent camera/pause conflicts.

    private void CloseAllPanels()
    {
        CharacterWindow cw = FindFirstObjectByType<CharacterWindow>();
        if (cw != null) cw.CloseWindow();

        InventoryUI inv = FindFirstObjectByType<InventoryUI>();
        if (inv != null) inv.CloseInventory();
    }

    // ─── Sequence Coroutine ───────────────────────────────────────────────────

    private IEnumerator RunSequence()
    {
        // Set the first entry's text before fading in so the old message never flashes through
        if (_queue.Count > 0 && messageText != null)
            messageText.text = _queue.Peek().message;

        // Fade in first so the popup is visible before the game freezes.
        // Track the coroutine so StopActive() can kill it if Hide() is called mid-fade.
        _fadePanelCoroutine = StartCoroutine(FadePanel(true));
        yield return _fadePanelCoroutine;
        _fadePanelCoroutine = null;

        // Request pause after the popup is fully visible.
        // The one-frame yield also ensures NetworkManager.IsListening is stable (avoids
        // briefly setting timeScale=0 before NGO is connected in early-scene popups).
        if (_pauseGameOnShow)
        {
            yield return null;
            _pauseRequested = true;
            PauseManager.RequestPause();
        }

        while (_queue.Count > 0)
        {
            DialogueEntry entry = _queue.Dequeue();
            yield return StartCoroutine(ShowEntry(entry));
        }

        _fadePanelCoroutine = StartCoroutine(FadePanel(false));
        yield return _fadePanelCoroutine;
        _fadePanelCoroutine = null;

        _pauseRequested = false;
        PauseManager.ReleasePause();
        IsShowing = false;
        OnSequenceComplete?.Invoke();
        Debug.Log("[PopupManager] Sequence complete.");
    }

    private IEnumerator ShowEntry(DialogueEntry entry)
    {
        if (messageText != null)
            messageText.text = entry.message;

        SetProgressBar(1f);

        // Short grace period before accepting input (unscaled — runs while game is paused)
        _inputReady = false;
        yield return new WaitForSecondsRealtime(inputDelay);
        _inputReady = true;

        float duration = entry.customDuration > 0f ? entry.customDuration : defaultDuration;
        float elapsed  = 0f;
        bool  skipped  = false;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetProgressBar(1f - (elapsed / duration));

            if (_inputReady && WasSkipPressed())
            {
                skipped = true;
                break;
            }

            yield return null;
        }

        SetProgressBar(0f);

        Debug.Log(skipped
            ? $"[PopupManager] Entry skipped: \"{entry.message}\""
            : $"[PopupManager] Entry auto-advanced: \"{entry.message}\"");
    }

    // ─── Input ────────────────────────────────────────────────────────────────

    private void OnContinuePressed() => _skipRequested = true;

    private bool WasSkipPressed()
    {
        if (_skipRequested) { _skipRequested = false; return true; }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.spaceKey.wasPressedThisFrame ||
                Keyboard.current.eKey.wasPressedThisFrame)
                return true;
        }

        if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
            return true;

        return false;
    }

    // ─── Panel Visibility & Fading ────────────────────────────────────────────

    private void SetPanelVisible(bool visible, bool instant = false)
    {
        if (popupPanel == null) return;

        if (instant)
        {
            popupPanel.SetActive(visible);
            if (_canvasGroup != null)
                _canvasGroup.alpha = visible ? 1f : 0f;
            return;
        }

        popupPanel.SetActive(visible || fadeDuration > 0f);
    }

    private IEnumerator FadePanel(bool fadeIn)
    {
        if (_canvasGroup == null || fadeDuration <= 0f)
        {
            SetPanelVisible(fadeIn, instant: true);
            yield break;
        }

        popupPanel.SetActive(true);
        float start   = fadeIn ? 0f : 1f;
        float end     = fadeIn ? 1f : 0f;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Lerp(start, end, elapsed / fadeDuration);
            yield return null;
        }

        _canvasGroup.alpha = end;
        if (!fadeIn) popupPanel.SetActive(false);
    }

    // ─── Progress Bar ─────────────────────────────────────────────────────────

    private void SetProgressBar(float fillAmount)
    {
        if (progressBar != null)
            progressBar.fillAmount = Mathf.Clamp01(fillAmount);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void StopActive()
    {
        if (_activeCoroutine != null)
        {
            StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
        }

        // Stop any in-progress fade. Nested coroutines survive StopCoroutine on the outer
        // coroutine, so FadePanel must be tracked and killed separately to prevent it from
        // completing and making the popup visible again after Hide() was called.
        if (_fadePanelCoroutine != null)
        {
            StopCoroutine(_fadePanelCoroutine);
            _fadePanelCoroutine = null;
            // Snap the panel to fully hidden so no partial alpha remains.
            SetPanelVisible(false, instant: true);
        }

        // If RunSequence had already called RequestPause before being stopped,
        // release it now so the pause count never leaks.
        if (_pauseRequested)
        {
            _pauseRequested = false;
            PauseManager.ReleasePause();
        }

        _skipRequested = false;
        _inputReady    = false;
    }
}
