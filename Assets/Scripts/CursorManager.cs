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

    // ─── Bootstrap ───────────────────────────────────────────────────────────

    // BeforeSceneLoad guarantees coverage from frame 0. A scene-placed CursorManager
    // (with textures configured) replaces the bare auto instance in its own Awake.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureExists()
    {
        if (Instance != null) return;
        var go = new GameObject("CursorManager [auto]");
        go.AddComponent<CursorManager>();
    }

    // ─── Unity ────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // A scene-placed (configured) CursorManager replaces the bare auto instance.
            Destroy(Instance.gameObject);
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        RefreshVisibility();
        ApplyDefault();
    }

    void OnEnable()  => InputSystem.onDeviceChange += OnDeviceChange;
    void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

    void Start() { }

    void Update()
    {
        // wasUpdatedThisFrame fires on the plug-in frame even without real input;
        // require non-default state so that mere device connection never triggers a switch.
        bool gamepadActive = Gamepad.current != null &&
                             Gamepad.current.wasUpdatedThisFrame &&
                             !Gamepad.current.CheckStateIsAtDefault();

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

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        // When the active gamepad is unplugged, immediately revert to mouse mode.
        if (device is Gamepad && change == InputDeviceChange.Removed && _usingGamepad)
        {
            if (Gamepad.current == null)
            {
                _usingGamepad = false;
                RefreshVisibility();
                ApplyDefault();
            }
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
        // ForceSoftware with a null texture triggers UI Toolkit warnings; fall back to Auto (OS default).
        Cursor.SetCursor(texture, hotspot, texture != null ? CursorMode.ForceSoftware : CursorMode.Auto);
    }
}
