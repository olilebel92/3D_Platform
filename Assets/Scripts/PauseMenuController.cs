using UnityEngine;
using UnityEngine.InputSystem;          // InputAction.WasPressedThisFrame() extension
using UnityEngine.SceneManagement;
using Unity.Netcode;

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
    private InventoryUI      _inventoryUI;
    private CharacterWindow  _characterWindow;
    private SkillTreeManager _skillTreeManager;

    // ─── Unity ────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (pauseMenuPanel != null)
            pauseMenuPanel.SetActive(false);

        _inventoryUI      = FindFirstObjectByType<InventoryUI>();
        _characterWindow  = FindFirstObjectByType<CharacterWindow>();
        _skillTreeManager = FindFirstObjectByType<SkillTreeManager>();
    }

    private void Update()
    {
        // UI.Cancel = Esc / B-East          → closes the topmost open panel.
        // UI.Pause  = Esc / Start           → also closes panels AND toggles the
        //                                     pause menu when nothing else is open.
        // closePressed = either action fires (any input that should close a panel).
        // pausePressed = Pause only (B-East alone must not open the pause menu).
        bool closePressed = InputManager.UI.Cancel.WasPressedThisFrame() ||
                            InputManager.UI.Pause.WasPressedThisFrame();
        bool pausePressed = InputManager.UI.Pause.WasPressedThisFrame();

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

            if (_skillTreeManager != null && _skillTreeManager.IsOpen)
            {
                _skillTreeManager.CloseWindow();
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

        // Shut down any active NGO session before loading the main menu.
        // This must happen here (game scene) — not after — so NGO doesn't
        // intercept the scene load or leave remote clients in a broken state.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            Debug.Log("[PauseMenuController] Shutting down NGO session before returning to main menu.");
            NetworkManager.Singleton.Shutdown();
        }

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

        if (paused) CursorManager.Instance?.OpenMenu();
        else        CursorManager.Instance?.CloseMenu();

        Debug.Log("[PauseMenuController] " + (paused ? "Paused." : "Resumed."));
    }
}
