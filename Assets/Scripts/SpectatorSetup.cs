using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Hides player HUD elements for spectator clients.
/// Attach to any persistent GameObject in the GameScene (e.g. a GameManager object).
///
/// In the Inspector, drag every HUD root you want hidden into the hudElements list
/// (health bar, stamina bar, spell bar, XP bar, etc.).
/// They are untouched for the host / solo play.
/// </summary>
public class SpectatorSetup : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("HUD to hide for spectators")]
    [Tooltip("Drag every player HUD root here — health bar, stamina, spell bar, XP, etc.")]
    public GameObject[] hudElements;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        // ── Solo mode: always show HUD ────────────────────────────────────────
        bool networkActive = NetworkManager.Singleton != null
                          && NetworkManager.Singleton.IsListening;
        if (!networkActive) return;

        // ── Networked: only hide HUD when the local player chose Spectator ────
        // DO NOT use "IsClient && !IsHost" — that hides HUD for ALL non-host
        // players, including warriors/mages who just aren't the host.
        // Instead, check the LobbyPlayer's selected class.
        LobbyPlayer localLobbyPlayer = null;
        foreach (LobbyPlayer lp in FindObjectsByType<LobbyPlayer>(FindObjectsSortMode.None))
        {
            if (lp.IsOwner) { localLobbyPlayer = lp; break; }
        }

        bool isSpectator = localLobbyPlayer != null && localLobbyPlayer.IsSpectator;
        if (!isSpectator) return;

        foreach (GameObject element in hudElements)
        {
            if (element != null)
                element.SetActive(false);
        }

        Debug.Log("[SpectatorSetup] Spectator detected — HUD hidden.");
    }
}
