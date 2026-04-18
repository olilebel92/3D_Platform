using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// Manages the Lobby scene via UI Toolkit: player list, name input, class selection,
/// ready system, and scene transition to the game.
///
/// Attach to a persistent GameObject in LobbyScene.
/// Assign the UIDocument that owns Lobby.uxml in the Inspector.
/// </summary>
public class LobbyManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static LobbyManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [SerializeField] private UIDocument _doc;

    [Header("Scenes")]
    public string gameSceneName     = "GameScene";
    public string mainMenuSceneName = "MainMenu";

    // ─── UIElements refs ──────────────────────────────────────────────────────

    private ScrollView    _playerList;
    private TextField     _nameField;
    private Label         _sessionModeLabel;
    private Button        _readyBtn;
    private Button        _startBtn;
    private Button        _forceStartBtn;
    private Button[]      _classBtns;
    private VisualElement _netPillDot;
    private Label         _netPillLabel;

    // ─── State ────────────────────────────────────────────────────────────────

    private bool _localReady;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        var root = _doc.rootVisualElement;

        _playerList       = root.Q<ScrollView>("player-list");
        _nameField        = root.Q<TextField>("name-field");
        _sessionModeLabel = root.Q<Label>("session-mode");
        _readyBtn         = root.Q<Button>("ready-button");
        _startBtn         = root.Q<Button>("start-button");
        _forceStartBtn    = root.Q<Button>("force-start-button");
        _netPillDot       = root.Q<VisualElement>("network-pill-dot");
        _netPillLabel     = root.Q<Label>("network-pill-label");

        _classBtns = new[]
        {
            root.Q<Button>("class-random"),
            root.Q<Button>("class-warrior"),
            root.Q<Button>("class-mage"),
            root.Q<Button>("class-rogue"),
            root.Q<Button>("class-spectator"),
        };

        bool networkReady = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        bool isHost       = networkReady && NetworkManager.Singleton.IsHost;

        // Host-only buttons
        if (_startBtn != null)
        {
            _startBtn.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
            _startBtn.RegisterCallback<ClickEvent>(_ => OnStartGame());
        }
        if (_forceStartBtn != null)
        {
            _forceStartBtn.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
            _forceStartBtn.RegisterCallback<ClickEvent>(_ => OnForceStart());
        }

        // Class buttons — order matches PlayerClass enum (Random=0 … Spectator=4)
        for (int i = 0; i < _classBtns.Length; i++)
        {
            int idx = i;
            _classBtns[i]?.RegisterCallback<ClickEvent>(_ => OnClassSelected(idx));
        }

        _readyBtn?.RegisterCallback<ClickEvent>(_ => OnReadyToggle());
        root.Q<Button>("disconnect-button")?.RegisterCallback<ClickEvent>(_ => OnDisconnect());

        if (_nameField != null)
            _nameField.RegisterCallback<FocusOutEvent>(_ => OnNameSubmitted(_nameField.value));

        if (networkReady)
        {
            NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }

        SetNameFieldFromPlayer();
        RefreshPlayerList();
        RefreshClassButtons();
        RefreshStartButton();
        RefreshSessionModeText();
        RefreshNetworkPill();

        // Deferred refresh — catches LobbyPlayers whose OnNetworkSpawn fired
        // before this Start() ran (e.g. objects that survived a scene transition).
        root.schedule.Execute(RefreshPlayerList).StartingIn(0);
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
        RefreshNetworkPill();
    }

    void OnClientDisconnected(ulong clientId)
    {
        RefreshPlayerList();
        RefreshStartButton();
        RefreshNetworkPill();
    }

    // ─── Player List ──────────────────────────────────────────────────────────

    public void RefreshPlayerList()
    {
        // Lazy-init in case this is called before Start() (e.g. from OnNetworkSpawn).
        if (_playerList == null && _doc != null)
            _playerList = _doc.rootVisualElement?.Q<ScrollView>("player-list");

        RefreshSessionModeText();
        if (_playerList == null) return;

        _playerList.contentContainer.Clear();

        var players = FindObjectsByType<LobbyPlayer>(FindObjectsSortMode.None);
        System.Array.Sort(players, (a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        int n = 1;
        foreach (LobbyPlayer player in players)
        {
            var row = new VisualElement();
            row.AddToClassList("player-row");

            var tagLabel = new Label($"[P{n}]");
            tagLabel.AddToClassList("player-tag");
            row.Add(tagLabel);

            if (player.OwnerClientId == 0)
            {
                var hostLabel = new Label("[Host]");
                hostLabel.AddToClassList("host-tag");
                row.Add(hostLabel);
            }

            var nameLabel = new Label(player.PlayerName.Value.ToString());
            nameLabel.AddToClassList("player-name");
            row.Add(nameLabel);

            var sep1 = new Label("—");
            sep1.AddToClassList("player-separator");
            row.Add(sep1);

            var classLabel = new Label(((LobbyPlayer.PlayerClass)player.SelectedClass.Value).ToString());
            classLabel.AddToClassList("player-class");
            row.Add(classLabel);

            var sep2 = new Label("—");
            sep2.AddToClassList("player-separator");
            row.Add(sep2);

            bool ready = player.IsReady.Value;
            var readyLabel = new Label(ready ? "[Ready]" : "[Not Ready]");
            readyLabel.AddToClassList(ready ? "ready-yes" : "ready-no");
            row.Add(readyLabel);

            _playerList.Add(row);
            n++;
        }
    }

    // ─── Session Mode Label ───────────────────────────────────────────────────

    void RefreshSessionModeText()
    {
        if (_sessionModeLabel == null && _doc != null)
            _sessionModeLabel = _doc.rootVisualElement?.Q<Label>("session-mode");
        if (_sessionModeLabel == null) return;
        bool active = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        int  count  = active ? NetworkManager.Singleton.ConnectedClientsIds.Count : 1;
        _sessionModeLabel.text = count > 1 ? "MULTIPLAYER MODE" : "SINGLE PLAYER SESSION";
    }

    // ─── Start Button ─────────────────────────────────────────────────────────

    public void RefreshStartButton()
    {
        RefreshSessionModeText();
        if (_startBtn == null && _doc != null)
            _startBtn = _doc.rootVisualElement?.Q<Button>("start-button");
        if (_startBtn == null) return;
        _startBtn.SetEnabled(AllPlayersReady());
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
        _localReady = false;
        UpdateReadyButtonLabel();
    }

    void RefreshClassButtons()
    {
        LobbyPlayer mine    = GetMyPlayer();
        int         current = mine != null ? mine.SelectedClass.Value : 0;

        for (int i = 0; i < _classBtns.Length; i++)
            _classBtns[i]?.EnableInClassList("class-btn--selected", i == current);
    }

    // ─── Ready ────────────────────────────────────────────────────────────────

    void OnReadyToggle()
    {
        _localReady = !_localReady;
        GetMyPlayer()?.SetReady(_localReady);
        UpdateReadyButtonLabel();
    }

    void UpdateReadyButtonLabel()
    {
        if (_readyBtn != null)
            _readyBtn.text = _localReady ? "CANCEL READY" : "READY";
    }

    // ─── Name ─────────────────────────────────────────────────────────────────

    void OnNameSubmitted(string input)
    {
        if (!string.IsNullOrWhiteSpace(input))
            PlayerPrefs.SetString("PlayerName", input.Trim());
        GetMyPlayer()?.SetName(input);
    }

    public void SetNameFieldFromPlayer()
    {
        if (_nameField == null) return;
        LobbyPlayer mine = GetMyPlayer();
        if (mine != null)
            _nameField.SetValueWithoutNotify(mine.PlayerName.Value.ToString());
        else if (PlayerPrefs.HasKey("PlayerName"))
            _nameField.SetValueWithoutNotify(PlayerPrefs.GetString("PlayerName"));
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

        if (NetworkManager.Singleton.ConnectedClientsIds.Count == 1)
        {
            Debug.Log("[LobbyManager] Only one player — switching to singleplayer mode.");
            NetworkManager.Singleton.Shutdown();
            SceneManager.LoadScene(gameSceneName);
            return;
        }

        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    // ─── Disconnect ───────────────────────────────────────────────────────────

    void OnDisconnect()
    {
        NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene(mainMenuSceneName);
    }

    // ─── Network Pill ─────────────────────────────────────────────────────────

    void RefreshNetworkPill()
    {
        if (_netPillLabel == null) return;

        NetworkManager nm = NetworkManager.Singleton;

        if (nm == null || !nm.IsListening)
        {
            SetPillColor(new Color(0.78f, 0.27f, 0.23f));
            _netPillLabel.text = "OFFLINE";
            return;
        }

        UnityTransport transport = nm.GetComponent<UnityTransport>();
        string ip   = transport != null ? transport.ConnectionData.Address : "?";
        ushort port = transport != null ? transport.ConnectionData.Port     : (ushort)0;

        SetPillColor(new Color(0.27f, 0.78f, 0.35f));
        _netPillLabel.text = nm.IsHost
            ? $"HOST  \u2014  {ip}:{port}"
            : $"CLIENT  \u2014  {ip}:{port}";
    }

    void SetPillColor(Color color)
    {
        if (_netPillDot   != null) _netPillDot.style.backgroundColor = new StyleColor(color);
        if (_netPillLabel != null) _netPillLabel.style.color          = new StyleColor(color);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    LobbyPlayer GetMyPlayer()
    {
        foreach (LobbyPlayer p in FindObjectsByType<LobbyPlayer>(FindObjectsSortMode.None))
            if (p.IsOwner) return p;
        return null;
    }
}
