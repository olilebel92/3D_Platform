using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Collections;

/// <summary>
/// Handles lobby chat — sends messages via ServerRpc and broadcasts them
/// back to every client via ClientRpc.
/// In singleplayer (NetworkManager not listening) messages are displayed locally.
///
/// Attach to a persistent GameObject in LobbyScene.
/// Wire all Inspector references.
/// </summary>
public class LobbyChatManager : NetworkBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static LobbyChatManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("UI")]
    [Tooltip("ScrollRect that wraps the message list.")]
    [SerializeField] private ScrollRect chatScrollRect;

    [Tooltip("Content transform inside the ScrollRect — messages are parented here.")]
    [SerializeField] private Transform messageContainer;

    [Tooltip("Prefab for one chat line. Must contain a TMP_Text in its hierarchy.")]
    [SerializeField] private GameObject messagePrefab;

    [Tooltip("Input field where the player types.")]
    [SerializeField] private TMP_InputField chatInput;

    [Tooltip("Button that submits the message.")]
    [SerializeField] private Button sendButton;

    [Header("Settings")]
    [Tooltip("Oldest messages are removed once this limit is reached.")]
    [SerializeField] private int maxMessages = 50;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendClicked);

        // Allow pressing Enter to send; hard cap at 50 chars
        if (chatInput != null)
        {
            chatInput.characterLimit = 50;
            chatInput.onSubmit.AddListener(_ => OnSendClicked());
        }
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─── Send ─────────────────────────────────────────────────────────────────

    void OnSendClicked()
    {
        if (chatInput == null) return;

        string message = chatInput.text.Trim();
        if (string.IsNullOrEmpty(message)) return;

        chatInput.text = string.Empty;
        chatInput.ActivateInputField();

string senderName = GetLocalPlayerName();

        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        if (networkActive)
        {
            SendMessageServerRpc(
                new FixedString64Bytes(senderName),
                new FixedString128Bytes(message)
            );
        }
        else
        {
            // Singleplayer — display directly without any RPCs
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
        if (messageContainer == null || messagePrefab == null) return;

        // Drop the oldest entry if the list is full
        if (messageContainer.childCount >= maxMessages)
            Destroy(messageContainer.GetChild(0).gameObject);

        GameObject entry = Instantiate(messagePrefab, messageContainer);
        TMP_Text label = entry.GetComponentInChildren<TMP_Text>();
        if (label != null)
            label.text = $"<color=#FFCC00><b>{senderName}</b></color>: {message}";

        // Auto-scroll to the latest message
        if (chatScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            chatScrollRect.verticalNormalizedPosition = 0f;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    string GetLocalPlayerName()
    {
        foreach (LobbyPlayer p in FindObjectsByType<LobbyPlayer>(FindObjectsSortMode.None))
            if (p.IsOwner) return p.PlayerName.Value.ToString();
        return "Player";
    }
}
