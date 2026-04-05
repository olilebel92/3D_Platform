using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// Place this in the GameScene. The host calls SpawnPlayers() on Start,
/// creating one player object per connected client with correct ownership.
///
/// How it works:
///   1. Host iterates ConnectedClientsIds.
///   2. Instantiates playerPrefab at the matching spawn point (or a fallback offset).
///   3. Calls NetworkObject.SpawnWithOwnership(clientId) so each client owns their player.
///   4. Each client's PlayerController.OnNetworkSpawn() then wires the camera automatically.
///
/// Setup in Inspector:
///   - playerPrefab  → your Player NetworkObject prefab (drag from Project)
///   - spawnPoints   → 1–4 Transforms spread around the start area
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Player Prefab")]
    [Tooltip("The networked Player prefab. Must have a NetworkObject component.")]
    public NetworkObject playerPrefab;

    [Header("Spawn Points")]
    [Tooltip("One spawn point per possible player. If fewer than players, extras use offset fallback.")]
    public Transform[] spawnPoints;

    [Tooltip("Extra upward offset applied to every spawn position. Increase if players spawn inside the ground.")]
    public float spawnHeightOffset = 1.5f;


    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("[PlayerSpawner] playerPrefab is not assigned! Assign the Player prefab in Inspector.");
            return;
        }

        bool networkingActive = NetworkManager.Singleton != null
                             && NetworkManager.Singleton.IsListening;

        if (networkingActive)
        {
            // Networked session — only the host spawns players.
            if (NetworkManager.Singleton.IsServer)
                SpawnPlayers();
        }
        else
        {
            // Solo / testing — no networking, just instantiate directly.
            SpawnSolo();
        }
    }

    // ─── Solo Spawn ───────────────────────────────────────────────────────────

    void SpawnSolo()
    {
        Vector3 pos = GetSpawnPosition(0);
        Quaternion rot = GetSpawnRotation(0);

        // Plain Instantiate — no NGO needed.
        Instantiate(playerPrefab.gameObject, pos, rot);
        Debug.Log("[PlayerSpawner] Solo mode — player spawned at " + pos);
    }

    // ─── Spawn ────────────────────────────────────────────────────────────────

    void SpawnPlayers()
    {
        List<ulong> clients = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);

        Debug.Log($"[PlayerSpawner] Spawning players for {clients.Count} connected client(s).");

        int playerIndex = 0; // Counts only non-spectators so spawn points stay contiguous.

        for (int i = 0; i < clients.Count; i++)
        {
            // Skip clients who chose to spectate — they get the SpectatorFreeCam instead.
            LobbyPlayer lobbyPlayer = GetLobbyPlayerForClient(clients[i]);
            if (lobbyPlayer != null && lobbyPlayer.IsSpectator)
            {
                Debug.Log($"[PlayerSpawner] Client {clients[i]} chose Spectator — skipping player spawn.");
                continue;
            }

            Vector3    pos = GetSpawnPosition(playerIndex);
            Quaternion rot = GetSpawnRotation(playerIndex);

            NetworkObject obj = Instantiate(playerPrefab, pos, rot);

            // SpawnWithOwnership assigns ownership to the client AND spawns on all peers.
            // destroyWithScene=true means the object is cleaned up when the scene unloads.
            obj.SpawnWithOwnership(clients[i], destroyWithScene: true);

            // ── Confirm spawn position on the owning client ───────────────────
            // Teleport() requires IsOwner == true, so the SERVER cannot call it on
            // a client-authoritative OwnerNetworkTransform — it would throw.
            // Instead we send a ClientRpc; the owner calls Teleport() on itself,
            // which has the correct authority and bypasses interpolation entirely.
            PlayerController pc = obj.GetComponent<PlayerController>();
            if (pc != null)
            {
                ClientRpcParams ownerOnly = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { clients[i] } }
                };
                pc.ApplySpawnPositionClientRpc(pos, ownerOnly);
            }

            Debug.Log($"[PlayerSpawner] Spawned player for client {clients[i]} at {pos} (spawn point {playerIndex})");
            playerIndex++;
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    LobbyPlayer GetLobbyPlayerForClient(ulong clientId)
    {
        foreach (LobbyPlayer lp in FindObjectsByType<LobbyPlayer>(FindObjectsSortMode.None))
            if (lp.OwnerClientId == clientId) return lp;
        return null;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    Vector3 GetSpawnPosition(int index)
    {
        Vector3 base_pos;

        if (spawnPoints != null && index < spawnPoints.Length && spawnPoints[index] != null)
            base_pos = spawnPoints[index].position;
        else
            base_pos = Vector3.right * index * 2.5f; // fallback

        // ── Priority 1: Unity Terrain heightmap (most reliable) ───────────────
        // SampleHeight reads the heightmap directly — no physics, no colliders,
        // immune to other spawned players or trigger zones being in the way.
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            float terrainY = terrain.SampleHeight(base_pos)
                           + terrain.transform.position.y;
            Vector3 result = new Vector3(base_pos.x, terrainY + spawnHeightOffset, base_pos.z);
            Debug.Log($"[PlayerSpawner] Terrain height at spawn {index}: terrainY={terrainY:F2} → spawnY={result.y:F2}");
            return result;
        }

        // ── Priority 2: Physics raycast (for non-Terrain scenes) ─────────────
        // Ignore triggers so they don't give a false surface reading.
        Vector3 rayOrigin = new Vector3(base_pos.x, base_pos.y + 500f, base_pos.z);
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 1000f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            Debug.Log($"[PlayerSpawner] Raycast hit at Y={hit.point.y:F2} for spawn {index}");
            return hit.point + Vector3.up * spawnHeightOffset;
        }

        // ── Fallback: raw spawn point position ────────────────────────────────
        Debug.LogWarning($"[PlayerSpawner] No surface found for spawn {index} — using raw position Y={base_pos.y}");
        return base_pos + Vector3.up * spawnHeightOffset;
    }

    /// <summary>
    /// Returns a safe respawn position for a player who died mid-session.
    /// Uses spawn point 0 so they always return to the start area.
    /// </summary>
    public Vector3 GetRespawnPosition() => GetSpawnPosition(0);

    Quaternion GetSpawnRotation(int index)
    {
        if (spawnPoints != null && index < spawnPoints.Length && spawnPoints[index] != null)
            return spawnPoints[index].rotation;

        return Quaternion.identity;
    }
}
