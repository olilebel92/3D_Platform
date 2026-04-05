using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles opening/closing the pause menu on ESC (keyboard) or Start (gamepad).
/// Attach to a persistent GameObject in your scene and wire up the Inspector references.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("UI")]
    [Tooltip("Root GameObject of the pause menu panel — toggled on/off.")]
    [SerializeField] private GameObject pauseMenuPanel;

    [Header("Scene")]
    [Tooltip("Exact name of the main menu scene to load on Quit.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    // ─── State ────────────────────────────────────────────────────────────────

    private bool _isPaused = false;
    private InventoryUI     _inventoryUI;
    private CharacterWindow _characterWindow;

    // ─── Unity ────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (pauseMenuPanel != null)
            pauseMenuPanel.SetActive(false);

        _inventoryUI     = FindFirstObjectByType<InventoryUI>();
        _characterWindow = FindFirstObjectByType<CharacterWindow>();
    }

    private void Update()
    {
        // Circle / B closes panels only — never opens the pause menu
        bool closePressed =
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (Gamepad.current  != null && Gamepad.current.buttonEast.wasPressedThisFrame);

        // ESC / Start can also open the pause menu when no panels are open
        bool pausePressed =
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (Gamepad.current  != null && Gamepad.current.startButton.wasPressedThisFrame);

        if (!closePressed && !pausePressed) return;

        // Close topmost open panel on any of the two inputs
        if (closePressed)
        {
            if (_inventoryUI != null && _inventoryUI.IsOpen)
            {
                _inventoryUI.CloseInventory();
                return;
            }

            if (_characterWindow != null && _characterWindow.IsOpen)
            {
                _characterWindow.CloseWindow();
                return;
            }
        }

        // Circle resumes the game if the pause menu is open
        if (closePressed && _isPaused)
        {
            SetPaused(false);
            return;
        }

        // No panels open — only toggle pause if the input was ESC or Start (not Circle alone)
        if (pausePressed)
            Toggle();
    }

    // ─── Public API (called by UI buttons) ────────────────────────────────────

    /// <summary>Close the pause menu and resume the game.</summary>
    public void Resume()
    {
        SetPaused(false);
    }

    /// <summary>Return to the main menu scene.</summary>
    public void Quit()
    {
        // Always release the pause before loading so timeScale is restored
        if (_isPaused)
            PauseManager.ReleasePause();

        _isPaused = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    // ─── Private ──────────────────────────────────────────────────────────────

    private void Toggle()
    {
        SetPaused(!_isPaused);
    }

    private void SetPaused(bool paused)
    {
        _isPaused = paused;

        if (paused)
            PauseManager.RequestPause();
        else
            PauseManager.ReleasePause();

        if (pauseMenuPanel != null)
            pauseMenuPanel.SetActive(paused);

        Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible   = paused;

        Debug.Log("[PauseMenuController] " + (paused ? "Paused." : "Resumed."));
    }
}
