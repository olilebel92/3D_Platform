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

    // ─── Private State ────────────────────────────────────────────────────────

    private Queue<DialogueEntry> _queue = new Queue<DialogueEntry>();
    private Coroutine _activeCoroutine;
    private CanvasGroup _canvasGroup;
    private bool _inputReady = false;

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
    public void Show(IEnumerable<DialogueEntry> entries)
    {
        StopActive();

        _queue.Clear();
        foreach (var e in entries)
            _queue.Enqueue(e);

        Time.timeScale = 0f;
        Debug.Log("[PopupManager] Game paused.");

        _activeCoroutine = StartCoroutine(RunSequence());
    }

    /// <summary>Show a single quick message with an optional custom duration.</summary>
    public void Show(string message, float duration = 0f)
    {
        Show(new[]
        {
            new DialogueEntry
            {
                message = message,
                customDuration = duration
            }
        });
    }

    /// <summary>Force-close the popup immediately.</summary>
    public void Hide()
    {
        StopActive();
        _queue.Clear();
        Time.timeScale = 1f;
        Debug.Log("[PopupManager] Game resumed.");
        StartCoroutine(FadePanel(false));
    }

    // ─── Sequence Coroutine ───────────────────────────────────────────────────

    private IEnumerator RunSequence()
    {
        yield return StartCoroutine(FadePanel(true));

        while (_queue.Count > 0)
        {
            DialogueEntry entry = _queue.Dequeue();
            yield return StartCoroutine(ShowEntry(entry));
        }

        yield return StartCoroutine(FadePanel(false));
        Time.timeScale = 1f;
        Debug.Log("[PopupManager] Game resumed.");
        OnSequenceComplete?.Invoke();
        Debug.Log("[PopupManager] Sequence complete.");
    }

    private IEnumerator ShowEntry(DialogueEntry entry)
    {
        // Display text
        if (messageText != null)
            messageText.text = entry.message;

        // Reset progress bar
        SetProgressBar(1f);

        // Short grace period before accepting input (unscaled — runs while game is paused)
        _inputReady = false;
        yield return new WaitForSecondsRealtime(inputDelay);
        _inputReady = true;

        float duration = entry.customDuration > 0f ? entry.customDuration : defaultDuration;
        float elapsed = 0f;
        bool skipped = false;

        // Count down — player can skip at any point after inputDelay
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetProgressBar(1f - (elapsed / duration));

            if (_inputReady && WasSkipPressed())
            {
                skipped = true;
                break;
            }

            yield return null;
        }

        SetProgressBar(0f);

        if (!skipped)
            Debug.Log($"[PopupManager] Entry auto-advanced: \"{entry.message}\"");
        else
            Debug.Log($"[PopupManager] Entry skipped: \"{entry.message}\"");
    }

    // ─── Input ────────────────────────────────────────────────────────────────

    /// <summary>Called by the Continue button click event.</summary>
    private void OnContinuePressed()
    {
        // Handled via WasSkipPressed polling — button click sets a flag instead
        // so it works within the coroutine's while loop.
        _skipRequested = true;
    }

    private bool _skipRequested = false;

    private bool WasSkipPressed()
    {
        // Consume button click flag
        if (_skipRequested)
        {
            _skipRequested = false;
            return true;
        }

        // Keyboard: Enter, Space, or E
        if (Keyboard.current != null)
        {
            if (Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.spaceKey.wasPressedThisFrame ||
                Keyboard.current.eKey.wasPressedThisFrame)
                return true;
        }

        // Gamepad: South button (A / Cross)
        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.wasPressedThisFrame)
                return true;
        }

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
        float start = fadeIn ? 0f : 1f;
        float end = fadeIn ? 1f : 0f;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Lerp(start, end, elapsed / fadeDuration);
            yield return null;
        }

        _canvasGroup.alpha = end;

        if (!fadeIn)
            popupPanel.SetActive(false);
    }

    // ─── Progress Bar Helper ──────────────────────────────────────────────────

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

        _skipRequested = false;
        _inputReady = false;
    }
}