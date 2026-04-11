using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using TMPro;

/// <summary>
/// Displays the lobby name above the player in world space.
/// Attach to the Player prefab. The label is created at runtime and billboards
/// toward Camera.main each frame.
///
/// In multiplayer the name is read from the owning client's LobbyPlayer
/// (which survives the Lobby → Game scene transition) and kept in sync via
/// NetworkVariable callback.
/// In solo / non-networked mode a "Player" label is shown as a fallback.
/// </summary>
public class PlayerNameTag : NetworkBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Name Tag")]
    [Tooltip("World-space height above the player root where the label appears.")]
    [SerializeField] private float heightOffset = 2.4f;

    [Tooltip("Font size of the world-space TMP label. Tune to match your scene scale.")]
    [SerializeField] private float fontSize = 2f;

    [Tooltip("Label color.")]
    [SerializeField] private Color nameColor = Color.white;

    [Tooltip("When true, the label is hidden for the locally owned player.")]
    [SerializeField] private bool hideForLocalOwner = false;

    // ─── Private State ────────────────────────────────────────────────────────

    private TextMeshPro  _label;
    private LobbyPlayer  _lobbyPlayer;
    private Transform    _cameraTransform;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        CreateLabel();
    }

    void Start()
    {
        if (Camera.main != null)
            _cameraTransform = Camera.main.transform;

        // Solo / non-networked fallback — OnNetworkSpawn never fires.
        bool networkingActive = NetworkManager.Singleton != null
                             && NetworkManager.Singleton.IsListening;
        if (!networkingActive)
            SetLabelText("Player");
    }

    // ─── NGO Lifecycle ────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        _lobbyPlayer = FindLobbyPlayer(OwnerClientId);

        if (_lobbyPlayer != null)
        {
            SetLabelText(_lobbyPlayer.PlayerName.Value.ToString());
            _lobbyPlayer.PlayerName.OnValueChanged += OnNameChanged;
        }
        else
        {
            // LobbyPlayer not found — use a generic placeholder.
            SetLabelText("Player " + (OwnerClientId + 1));
            Debug.LogWarning($"[PlayerNameTag] No LobbyPlayer found for client {OwnerClientId}.");
        }

        if (hideForLocalOwner && IsOwner)
            _label.gameObject.SetActive(false);
    }

    public override void OnNetworkDespawn()
    {
        if (_lobbyPlayer != null)
            _lobbyPlayer.PlayerName.OnValueChanged -= OnNameChanged;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (_lobbyPlayer != null)
            _lobbyPlayer.PlayerName.OnValueChanged -= OnNameChanged;
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    void LateUpdate()
    {
        if (_label == null || !_label.gameObject.activeSelf) return;

        // Lazy camera init — Camera.main may not be ready on the first frame.
        if (_cameraTransform == null)
        {
            if (Camera.main != null) _cameraTransform = Camera.main.transform;
            else return;
        }

        // Billboard: align to camera's forward so the text always faces the viewer.
        _label.transform.rotation = Quaternion.LookRotation(_cameraTransform.forward);
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    void CreateLabel()
    {
        GameObject obj = new GameObject("NameTag");
        obj.transform.SetParent(transform, worldPositionStays: false);
        obj.transform.localPosition = new Vector3(0f, heightOffset, 0f);
        obj.transform.localRotation = Quaternion.identity;

        _label = obj.AddComponent<TextMeshPro>();
        _label.fontSize  = fontSize;
        _label.color     = nameColor;
        _label.alignment = TextAlignmentOptions.Center;
        _label.fontStyle = FontStyles.Bold;
        _label.text      = "";

        // Render on top of geometry so the name is always readable.
        _label.GetComponent<MeshRenderer>().sortingOrder = 1;
    }

    void SetLabelText(string value)
    {
        if (_label != null)
            _label.text = value;
    }

    void OnNameChanged(FixedString64Bytes previous, FixedString64Bytes current)
    {
        SetLabelText(current.ToString());
    }

    LobbyPlayer FindLobbyPlayer(ulong clientId)
    {
        foreach (var lp in FindObjectsByType<LobbyPlayer>(FindObjectsSortMode.None))
            if (lp.OwnerClientId == clientId) return lp;
        return null;
    }
}
