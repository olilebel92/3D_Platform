using UnityEngine;
using Unity.Netcode;

// ─── Loot Type Enum ───────────────────────────────────────────────────────────
/// <summary>
/// Defines what reward this pickup grants. The Inspector only shows
/// the fields relevant to the selected type (via LootPickupEditor).
/// </summary>
public enum LootType
{
    XPReward,
    HPPotion,
    ManaPotion,   // WIP — ready for when ManaSystem is implemented
    Material      // WIP — adds an ItemData to the player's inventory
}

// ─── Restore Mode ─────────────────────────────────────────────────────────────
/// <summary>
/// Shared by HP and Mana potions.
/// Flat: restores a fixed amount.
/// Percent: restores a % of the player's max value.
/// Both: applies flat amount first, then the percentage on top.
/// </summary>
public enum RestoreMode { Flat, Percent, Both }

/// <summary>
/// General-purpose ground pickup. Select a LootType in the Inspector
/// to configure only the fields that apply to that reward.
///
/// Multiplayer flow:
///   1. OnTriggerEnter fires on the local client whose player touched the pickup.
///   2. That client calls CollectServerRpc — server validates and processes rewards once.
///   3. Server despawns the object so it disappears for everyone.
///   4. FX (particles/sound) are triggered on all clients via ClientRpc.
/// </summary>
public class LootPickup : NetworkBehaviour
{
    // ─── Type ─────────────────────────────────────────────────────────────────
    [Header("Loot Type")]
    [Tooltip("Select what this pickup grants. The Inspector will show only the relevant reward fields.")]
    public LootType lootType = LootType.XPReward;

    // ─── XP ───────────────────────────────────────────────────────────────────
    [Tooltip("Amount of XP granted to the collecting player on pickup.")]
    public int xpReward = 25;

    [Tooltip("Scales xpReward with wave number: final XP = xpReward × (1 + xpWaveScale × currentWave). " +
             "Set 0 to keep flat XP. e.g. 0.5 = +50% XP per wave.")]
    public float xpWaveScale = 0f;

    // ─── HP ───────────────────────────────────────────────────────────────────
    [Tooltip("Flat: restores a fixed amount. Percent: restores a % of max HP. Both: applies flat then percent.")]
    public RestoreMode hpRestoreMode = RestoreMode.Flat;

    [Tooltip("Flat amount of HP restored on pickup.")]
    public int hpReward = 1;

    [Tooltip("Percentage of max HP restored on pickup (0–100).")]
    [Range(0, 100)]
    public float hpRestorePercent = 25f;

    // ─── Mana ─────────────────────────────────────────────────────────────────
    [Tooltip("Flat: restores a fixed amount. Percent: restores a % of max mana. Both: applies flat then percent.")]
    public RestoreMode manaRestoreMode = RestoreMode.Flat;

    [Tooltip("Flat amount of Mana restored on pickup.")]
    public int manaReward = 25;

    [Tooltip("Percentage of max Mana restored on pickup (0–100).")]
    [Range(0, 100)]
    public float manaRestorePercent = 50f;

    // ─── Material / Item ──────────────────────────────────────────────────────
    [Tooltip("The ItemData ScriptableObject to add to the collecting player's inventory.")]
    public ItemData itemReward;

    // ─── Collider ─────────────────────────────────────────────────────────────
    [Header("Collider")]
    [Tooltip("Auto-sets the trigger SphereCollider radius on Awake. Increase if players miss the pickup by walking over it.")]
    public float pickupRadius = 0.8f;

    // ─── Common ───────────────────────────────────────────────────────────────
    [Header("Settings")]
    [Tooltip("Tag used to identify players. Must match the Player GameObject tag.")]
    public string playerTag = "Player";

    [Header("Lifetime")]
    [Tooltip("Seconds before this pickup auto-despawns if not collected. 0 = never expires.")]
    public float lifetime = 30f;

    [Header("Effects (optional)")]
    [Tooltip("Particle prefab spawned at the pickup's position on collection.")]
    public GameObject pickupParticles;

    [Tooltip("Sound played on collection.")]
    public AudioClip pickupSound;

    // ─── Spawner Link (optional) ──────────────────────────────────────────────
    [HideInInspector]
    [Tooltip("Set automatically by ItemSpawner. Notified when this pickup is collected.")]
    public ItemSpawner spawner;

    // ─── Internal ─────────────────────────────────────────────────────────────
    private bool _collected = false;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    void Awake()
    {
        SphereCollider sc = GetComponent<SphereCollider>();
        if (sc != null) sc.radius = pickupRadius;
    }

    // ─── Network Lifecycle ────────────────────────────────────────────────────
    public override void OnNetworkSpawn()
    {
        if (IsServer && lifetime > 0f)
            Invoke(nameof(ExpirePickup), lifetime);
    }

    // ─── Trigger ──────────────────────────────────────────────────────────────
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (_collected) return;

        // Solo / offline — collect directly, no NGO needed
        if (!IsNetworkActive())
        {
            _collected = true;
            ApplyReward(other.gameObject);
            if (spawner != null) spawner.ItemCollected();
            PlayFX();
            Destroy(gameObject);
            return;
        }

