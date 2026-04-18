using UnityEngine;
using UnityEngine.InputSystem;

public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance { get; private set; }

    [Header("Default Cursor")]
    [SerializeField] private Texture2D defaultCursor;
    [SerializeField] private Vector2   defaultHotspot = Vector2.zero;

    [Header("Skill Targeting Cursors")]
    [Tooltip("Cursor shown while in targeting mode but not hovering over an enemy.")]
    [SerializeField] private Texture2D targetingCursor;
    [SerializeField] private Vector2   targetingHotspot = Vector2.zero;

    [Tooltip("Cursor shown when hovering over an enemy during targeting mode.")]
    [SerializeField] private Texture2D enemyHoverCursor;
    [SerializeField] private Vector2   enemyHoverHotspot = Vector2.zero;

    // ─── State ────────────────────────────────────────────────────────────────

    private bool _usingGamepad = false;
    private int  _menuCount    = 0;   // incremented per open menu; cursor always visible when > 0

    // ─── Unity ────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start() => ApplyDefault();

    void Update()
    {
        bool gamepadActive = Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame;
        bool mouseActive   = Mouse.current   != null &&
                             (Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f ||
                              Mouse.current.leftButton.wasPressedThisFrame          ||
                              Mouse.current.rightButton.wasPressedThisFrame);

        if (!_usingGamepad && gamepadActive)
        {
            _usingGamepad = true;
            RefreshVisibility();
        }
        else if (_usingGamepad && mouseActive)
        {
            _usingGamepad = false;
            RefreshVisibility();
            ApplyDefault();
        }
    }

    // ─── Menu Tracking ────────────────────────────────────────────────────────

    /// <summary>Call when any menu/panel opens — forces cursor visible even on gamepad.</summary>
    public void OpenMenu()
    {
        _menuCount++;
        RefreshVisibility();
    }

    /// <summary>Call when any menu/panel closes — hides cursor again if on gamepad and no menus remain.</summary>
    public void CloseMenu()
    {
        _menuCount = Mathf.Max(0, _menuCount - 1);
        RefreshVisibility();
    }

    // ─── Cursor Setters ───────────────────────────────────────────────────────

    public void ApplyDefault()
    {
        if (_usingGamepad && _menuCount == 0) return;
        Set(defaultCursor, defaultHotspot);
    }

    public void ApplyTargeting()
    {
        if (_usingGamepad && _menuCount == 0) return;
        Set(targetingCursor != null ? targetingCursor : defaultCursor,
            targetingCursor != null ? targetingHotspot : defaultHotspot);
    }

    public void ApplyEnemyHover()
    {
        if (_usingGamepad && _menuCount == 0) return;
        Set(enemyHoverCursor  != null ? enemyHoverCursor  : targetingCursor != null ? targetingCursor : defaultCursor,
            enemyHoverCursor  != null ? enemyHoverHotspot : targetingCursor != null ? targetingHotspot : defaultHotspot);
    }

    public void ApplyCursor(Texture2D texture, Vector2 hotspot)
    {
        if (_usingGamepad && _menuCount == 0) return;
        Set(texture, hotspot);
    }

    // ─── Internal ─────────────────────────────────────────────────────────────

    private void RefreshVisibility()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = !_usingGamepad || _menuCount > 0;
    }

    private void Set(Texture2D texture, Vector2 hotspot)
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.SetCursor(texture, hotspot, CursorMode.ForceSoftware);
    }
}
