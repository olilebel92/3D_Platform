using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;
using Unity.Collections;

/// <summary>
/// Handles lobby chat via UI Toolkit — sends messages through ServerRpc and broadcasts
/// them to every client. In singleplayer (NetworkManager not listening) messages are
/// displayed locally.
///
/// Attach to a persistent GameObject in LobbyScene.
/// Assign the same UIDocument used by LobbyManager in the Inspector.
/// </summary>
public class LobbyChatManager : NetworkBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static LobbyChatManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [SerializeField] private UIDocument _doc;

    [Header("Settings")]
    [Tooltip("Oldest messages are removed once this limit is reached.")]
    [SerializeField] private int maxMessages = 50;

    // ─── UIElements refs ──────────────────────────────────────────────────────

    private ScrollView _chatScroll;
    private TextField  _chatField;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        var root = _doc.rootVisualElement;

        _chatScroll = root.Q<ScrollView>("chat-scroll");
        _chatField  = root.Q<TextField>("chat-input");

        root.Q<Button>("send-button")?.RegisterCallback<ClickEvent>(_ => OnSendClicked());

        if (_chatField != null)
        {
            _chatField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    OnSendClicked();
            });
        }
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─── Send ─────────────────────────────────────────────────────────────────

    void OnSendClicked()
    {
        if (_chatField == null) return;

        string message = _chatField.value.Trim();
        if (string.IsNullOrEmpty(message)) return;

        _chatField.SetValueWithoutNotify(string.Empty);
        _chatField.schedule.Execute(() => _chatField.Focus()).StartingIn(0);

        string senderName   = GetLocalPlayerName();
        bool   networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        if (networkActive)
        {
            SendMessageServerRpc(
                new FixedString64Bytes(senderName),
                new FixedString128Bytes(message)
            );
        }
        else
        {
            DisplayMessage(senderName, message);
        }
    }

    // ─── RPCs ─────────────────────────────────────────────────────────────────

    [Rpc(SendTo.Server)]
    void SendMessageServerRpc(FixedString64Bytes senderName, FixedString128Bytes message)
    {
        ReceiveMessageClientRpc(senderName, message);
    }

    [ClientRpc]
    void ReceiveMessageClientRpc(FixedString64Bytes senderName, FixedString128Bytes message)
    {
        DisplayMessage(senderName.ToString(), message.ToString());
    }

    // ─── Display ──────────────────────────────────────────────────────────────

    void DisplayMessage(string senderName, string message)
    {
        if (_chatScroll == null) return;

        // Drop oldest entry if full
        if (_chatScroll.contentContainer.childCount >= maxMessages)
            _chatScroll.contentContainer.RemoveAt(0);

        var row = new VisualElement();
        row.AddToClassList("chat-message");

        var senderLabel = new Label(senderName);
        senderLabel.AddToClassList("chat-sender");
        row.Add(senderLabel);

        var colonLabel = new Label(": ");
        colonLabel.AddToClassList("chat-colon");
        row.Add(colonLabel);

        var textLabel = new Label(message);
        textLabel.AddToClassList("chat-text");
        row.Add(textLabel);

        _chatScroll.Add(row);

        // Scroll to bottom after layout updates
        _chatScroll.schedule.Execute(() =>
            _chatScroll.scrollOffset = new Vector2(0, float.MaxValue));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    string GetLocalPlayerName()
    {
        foreach (LobbyPlayer p in FindObjectsByType<LobbyPlayer>(FindObjectsSortMode.None))
            if (p.IsOwner) return p.PlayerName.Value.ToString();
        return "Player";
    }
}