        // Multiplayer — only the local owner of the player triggers collection
        NetworkObject playerNet = other.GetComponent<NetworkObject>();
        if (playerNet == null || !playerNet.IsOwner) return;
        CollectServerRpc(playerNet.OwnerClientId);
    }

    // ─── Server RPC ───────────────────────────────────────────────────────────
    [Rpc(SendTo.Server)]
    private void CollectServerRpc(ulong collectorClientId)
    {
        if (_collected) return;
        _collected = true;

        // Send reward to the collecting client only — HealthSystem / ExperienceManager
        // are plain MonoBehaviours with no NetworkVariables, so rewards must be applied
        // on the owning client rather than on the server's copy of the player.
        ClientRpcParams ownerOnly = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { collectorClientId } }
        };
        ApplyRewardClientRpc(ownerOnly);

        if (spawner != null) spawner.ItemCollected();

        PlayFXClientRpc();

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    // ─── Apply Reward Client RPC ──────────────────────────────────────────────
    // Runs only on the collecting player's machine. Finds the locally-owned player
    // GameObject and applies the reward directly to its components.
    [ClientRpc]
    private void ApplyRewardClientRpc(ClientRpcParams clientRpcParams = default)
    {
        GameObject playerObj = FindLocalPlayer();
        if (playerObj != null)
            ApplyReward(playerObj);
        else
            Debug.LogWarning("[LootPickup] ApplyRewardClientRpc: could not find local owned player.");
    }

    // ─── Reward Logic (shared between solo and MP paths) ──────────────────────
    private void ApplyReward(GameObject playerObj)
    {
        switch (lootType)
        {
            // ── XP ────────────────────────────────────────────────────────────
            case LootType.XPReward:
                ExperienceManager xp = playerObj.GetComponent<ExperienceManager>();
                if (xp != null)
                {
                    int finalXP = xpReward;
                    if (xpWaveScale > 0f && WaveManager.Instance != null)
                        finalXP = Mathf.Max(1, Mathf.RoundToInt(xpReward * (1f + xpWaveScale * WaveManager.Instance.CurrentWave)));
                    xp.GainXP(finalXP);
                }
                else Debug.LogWarning("[LootPickup] No ExperienceManager on collecting player.");
                break;

            // ── HP ────────────────────────────────────────────────────────────
            case LootType.HPPotion:
                HealthSystem health = playerObj.GetComponent<HealthSystem>();
                if (health != null)
                {
                    if (hpRestoreMode == RestoreMode.Flat || hpRestoreMode == RestoreMode.Both)
                        health.Heal(hpReward);
                    if (hpRestoreMode == RestoreMode.Percent || hpRestoreMode == RestoreMode.Both)
                        health.Heal(Mathf.RoundToInt(health.maxHealth * (hpRestorePercent / 100f)));
                }
                else Debug.LogWarning("[LootPickup] No HealthSystem on collecting player.");
                break;

            // ── Mana ──────────────────────────────────────────────────────────
            case LootType.ManaPotion:
                // TODO: uncomment once ManaSystem is implemented
                // ManaSystem mana = playerObj.GetComponent<ManaSystem>();
                // if (mana != null)
                // {
                //     if (manaRestoreMode == RestoreMode.Flat || manaRestoreMode == RestoreMode.Both)
                //         mana.RestoreMana(manaReward);
                //     if (manaRestoreMode == RestoreMode.Percent || manaRestoreMode == RestoreMode.Both)
                //         mana.RestoreManaPercent(manaRestorePercent / 100f);
                // }
                Debug.Log($"[LootPickup] Mana pickup collected ({manaRestoreMode}) — ManaSystem not yet implemented.");
                break;

            // ── Item / Material ───────────────────────────────────────────────
            case LootType.Material:
                if (itemReward != null)
                {
                    PlayerInventory inventory = playerObj.GetComponent<PlayerInventory>();
                    if (inventory != null) inventory.AddItem(itemReward);
                    else Debug.LogWarning("[LootPickup] No PlayerInventory on collecting player.");
                }
                else Debug.LogWarning("[LootPickup] Material pickup has no ItemData assigned.");
                break;
        }
    }

    // ─── Network State Helper ─────────────────────────────────────────────────
    private static bool IsNetworkActive() =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    // ─── Local FX (solo path) ─────────────────────────────────────────────────
    private void PlayFX()
    {
        if (pickupParticles != null)
            Instantiate(pickupParticles, transform.position, Quaternion.identity);
        if (pickupSound != null)
            AudioSource.PlayClipAtPoint(pickupSound, transform.position);
    }

    // ─── Player Lookup Helpers ────────────────────────────────────────────────
    // Finds a player by NGO OwnerClientId. Safer than client.PlayerObject
    // which requires SpawnAsPlayerObject (PlayerSpawner uses SpawnWithOwnership).
    private GameObject FindPlayerByClientId(ulong clientId)
    {
        foreach (GameObject p in GameObject.FindGameObjectsWithTag("Player"))
        {
            NetworkObject net = p.GetComponent<NetworkObject>();
            if (net != null && net.OwnerClientId == clientId)
                return p;
        }
        return null;
    }

    // Returns the player GameObject owned by the local client.
    // Used inside ClientRpcs so the receiving machine can find its own player.
    private GameObject FindLocalPlayer()
    {
        foreach (GameObject p in GameObject.FindGameObjectsWithTag("Player"))
        {
            NetworkObject net = p.GetComponent<NetworkObject>();
            if (net != null && net.IsOwner)
                return p;
        }
        return null;
    }

    // ─── Lifetime Expiry ──────────────────────────────────────────────────────
    private void ExpirePickup()
    {
        if (_collected) return;
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    // ─── Client RPC ───────────────────────────────────────────────────────────
    [ClientRpc]
    private void PlayFXClientRpc()
    {
        if (pickupParticles != null)
            Instantiate(pickupParticles, transform.position, Quaternion.identity);

        if (pickupSound != null)
            AudioSource.PlayClipAtPoint(pickupSound, transform.position);
    }
}
