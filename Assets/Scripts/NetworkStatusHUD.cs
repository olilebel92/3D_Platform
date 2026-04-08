using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// Persistent HUD label showing NGO connection status.
/// Red "Disconnected" when not in a session; colored text when hosting or connected.
///
/// Setup:
///   1. Create a Canvas in any scene (Sort Order 999, Screen Space Overlay).
///   2. Add a TMP label child, position it where you want.
///   3. Add this component to the Canvas root (or any GameObject on it).
///   4. Drag the TMP label into the Status Label slot.
///   The whole Canvas is marked DontDestroyOnLoad automatically — only ONE
///   instance will ever exist across all scenes.
/// </summary>
public class NetworkStatusHUD : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static NetworkStatusHUD Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("UI")]
    [Tooltip("TMP label that will show the connection status.")]
    [SerializeField] private TMP_Text statusLabel;

    [Header("Colors")]
    [Tooltip("Color used when hosting or connected as a client.")]
    [SerializeField] private Color connectedColor = Color.green;

    [Tooltip("Color used when not in any NGO session.")]
    [SerializeField] private Color disconnectedColor = Color.red;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        // Singleton: destroy duplicate if one already exists from a previous scene.
        if (Instance != null && Instance != this)
        {
            Destroy(transform.root.gameObject);
            return;
        }

        Instance = this;
        // Persist the root Canvas (or this GameObject if it has no parent).
        DontDestroyOnLoad(transform.root.gameObject);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback  += OnConnectionChanged;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnConnectionChanged;
        NetworkManager.Singleton.OnServerStarted            += Refresh;
        NetworkManager.Singleton.OnServerStopped            += OnServerStopped;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback  -= OnConnectionChanged;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnConnectionChanged;
        NetworkManager.Singleton.OnServerStarted            -= Refresh;
        NetworkManager.Singleton.OnServerStopped            -= OnServerStopped;
    }

    void Start()
    {
        Refresh();
    }

    // ─── NGO Callbacks ────────────────────────────────────────────────────────

    void OnConnectionChanged(ulong _)            => Refresh();
    void OnServerStopped(bool _)                 => Refresh();
    void OnSceneLoaded(Scene _, LoadSceneMode __) => Refresh();

    // ─── Core ─────────────────────────────────────────────────────────────────

    static bool SceneIsMainMenu()
    {
        string name = SceneManager.GetActiveScene().name;
        return name == "MainMenu" || name == "Main Menu";
    }

    void Refresh()
    {
        if (statusLabel == null) return;

        NetworkManager nm = NetworkManager.Singleton;

        if (nm == null || !nm.IsListening)
        {
            bool mainMenu = SceneIsMainMenu();
            statusLabel.text  = mainMenu ? "Disconnected" : "Single Player";
            statusLabel.color = mainMenu ? disconnectedColor : connectedColor;
            return;
        }

        UnityTransport transport = nm.GetComponent<UnityTransport>();
        string ip   = transport != null ? transport.ConnectionData.Address : "?";
        ushort port = transport != null ? transport.ConnectionData.Port     : (ushort)0;

        if (nm.IsHost)
            statusLabel.text = $"Host — {ip}:{port}";
        else if (nm.IsClient)
            statusLabel.text = $"Client — {ip}:{port}";

        statusLabel.color = connectedColor;
    }
}
