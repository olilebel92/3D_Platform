using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class MainMenuManager : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Scene Names")]
    [SerializeField] string gameSceneName  = "GameScene";
    [SerializeField] string lobbySceneName = "LobbyScene";

    [Header("Network")]
    [SerializeField] ushort port = 7777;

    [Header("Audio Mixer")]
    [SerializeField] AudioMixer mixer;

    [Header("UI")]
    [SerializeField] UIDocument document;
    [SerializeField] string gameTitleString = "HACKNSLASH";
    [SerializeField] DevLog devLog;

    [Header("UI Sounds")]
    [SerializeField] AudioClip uiClickClip;
    [SerializeField] AudioClip uiBackClip;

    [Header("Test Sounds")]
    [SerializeField] AudioClip sfxTestClip;
    [SerializeField] AudioClip uiTestClip;

    // ─── Cached Elements ──────────────────────────────────────────────────────

    VisualElement mainPanel, joinPanel, waitingPanel, settingsPanel, devlogPanel;
    VisualElement scrim, dialogWrap;
    Label         waitingLabel;
    TextField     ipField;

    // Settings sliders / labels
    Slider masterSlider, musicSlider, sfxSlider, uiSlider;
    Label  masterLabel,  musicLabel,  sfxLabel,  uiLabel;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        Time.timeScale = 1f;
        ShutdownNetwork();
    }

    void Start()
    {
        if (document == null) document = GetComponent<UIDocument>();

        if (mixer != null)
        {
            SettingsManager.Mixer = mixer;
            SettingsManager.Apply();
        }

        var root = document.rootVisualElement;

        // ── Panels ──
        mainPanel     = root.Q("main-panel");
        joinPanel     = root.Q("join-panel");
        waitingPanel  = root.Q("waiting-panel");
        settingsPanel = root.Q("settings-panel");
        devlogPanel   = root.Q("devlog-panel");
        scrim         = root.Q("scrim");
        dialogWrap    = root.Q("dialog-wrap");

        root.Q<Label>("title-label").text = gameTitleString;
        waitingLabel = root.Q<Label>("waiting-label");
        ipField      = root.Q<TextField>("ip-field");

        // ── Settings elements ──
        masterSlider = root.Q<Slider>("master-slider");
        musicSlider  = root.Q<Slider>("music-slider");
        sfxSlider    = root.Q<Slider>("sfx-slider");
        uiSlider     = root.Q<Slider>("ui-slider");
        masterLabel  = root.Q<Label>("master-label");
        musicLabel   = root.Q<Label>("music-label");
        sfxLabel     = root.Q<Label>("sfx-label");
        uiLabel      = root.Q<Label>("ui-label");

        // ── Slider callbacks ──
        masterSlider?.RegisterValueChangedCallback(e => { SettingsManager.MasterVolume = e.newValue; UpdateVolumeLabel(masterLabel, e.newValue); });
        musicSlider?.RegisterValueChangedCallback(e  => { SettingsManager.MusicVolume  = e.newValue; UpdateVolumeLabel(musicLabel,  e.newValue); });
        sfxSlider?.RegisterValueChangedCallback(e    => { SettingsManager.SfxVolume    = e.newValue; UpdateVolumeLabel(sfxLabel,    e.newValue); });
        uiSlider?.RegisterValueChangedCallback(e     => { SettingsManager.UiVolume     = e.newValue; UpdateVolumeLabel(uiLabel,     e.newValue); });

        // ── Button callbacks ──
        root.Q<Button>("play-button")?.RegisterCallback<ClickEvent>(_     => { PlayClick(); OnPlay(); });
        root.Q<Button>("host-button")?.RegisterCallback<ClickEvent>(_     => { PlayClick(); OnHost(); });
        root.Q<Button>("join-button")?.RegisterCallback<ClickEvent>(_     => { PlayClick(); ShowJoin(); });
        root.Q<Button>("settings-button")?.RegisterCallback<ClickEvent>(_ => { PlayClick(); ShowSettings(); });
        root.Q<Button>("devlog-button")?.RegisterCallback<ClickEvent>(_   => { PlayClick(); ShowDevLog(); });
        root.Q<Button>("quit-button")?.RegisterCallback<ClickEvent>(_     => { PlayClick(); OnQuit(); });

        root.Q<Button>("connect-button")?.RegisterCallback<ClickEvent>(_       => { PlayClick(); OnConnect(); });
        root.Q<Button>("join-back-button")?.RegisterCallback<ClickEvent>(_     => { PlayBack();  ShowMain(); });
        root.Q<Button>("cancel-button")?.RegisterCallback<ClickEvent>(_        => { PlayBack();  OnCancel(); });
        root.Q<Button>("settings-back-button")?.RegisterCallback<ClickEvent>(_ => { PlayBack();  ShowMain(); });
        root.Q<Button>("devlog-back-button")?.RegisterCallback<ClickEvent>(_   => { PlayBack();  ShowMain(); });
        root.Q<Button>("sfx-test-button")?.RegisterCallback<ClickEvent>(_ => SoundManager.Instance?.PlaySFX(sfxTestClip));
        root.Q<Button>("ui-test-button")?.RegisterCallback<ClickEvent>(_  => SoundManager.Instance?.PlayUI(uiTestClip));

        ShowMain();
    }

    // ─── Navigation ───────────────────────────────────────────────────────────

    void ShowMain()     => SetActivePanel(mainPanel);
    void ShowJoin()     => SetActivePanel(joinPanel);
    void ShowDevLog()
    {
        devLog?.Populate(document.rootVisualElement.Q<ScrollView>("devlog-scroll"));
        SetActivePanel(devlogPanel);
    }

    void ShowSettings()
    {
        // Sync sliders to saved values before showing
        masterSlider?.SetValueWithoutNotify(SettingsManager.MasterVolume); UpdateVolumeLabel(masterLabel, SettingsManager.MasterVolume);
        musicSlider?.SetValueWithoutNotify(SettingsManager.MusicVolume);   UpdateVolumeLabel(musicLabel,  SettingsManager.MusicVolume);
        sfxSlider?.SetValueWithoutNotify(SettingsManager.SfxVolume);       UpdateVolumeLabel(sfxLabel,    SettingsManager.SfxVolume);
        uiSlider?.SetValueWithoutNotify(SettingsManager.UiVolume);         UpdateVolumeLabel(uiLabel,     SettingsManager.UiVolume);

        SetActivePanel(settingsPanel);
    }

    // ─── Actions ──────────────────────────────────────────────────────────────

    void OnPlay()
    {
        Debug.Log("[MainMenu] Solo play — loading: " + gameSceneName);
        ShutdownNetwork();
        if (SceneTransitionManager.Instance != null)
            SceneTransitionManager.Instance.LoadScene(gameSceneName);
        else
            SceneManager.LoadScene(gameSceneName);
    }

    void OnHost()
    {
        if (!ValidateNetworkManager()) return;

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
            transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");

        Debug.Log("[MainMenu] Starting as Host — loading lobby...");

        void StartHost()
        {
            NetworkManager.Singleton.StartHost();
            NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
        }

        if (SceneTransitionManager.Instance != null)
            SceneTransitionManager.Instance.FadeOutThen(StartHost);
        else
            StartHost();
    }

    void OnConnect()
    {
        if (!ValidateNetworkManager()) return;

        string ip = ipField != null ? ipField.value.Trim() : "127.0.0.1";
        if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("[MainMenu] UnityTransport not found on NetworkManager!");
            return;
        }

        transport.SetConnectionData(ip, port);
        NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.StartClient();

        Debug.Log($"[MainMenu] Connecting to {ip}:{port}...");
        ShowWaiting("Connecting...");
    }

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

    void OnQuit()
    {
        Debug.Log("[MainMenu] Quitting.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ─── Network Callbacks ────────────────────────────────────────────────────

    void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        Debug.Log("[MainMenu] Connected! Waiting for scene load...");
        ShowWaiting("Connected! Loading game...");

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
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    void ShowWaiting(string message)
    {
        if (waitingLabel != null) waitingLabel.text = message;
        SetActivePanel(waitingPanel);
    }

    void SetActivePanel(VisualElement panel)
    {
        // Hide all dialog panels
        joinPanel?.AddToClassList("is-hidden");
        waitingPanel?.AddToClassList("is-hidden");
        settingsPanel?.AddToClassList("is-hidden");
        devlogPanel?.AddToClassList("is-hidden");

        if (panel == mainPanel)
        {
            scrim?.RemoveFromClassList("on");
            dialogWrap?.RemoveFromClassList("on");
        }
        else
        {
            scrim?.AddToClassList("on");
            dialogWrap?.AddToClassList("on");
            panel?.RemoveFromClassList("is-hidden");
        }
    }

    void PlayClick() => SoundManager.Instance?.PlayUI(uiClickClip);
    void PlayBack()  => SoundManager.Instance?.PlayUI(uiBackClip);

    static void UpdateVolumeLabel(Label label, float value)
    {
        if (label != null) label.text = Mathf.RoundToInt(value * 100f) + "%";
    }

    bool ValidateNetworkManager()
    {
        if (NetworkManager.Singleton != null) return true;
        Debug.LogError("[MainMenu] NetworkManager not found in scene!");
        return false;
    }

    static void ShutdownNetwork()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;
        try
        {
            Debug.Log("[MainMenu] Shutting down NGO session.");
            NetworkManager.Singleton.Shutdown();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MainMenu] NGO shutdown exception (safe to ignore): {e.Message}");
        }
    }
}
