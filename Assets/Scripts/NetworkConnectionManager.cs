using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Enforces a maximum player count by rejecting connections once the server is full.
/// Attach to the same GameObject as NetworkManager.
/// Make sure Connection Approval is ticked in the NetworkManager Inspector.
/// </summary>
public class NetworkConnectionManager : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Connection")]
    [Tooltip("Maximum number of people allowed in the session (host counts as 1).")]
    public int maxPlayers = 4;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
    }

    // ─── Approval ─────────────────────────────────────────────────────────────

    void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest  request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        int current = NetworkManager.Singleton.ConnectedClientsIds.Count;
        bool approved = current < maxPlayers;

        response.Approved           = approved;
        response.CreatePlayerObject = approved; // Spawn LobbyPlayer only if approved.
        response.Pending            = false;

        if (approved)
            Debug.Log($"[NetworkConnectionManager] Client approved ({current + 1}/{maxPlayers}).");
        else
            Debug.Log($"[NetworkConnectionManager] Connection rejected — server full ({current}/{maxPlayers}).");
    }
}
