using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles all tutorial popups in one place:
///   • Start-of-game hint (shown after <see cref="showDelay"/> seconds, dismissed on movement)
///   • Level-based hints (shown when the player reaches a specific level, dismissed on
///     movement OR after their individual <see cref="LevelHint.duration"/> seconds)
///
/// All popups are routed through PopupManager so nothing is duplicated.
/// Attach to any persistent GameObject in your opening scene.
/// </summary>
public class MovementTutorialTrigger : MonoBehaviour
{
    // ─── Level Hint Entry ─────────────────────────────────────────────────────

    [System.Serializable]
    public class LevelHint
    {
        [Tooltip("Player level that triggers this hint.")]
        public int triggerLevel = 3;

        [Tooltip("Message shown in the popup.")]
        [TextArea(1, 3)]
        public string message = "Press C to open the Character window and spend your Stat Points!";

        [Tooltip("Seconds before the popup auto-dismisses. Use 9999 to keep until the player moves.")]
        public float duration = 3f;
    }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Start-of-Game Hint")]
    [Tooltip("Message shown to the player when the game first loads.")]
    public string message = "Use WASD or left stick to move";

    [Tooltip("Seconds to wait after the scene loads before showing the popup.")]
    public float showDelay = 1.5f;

    [Tooltip("Seconds to wait after the popup appears before accepting movement input (prevents instant dismiss).")]
    public float inputDelay = 0.5f;

    [Header("Level Hints")]
    [Tooltip("One entry per level-triggered hint. Add, remove, or reorder freely.")]
    public List<LevelHint> levelHints = new()
    {
        new LevelHint
        {
            triggerLevel = 3,
            message      = "Press C to open the Character window and spend your Stat Points!",
            duration     = 3f,
        },
    };

    // ─── Private State ────────────────────────────────────────────────────────

    private bool          _activeHint  = false;   // a popup is currently showing
    private float         _inputTimer  = 0f;      // grace period before movement is accepted
    private float         _autoTimer   = 0f;      // counts up toward auto-dismiss
    private float         _autoDuration = 9999f;  // duration of the current hint

    private bool          _startDismissed = false;
    private readonly HashSet<int> _shownLevels = new();

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    // Track which XP manager we subscribed to so we can unsubscribe cleanly.
    private ExperienceManager _subscribedXP;

    void Start()
    {
        if (PopupManager.Instance == null)
        {
            Debug.LogWarning("[MovementTutorialTrigger] PopupManager instance not found in scene!");
            return;
        }

        StartCoroutine(ShowStartHintAfterDelay());

        // ExperienceManager.Instance is set by PlayerController.OnNetworkSpawn which
        // fires AFTER scene objects' Start(). Poll until it's available.
        StartCoroutine(WaitForXPManager());
    }

    private IEnumerator WaitForXPManager()
    {
        while (ExperienceManager.Instance == null)
            yield return null;

        _subscribedXP = ExperienceManager.Instance;
        _subscribedXP.OnLevelUp += OnLevelUp;
        Debug.Log("[MovementTutorialTrigger] Subscribed to ExperienceManager.OnLevelUp.");
    }

    void OnDestroy()
    {
        if (_subscribedXP != null)
            _subscribedXP.OnLevelUp -= OnLevelUp;
    }

    void Update()
    {
        if (!_activeHint) return;

        // Grace period: don't accept movement input right after the popup appears
        _inputTimer += Time.unscaledDeltaTime;
        if (_inputTimer < inputDelay) return;

        // Auto-dismiss timer (for level hints with a finite duration)
        _autoTimer += Time.unscaledDeltaTime;
        if (_autoTimer >= _autoDuration)
        {
            Dismiss();
            return;
        }

        // Movement dismiss
        if (PlayerIsMoving())
            Dismiss();
    }

    // ─── Hint Show / Dismiss ─────────────────────────────────────────────────

    private IEnumerator ShowStartHintAfterDelay()
    {
        yield return new WaitForSecondsRealtime(showDelay);
        ShowHint(message, 9999f);
        Debug.Log("[MovementTutorialTrigger] Start hint shown.");
    }

    private void OnLevelUp(int newLevel)
    {
        if (_shownLevels.Contains(newLevel)) return;

        foreach (var hint in levelHints)
        {
            if (hint.triggerLevel != newLevel) continue;

            _shownLevels.Add(newLevel);

            // Small delay so the level-up audio / effects play first
            StartCoroutine(ShowLevelHintNextFrame(hint));
            return;
        }
    }

    private IEnumerator ShowLevelHintNextFrame(LevelHint hint)
    {
        yield return null;   // wait one frame
        ShowHint(hint.message, hint.duration);
        Debug.Log($"[MovementTutorialTrigger] Level {hint.triggerLevel} hint shown.");
    }

    private void ShowHint(string msg, float duration)
    {
        PopupManager.Instance.Show(msg, duration: 9999f);  // we manage dismiss ourselves
        _activeHint   = true;
        _inputTimer   = 0f;
        _autoTimer    = 0f;
        _autoDuration = duration;
    }

    private void Dismiss()
    {
        _activeHint = false;
        if (!_startDismissed)
            _startDismissed = true;

        PopupManager.Instance.Hide();
        Debug.Log("[MovementTutorialTrigger] Popup dismissed.");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private bool PlayerIsMoving()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed ||
                Keyboard.current.aKey.isPressed ||
                Keyboard.current.sKey.isPressed ||
                Keyboard.current.dKey.isPressed)
                return true;
        }

        if (Gamepad.current != null)
        {
            if (Gamepad.current.leftStick.ReadValue().magnitude > 0.2f)
                return true;
        }

        return false;
    }
}
