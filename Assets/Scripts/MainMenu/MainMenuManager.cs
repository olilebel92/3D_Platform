using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// Main menu controller — handles solo Play, Host, Spectate, Settings, and Quit.
/// Attach this to a MainMenu GameObject in your menu scene.
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    // ─── Scene ────────────────────────────────────────────────────────────────

    [Header("Scene")]
    [Tooltip("Exact name of your gameplay scene as it appears in Build Settings.")]
    public string gameSceneName = "GameScene";

    [Tooltip("Exact name of the lobby scene as it appears in Build Settings.")]
    public string lobbySceneName = "LobbyScene";

    // ─── Panels ───────────────────────────────────────────────────────────────

    [Header("Panels")]
    [Tooltip("Root panel of the main menu buttons.")]
    public GameObject mainPanel;

    [Tooltip("Root panel of the settings screen.")]
    public GameObject settingsPanel;

    [Tooltip("Panel shown when entering a host IP to spectate.")]
    public GameObject spectatePanel;

    [Tooltip("Panel shown while connecting or waiting for the host to load the scene.")]
    public GameObject waitingPanel;

    // ─── Main Panel Buttons ───────────────────────────────────────────────────

    [Header("Main Buttons")]
    public Button playButton;
    public Button hostButton;
    public Button spectateButton;
    public Button settingsButton;
    public Button settingsBackButton;
    public Button quitButton;

    // ─── Spectate Panel ───────────────────────────────────────────────────────

    [Header("Spectate Panel")]
    [Tooltip("Input field where the spectator types the host IP address.")]
    public TMP_InputField ipInputField;

    [Tooltip("Port to use. Must match the host. Default: 7777.")]
    public ushort port = 7777;

    public Button connectButton;
    public Button spectateBackButton;

    // ─── Waiting Panel ────────────────────────────────────────────────────────

    [Header("Waiting Panel")]
    [Tooltip("Status label shown while connecting (e.g. 'Connecting...').")]
    public TextMeshProUGUI waitingText;

    public Button cancelButton;

    // ─── Title ────────────────────────────────────────────────────────────────

    [Header("Title")]
    [Tooltip("TMP label showing your game title.")]
    public TextMeshProUGUI titleText;

    [Tooltip("Text to display as the game title.")]
    public string gameTitleString = "YOUR GAME";

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        // Reset timeScale in case we came back from a paused state
        Time.timeScale = 1f;

        // Shut down any active NGO session when returning to the main menu
        ShutdownNetwork();
    }

    void Start()
    {
        if (titleText != null)
            titleText.text = gameTitleString;

        // ── Wire all buttons ──
        if (playButton != null)         playButton.onClick.AddListener(OnPlay);
        if (hostButton != null)         hostButton.onClick.AddListener(OnHost);
        if (spectateButton != null)     spectateButton.onClick.AddListener(OnSpectate);
        if (settingsButton != null)     settingsButton.onClick.AddListener(OnSettings);
        if (settingsBackButton != null) settingsBackButton.onClick.AddListener(OnSettingsBack);
        if (quitButton != null)         quitButton.onClick.AddListener(OnQuit);
        if (connectButton != null)      connectButton.onClick.AddListener(OnConnect);
        if (spectateBackButton != null) spectateBackButton.onClick.AddListener(OnSpectateBack);
        if (cancelButton != null)       cancelButton.onClick.AddListener(OnCancel);

        ShowMain();
    }

    // ─── Main Panel Handlers ──────────────────────────────────────────────────

    /// <summary>Solo play — no networking, loads the scene directly.</summary>
    void OnPlay()
    {
        Debug.Log("[MainMenu] Solo play — loading: " + gameSceneName);

        ShutdownNetwork();

        if (SceneTransitionManager.Instance != null)
            SceneTransitionManager.Instance.LoadScene(gameSceneName);
        else
            SceneManager.LoadScene(gameSceneName);
    }

    /// <summary>
    /// Starts as Host then immediately loads the game scene.
    /// NGO's scene manager ensures the spectator client is also moved to the game scene
    /// once they connect.
    /// </summary>
    void OnHost()
    {
        if (!ValidateNetworkManager()) return;

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
            transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");

        Debug.Log("[MainMenu] Starting as Host — loading lobby...");
        NetworkManager.Singleton.StartHost();

        // Load the lobby. Any spectator who connects will be moved there automatically.
        NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
    }

    void OnSpectate()
    {
        ShowSpectate();
    }

    void OnSettings()
    {
        if (mainPanel != null)   mainPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    void OnSettingsBack()
    {
        ShowMain();
    }

    void OnQuit()
    {
        Debug.Log("[MainMenu] Quitting.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ─── Spectate Panel Handlers ──────────────────────────────────────────────

    /// <summary>Reads the IP field, connects as a client, then waits for the host to load the scene.</summary>
    void OnConnect()
    {
        if (!ValidateNetworkManager()) return;

        string ip = ipInputField != null ? ipInputField.text.Trim() : "127.0.0.1";
        if (string.IsNullOrEmpty(ip))
            ip = "127.0.0.1";

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("[MainMenu] UnityTransport not found on NetworkManager!");
            return;
        }

        transport.SetConnectionData(ip, port);

        NetworkManager.Singleton.OnClientConnectedCallback   += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback  += OnClientDisconnected;

        NetworkManager.Singleton.StartClient();
        Debug.Log($"[MainMenu] Connecting to {ip}:{port}...");

        ShowWaiting("Connecting...");
    }

    void OnSpectateBack()
    {
        ShowMain();
    }

    // ─── Waiting Panel Handlers ───────────────────────────────────────────────

    void OnCancel()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.Shutdown();
        }

        ShowMain();
    }

    // ─── Network Callbacks ────────────────────────────────────────────────────

    void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        Debug.Log("[MainMenu] Connected to host! Waiting for scene load...");
        ShowWaiting("Connected! Loading game...");

        // Unsubscribe — NGO's scene manager takes over from here.
        NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        Debug.LogWarning("[MainMenu] Connection failed or host disconnected.");

        NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        NetworkManager.Singleton.Shutdown();

        ShowWaiting("Could not connect.\nCheck the IP address and try again.");
        // The Cancel button on the waiting panel lets them go back to the main menu.
    }

    // ─── Panel Helpers ────────────────────────────────────────────────────────

    void ShowMain()
    {
        if (mainPanel != null)    mainPanel.SetActive(true);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (spectatePanel != null) spectatePanel.SetActive(false);
        if (waitingPanel != null)  waitingPanel.SetActive(false);
    }

    void ShowSpectate()
    {
        if (mainPanel != null)    mainPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (spectatePanel != null) spectatePanel.SetActive(true);
        if (waitingPanel != null)  waitingPanel.SetActive(false);
    }

    void ShowWaiting(string message)
    {
        if (mainPanel != null)    mainPanel.SetActive(false);
        if (spectatePanel != null) spectatePanel.SetActive(false);
        if (waitingPanel != null)  waitingPanel.SetActive(true);
        if (waitingText != null)   waitingText.text = message;
    }

    // ─── Utility ─────────────────────────────────────────────────────────────

    bool ValidateNetworkManager()
    {
        if (NetworkManager.Singleton != null) return true;
        Debug.LogError("[MainMenu] NetworkManager not found in scene! Add a NetworkManager GameObject.");
        return false;
    }

    static void ShutdownNetwork()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            Debug.Log("[MainMenu] Shutting down NGO session.");
            NetworkManager.Singleton.Shutdown();
        }
    }
}
