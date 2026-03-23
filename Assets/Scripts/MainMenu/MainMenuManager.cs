using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Main menu controller — handles Play, Settings panel toggle, and Quit.
/// Attach this to a MainMenu GameObject in your menu scene.
///
/// Inspector wiring:
///   - gameSceneName   → exact name of your gameplay scene (must be in Build Settings)
///   - settingsPanel   → the Settings sub-panel to show/hide
///   - All button references → drag from Hierarchy
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    // ─── Scene ────────────────────────────────────────────────────────────────

    [Header("Scene")]
    [Tooltip("Exact name of your gameplay scene as it appears in Build Settings.")]
    public string gameSceneName = "GameScene";

    // ─── UI References ────────────────────────────────────────────────────────

    [Header("Panels")]
    [Tooltip("Root panel of the main menu buttons.")]
    public GameObject mainPanel;

    [Tooltip("Root panel of the settings screen.")]
    public GameObject settingsPanel;

    [Header("Buttons")]
    public Button playButton;
    public Button settingsButton;
    public Button settingsBackButton;
    public Button quitButton;

    [Header("Title")]
    [Tooltip("TMP label showing your game title.")]
    public TextMeshProUGUI titleText;

    [Tooltip("Text to display as the game title.")]
    public string gameTitleString = "YOUR GAME";

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        // Make sure timeScale is reset in case we came from a paused popup
        Time.timeScale = 1f;
    }

    void Start()
    {
        // Set title
        if (titleText != null)
            titleText.text = gameTitleString;

        // Wire buttons
        if (playButton != null) playButton.onClick.AddListener(OnPlay);
        if (settingsButton != null) settingsButton.onClick.AddListener(OnSettings);
        if (settingsBackButton != null) settingsBackButton.onClick.AddListener(OnSettingsBack);
        if (quitButton != null) quitButton.onClick.AddListener(OnQuit);

        // Start on main panel
        ShowMain();
    }

    // ─── Button Handlers ──────────────────────────────────────────────────────

    void OnPlay()
    {
        Debug.Log("[MainMenu] Loading game scene: " + gameSceneName);

        if (string.IsNullOrEmpty(gameSceneName))
        {
            Debug.LogWarning("[MainMenu] gameSceneName is empty — assign it in the Inspector!");
            return;
        }

        SceneManager.LoadScene(gameSceneName);
    }

    void OnSettings()
    {
        Debug.Log("[MainMenu] Opening settings.");
        if (mainPanel != null) mainPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    void OnSettingsBack()
    {
        Debug.Log("[MainMenu] Closing settings.");
        ShowMain();
    }

    void OnQuit()
    {
        Debug.Log("[MainMenu] Quitting application.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void ShowMain()
    {
        if (mainPanel != null) mainPanel.SetActive(true);
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }
}