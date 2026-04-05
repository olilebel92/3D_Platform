using UnityEngine;
using Unity.Netcode;
using Unity.Collections;

/// <summary>
/// Spawned automatically by NGO for every connected client (host included).
/// Survives scene changes (Lobby → Game) so name and class are always available.
/// One per client, owned by that client.
/// </summary>
public class LobbyPlayer : NetworkBehaviour
{
    // ─── Class Definition ─────────────────────────────────────────────────────

    public enum PlayerClass
    {
        Random    = 0,
        Warrior   = 1,
        Mage      = 2,
        Rogue     = 3,
        Spectator = 4
    }

    /// <summary>True if this player chose to spectate instead of play.</summary>
    public bool IsSpectator => (PlayerClass)SelectedClass.Value == PlayerClass.Spectator;

    // ─── Networked State ──────────────────────────────────────────────────────

    [Tooltip("Display name chosen in the lobby.")]
    public NetworkVariable<FixedString64Bytes> PlayerName = new NetworkVariable<FixedString64Bytes>(
        "Player",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    [Tooltip("Class selected in the lobby. 0 = Random (assigned on game start).")]
    public NetworkVariable<int> SelectedClass = new NetworkVariable<int>(
        (int)PlayerClass.Random,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    [Tooltip("True once the player has clicked Ready.")]
    public NetworkVariable<bool> IsReady = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    // ─── NGO Lifecycle ────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        // Survive scene transitions so name + class carry from Lobby → GameScene.
        NetworkObject.DestroyWithScene = false;

        if (IsOwner)
            PlayerName.Value = new FixedString64Bytes("Player " + (OwnerClientId + 1));

        PlayerName.OnValueChanged    += OnAnyValueChanged;
        SelectedClass.OnValueChanged += OnAnyValueChanged;
        IsReady.OnValueChanged       += OnAnyValueChanged;

        LobbyManager.Instance?.RefreshPlayerList();
        LobbyManager.Instance?.RefreshStartButton();

        // Pre-fill the name input field now that our LobbyPlayer exists.
        if (IsOwner)
            LobbyManager.Instance?.SetNameFieldFromPlayer();

        Debug.Log($"[LobbyPlayer] Spawned — client {OwnerClientId}  IsOwner={IsOwner}");
    }

    public override void OnNetworkDespawn()
    {
        PlayerName.OnValueChanged    -= OnAnyValueChanged;
        SelectedClass.OnValueChanged -= OnAnyValueChanged;
        IsReady.OnValueChanged       -= OnAnyValueChanged;

        LobbyManager.Instance?.RefreshPlayerList();
        LobbyManager.Instance?.RefreshStartButton();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public void SetName(string newName)
    {
        if (!IsOwner || string.IsNullOrWhiteSpace(newName)) return;
        PlayerName.Value = new FixedString64Bytes(newName.Trim());
    }

    /// <summary>
    /// Sets the class and resets ready — changing your mind after clicking Ready
    /// brings you back to not-ready so others know you reconsidered.
    /// </summary>
    public void SetClass(PlayerClass playerClass)
    {
        if (!IsOwner) return;
        SelectedClass.Value = (int)playerClass;
        IsReady.Value = false;          // Changing class resets ready state.
    }

    public void SetReady(bool ready)
    {
        if (!IsOwner) return;
        IsReady.Value = ready;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    public PlayerClass GetClass() => (PlayerClass)SelectedClass.Value;

    void OnAnyValueChanged<T>(T previous, T current)
    {
        LobbyManager.Instance?.RefreshPlayerList();
        LobbyManager.Instance?.RefreshStartButton();
    }
}
