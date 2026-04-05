using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Manages the Lobby scene: player list, name input, class selection,
/// ready system, and scene transition to the game.
///
/// Attach to a persistent GameObject in LobbyScene.
/// Wire all Inspector references.
/// </summary>
public class LobbyManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static LobbyManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Player List")]
    [Tooltip("Vertical Layout Group container where player rows are spawned.")]
    public Transform playerListContainer;

    [Tooltip("Prefab for one row. Must contain a TMP_Text somewhere in its hierarchy.")]
    public GameObject playerEntryPrefab;

    [Header("Name")]
    public TMP_InputField nameInputField;

    [Header("Class Buttons")]
    [Tooltip("Assign in order: Random, Warrior, Mage, Rogue.")]
    public Button[] classButtons;

    [Tooltip("Color applied to the currently selected class button.")]
    public Color selectedClassColor = new Color(0.3f, 0.8f, 0.3f);

    [Tooltip("Default color for unselected class buttons.")]
    public Color defaultClassColor = Color.white;

    [Header("Ready")]
    [Tooltip("Toggles the local player's ready state.")]
    public Button readyButton;

    [Tooltip("Label on the ready button.")]
    public TMP_Text readyButtonText;

    [Header("Host Buttons")]
    [Tooltip("Enabled only when ALL players are ready. Host-only.")]
    public Button startGameButton;

    [Tooltip("Lets the host start even if not everyone is ready. Host-only.")]
    public Button forceStartButton;

    [Header("Session Mode")]
    [Tooltip("Label that shows 'Single Player Session' or 'Multiplayer Mode' based on connected player count.")]
    public TMP_Text sessionModeText;

    [Header("Other")]
    public Button disconnectButton;

    [Header("Scenes")]
    public string gameSceneName     = "GameScene";
    public string mainMenuSceneName = "MainMenu";

    // ─── Private State ────────────────────────────────────────────────────────

    private bool _localReady = false;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        bool networkReady = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        bool isHost       = networkReady && NetworkManager.Singleton.IsHost;

        // Host-only buttons
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(isHost);
            startGameButton.onClick.AddListener(OnStartGame);
        }

        if (forceStartButton != null)
        {
            forceStartButton.gameObject.SetActive(isHost);
            forceStartButton.onClick.AddListener(OnForceStart);
        }

        // Class buttons — one per PlayerClass value (Random=0, Warrior=1, Mage=2, Rogue=3, Spectator=4)
        for (int i = 0; i < classButtons.Length; i++)
        {
            int index = i; // capture for lambda
            if (classButtons[i] != null)
                classButtons[i].onClick.AddListener(() => OnClassSelected(index));
        }

        if (readyButton != null)
            readyButton.onClick.AddListener(OnReadyToggle);

        if (disconnectButton != null)
            disconnectButton.onClick.AddListener(OnDisconnect);

        if (nameInputField != null)
            nameInputField.onEndEdit.AddListener(OnNameSubmitted);

        // Network events
        if (networkReady)
        {
            NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }

        // Pre-fill name field
        SetNameFieldFromPlayer();

        RefreshPlayerList();
        RefreshClassButtons();
        RefreshStartButton();
        RefreshSessionModeText();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    // ─── Network Callbacks ────────────────────────────────────────────────────

    void OnClientConnected(ulong clientId)
    {
        RefreshPlayerList();
        RefreshStartButton();
    }

    void OnClientDisconnected(ulong clientId)
    {
        RefreshPlayerList();
        RefreshStartButton();
    }

    // ─── Player List ──────────────────────────────────────────────────────────

    // All player slot tags are yellow. Host tag is red.
    private static readonly string[] PlayerColors = { "#FFCC00", "#FFCC00", "#FFCC00", "#FFCC00" };

    /// <summary>Rebuilds the player list rows from all active LobbyPlayers.</summary>
    public void RefreshPlayerList()
    {
        RefreshSessionModeText();
        if (playerListContainer == null || playerEntryPrefab == null) return;

        foreach (Transform child in playerListContainer)
            Destroy(child.gameObject);

        // Sort by client ID so the order is always consistent.
        var players = FindObjectsByType<LobbyPlayer>(FindObjectsSortMode.None);
        System.Array.Sort(players, (a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        int playerNumber = 1;
        foreach (LobbyPlayer player in players)
        {
            GameObject entry = Instantiate(playerEntryPrefab, playerListContainer);
            TMP_Text   label = entry.GetComponentInChildren<TMP_Text>();
            if (label == null) { playerNumber++; continue; }

            string color     = PlayerColors[Mathf.Clamp(playerNumber - 1, 0, PlayerColors.Length - 1)];
            string pTag      = $"<color={color}>[P{playerNumber}]</color>";
            string hostTag   = player.OwnerClientId == 0 ? " <color=#FF4444>[Host]</color>" : "";
            string name      = player.PlayerName.Value.ToString();
            string className = ((LobbyPlayer.PlayerClass)player.SelectedClass.Value).ToString();
            string ready     = player.IsReady.Value ? "<color=#44FF44>[Ready]</color>" : "<color=#FF4444>[Not Ready]</color>";

            label.text = $"{pTag}{hostTag} {name}  —  {className}  —  {ready}";
            playerNumber++;
        }
    }

    // ─── Session Mode Label ───────────────────────────────────────────────────

    /// <summary>Updates the session mode label based on how many players are connected.</summary>
    void RefreshSessionModeText()
    {
        if (sessionModeText == null) return;

        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        int  playerCount   = networkActive ? NetworkManager.Singleton.ConnectedClientsIds.Count : 1;

        sessionModeText.text = playerCount > 1 ? "Multiplayer Mode" : "Single Player Session";
    }

    // ─── Start Button ─────────────────────────────────────────────────────────

    /// <summary>Enables Start Game only when every connected player is ready.</summary>
    public void RefreshStartButton()
    {
        RefreshSessionModeText();
        if (startGameButton == null) return;
        startGameButton.interactable = AllPlayersReady();
    }

    bool AllPlayersReady()
    {
        var players = FindObjectsByType<LobbyPlayer>(FindObjectsSortMode.None);
        if (players.Length == 0) return false;
        foreach (LobbyPlayer p in players)
            if (!p.IsReady.Value) return false;
        return true;
    }

    // ─── Class Selection ──────────────────────────────────────────────────────

    void OnClassSelected(int classIndex)
    {
        LobbyPlayer mine = GetMyPlayer();
        if (mine == null) return;

        mine.SetClass((LobbyPlayer.PlayerClass)classIndex);
        RefreshClassButtons();

        // Reset local ready toggle to match the new IsReady=false from SetClass.
        _localReady = false;
        UpdateReadyButtonLabel();
    }

    /// <summary>Highlights the button matching the local player's current class.</summary>
    void RefreshClassButtons()
    {
        LobbyPlayer mine = GetMyPlayer();
        int current = mine != null ? mine.SelectedClass.Value : 0;

        for (int i = 0; i < classButtons.Length; i++)
        {
            if (classButtons[i] == null) continue;
            var colors = classButtons[i].colors;
            colors.normalColor = (i == current) ? selectedClassColor : defaultClassColor;
            classButtons[i].colors = colors;
        }
    }

    // ─── Ready ────────────────────────────────────────────────────────────────

    void OnReadyToggle()
    {
        _localReady = !_localReady;
        LobbyPlayer mine = GetMyPlayer();
        mine?.SetReady(_localReady);
        UpdateReadyButtonLabel();
    }

    void UpdateReadyButtonLabel()
    {
        if (readyButtonText != null)
            readyButtonText.text = _localReady ? "Cancel Ready" : "Ready";
    }

    // ─── Name ─────────────────────────────────────────────────────────────────

    void OnNameSubmitted(string input)
    {
        GetMyPlayer()?.SetName(input);
    }

    public void SetNameFieldFromPlayer()
    {
        if (nameInputField == null) return;
        LobbyPlayer mine = GetMyPlayer();
        if (mine != null)
            nameInputField.text = mine.PlayerName.Value.ToString();
    }

    // ─── Host Actions ─────────────────────────────────────────────────────────

    void OnStartGame()
    {
        if (!NetworkManager.Singleton.IsHost || !AllPlayersReady()) return;
        LoadGame();
    }

    void OnForceStart()
    {
        if (!NetworkManager.Singleton.IsHost) return;
        Debug.Log("[LobbyManager] Host force-started the game.");
        LoadGame();
    }

    void LoadGame()
    {
        Debug.Log("[LobbyManager] Loading: " + gameSceneName);
        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    // ─── Disconnect ───────────────────────────────────────────────────────────

    void OnDisconnect()
    {
        NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene(mainMenuSceneName);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    LobbyPlayer GetMyPlayer()
    {
        foreach (LobbyPlayer p in FindObjectsByType<LobbyPlayer>(FindObjectsSortMode.None))
            if (p.IsOwner) return p;
        return null;
    }
}
