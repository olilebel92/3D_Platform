using System.Collections.Generic;
using Unity.Collections;
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
    ManaPotion,
    Material,     // adds a specific ItemData to the player's inventory
    Items,        // Random pick from a curated ItemData pool — server-authoritative roll
    RandomItem    // Procedurally rolled ItemData from ItemGenerator — server-authoritative
}

// ─── Restore Mode ─────────────────────────────────────────────────────────────
/// <summary>
/// Shared by HP and Mana potions.
/// Flat: restores a fixed amount.
/// Percent: restores a % of the player's max value.
/// Both: applies flat amount first, then the percentage on top.
/// </summary>
public enum RestoreMode { Flat, Percent, Both }

// ─── Rolled Loot ──────────────────────────────────────────────────────────────
/// <summary>
/// Single atomic payload describing the server's roll for a pickup. Keeps subType +
/// rarity (RandomItem) or the pool item (Items) in one NetworkVariable so clients
/// rebuild the visual exactly once. GUIDs are used instead of catalog indices so
/// designer reordering of ItemGenerator.subTypes / .rarities cannot remap in-flight
/// pickups to the wrong asset.
/// </summary>
public struct RolledLoot : INetworkSerializable, System.IEquatable<RolledLoot>
{
    public FixedString64Bytes itemGuid;     // LootType.Items     — chosen ItemData GUID
    public FixedString64Bytes subTypeGuid;  // LootType.RandomItem — chosen SubTypeData GUID
    public FixedString64Bytes rarityGuid;   // LootType.RandomItem — chosen RarityData GUID

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref itemGuid);
        serializer.SerializeValue(ref subTypeGuid);
        serializer.SerializeValue(ref rarityGuid);
    }

    public bool Equals(RolledLoot other)
        => itemGuid.Equals(other.itemGuid)
        && subTypeGuid.Equals(other.subTypeGuid)
        && rarityGuid.Equals(other.rarityGuid);

    public override bool Equals(object obj) => obj is RolledLoot r && Equals(r);
    public override int GetHashCode() => System.HashCode.Combine(itemGuid, subTypeGuid, rarityGuid);
}

/// <summary>
/// General-purpose ground pickup. Select a LootType in the Inspector
/// to configure only the fields that apply to that reward.
///
/// For Material/Items/RandomItem pickups the SubType.worldModelPrefab is instanced on top
/// of this pickup and the Rarity drives the glow colour via PickupVisual.
///
/// Multiplayer flow:
///   1. Server rolls the loot in OnNetworkSpawn and writes a single NetworkVariable&lt;RolledLoot&gt;
///      so all clients render the same model/glow in one visual pass.
///   2. OnTriggerEnter fires on the local client whose player touched the pickup.
///   3. That client calls CollectServerRpc — server validates and processes rewards once.
///   4. Server despawns the object so it disappears for everyone.
///   5. FX (particles/sound) are triggered on all clients via ClientRpc.
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

    // ─── Items Pool ───────────────────────────────────────────────────────────
    [Tooltip("Pool of ItemData. One entry is chosen at random on spawn and added to the player's inventory. " +
             "The random pick is rolled on the server in multiplayer so every client agrees on the outcome.")]
    public List<ItemData> itemPool = new();

    // ─── Random Item ──────────────────────────────────────────────────────────
    [Tooltip("RandomItem only: overrides the wave used for rarity scaling. Set < 1 to use WaveManager.CurrentWave.")]
    public int randomItemWaveOverride = 0;

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

    // Single atomic NetworkVariable carrying the server's roll. Subscribing once and
    // reading on every change avoids the double-Instantiate/Destroy flicker that the
    // previous 3-NV layout produced for RandomItem pickups.
    private readonly NetworkVariable<RolledLoot> _rolledLoot =
        new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Solo-mode mirror. The NetworkVariable.Value setter must not be touched before
    // OnNetworkSpawn, so in singleplayer we route through this plain field instead.
    private RolledLoot _soloRolledLoot;

    // Current roll, routed to the right source depending on network state.
    private RolledLoot CurrentRoll => IsNetworkActive() ? _rolledLoot.Value : _soloRolledLoot;

    private PickupVisual _visual;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    void Awake()
    {
        SphereCollider sc = GetComponent<SphereCollider>();
        if (sc != null) sc.radius = pickupRadius;

        _visual = GetComponent<PickupVisual>();
        if (_visual == null && (lootType == LootType.Items || lootType == LootType.RandomItem))
            Debug.LogWarning($"[LootPickup] '{name}' has lootType {lootType} but no PickupVisual component — the pickup will be invisible.");
    }

    void Start()
    {
        // Solo (no NGO) — roll and apply visuals immediately. In MP this is deferred
        // until OnNetworkSpawn so the server can roll first and sync.
        if (!IsNetworkActive())
        {
            _soloRolledLoot = RollLootLocally();
            ApplyVisualForCurrentItem();
        }
    }

    // ─── Network Lifecycle ────────────────────────────────────────────────────
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _rolledLoot.Value = RollLootLocally();
            if (lifetime > 0f) Invoke(nameof(ExpirePickup), lifetime);
        }

        _rolledLoot.OnValueChanged += OnRolledLootChanged;
        ApplyVisualForCurrentItem();
    }

    public override void OnNetworkDespawn()
    {
        _rolledLoot.OnValueChanged -= OnRolledLootChanged;
    }

    // Performs the per-type roll. Pure-local: returns the rolled struct without
    // touching any NetworkVariable. The caller decides where to store it.
    private RolledLoot RollLootLocally()
    {
        RolledLoot roll = default;

        if (lootType == LootType.Items)
        {
            int idx = RollItemsIndex();
            if (idx >= 0 && itemPool[idx] != null)
                roll.itemGuid = itemPool[idx].AssetGuid ?? "";
        }
        else if (lootType == LootType.RandomItem)
        {
            if (ItemGenerator.Instance == null)
            {
                Debug.LogWarning("[LootPickup] RandomItem: ItemGenerator.Instance is null — visuals will be empty.");
                return roll;
            }
            int wave = randomItemWaveOverride > 0
                ? randomItemWaveOverride
                : (WaveManager.Instance != null ? WaveManager.Instance.CurrentWave : 1);

            if (ItemGenerator.Instance.RollWaveAppearance(wave, out string subGuid, out string rarGuid))
            {
                roll.subTypeGuid = subGuid ?? "";
                roll.rarityGuid  = rarGuid ?? "";
            }
        }

        return roll;
    }

    private void OnRolledLootChanged(RolledLoot prev, RolledLoot next) => ApplyVisualForCurrentItem();

    private void ApplyVisualForCurrentItem()
    {
        if (_visual == null) return;

        // Resolve subType + rarity from the appropriate source for this LootType.
        SubTypeData sub = null;
        RarityData  rar = null;

        switch (lootType)
        {
            case LootType.Material:
                if (itemReward != null) { sub = itemReward.subType; rar = itemReward.rarity; }
                break;

            case LootType.Items:
                ItemData pooled = FindItemInPoolByGuid(CurrentRoll.itemGuid);
                if (pooled != null) { sub = pooled.subType; rar = pooled.rarity; }
                break;

            case LootType.RandomItem:
                if (ItemGenerator.Instance != null)
                {
                    sub = ItemGenerator.Instance.GetSubTypeByGuid(CurrentRoll.subTypeGuid.ToString());
                    rar = ItemGenerator.Instance.GetRarityByGuid(CurrentRoll.rarityGuid.ToString());
                }
                break;
        }

        _visual.ApplyItemVisuals(sub, rar);
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
            ApplyReward(other.gameObject, _soloRolledLoot);
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

        RolledLoot snapshot = _rolledLoot.Value;

        // Send reward to the collecting client only — HealthSystem / ExperienceManager
        // are per-player and live on the owning client's machine.
        ClientRpcParams ownerOnly = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { collectorClientId } }
        };
        ApplyRewardClientRpc(snapshot, ownerOnly);

        if (spawner != null) spawner.ItemCollected();

        PlayFXClientRpc();

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    // ─── Apply Reward Client RPC ──────────────────────────────────────────────
    [ClientRpc]
    private void ApplyRewardClientRpc(RolledLoot roll, ClientRpcParams clientRpcParams = default)
    {
        GameObject playerObj = FindLocalPlayer();
        if (playerObj != null)
            ApplyReward(playerObj, roll);
        else
            Debug.LogWarning("[LootPickup] ApplyRewardClientRpc: could not find local owned player.");
    }

    // ─── Items Roll Helper ────────────────────────────────────────────────────
    private int RollItemsIndex()
    {
        if (lootType != LootType.Items) return -1;
        if (itemPool == null || itemPool.Count == 0) return -1;
        return Random.Range(0, itemPool.Count);
    }

    private ItemData FindItemInPoolByGuid(FixedString64Bytes guid)
    {
        if (itemPool == null || itemPool.Count == 0 || guid.Length == 0) return null;
        string g = guid.ToString();
        foreach (var it in itemPool)
            if (it != null && it.AssetGuid == g) return it;
        return null;
    }

    // ─── Reward Logic (shared between solo and MP paths) ──────────────────────
    private void ApplyReward(GameObject playerObj, RolledLoot roll)
    {
        switch (lootType)
        {
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

            case LootType.ManaPotion:
                ManaSystem mana = playerObj.GetComponent<ManaSystem>();
                if (mana != null)
                {
                    if (manaRestoreMode == RestoreMode.Flat || manaRestoreMode == RestoreMode.Both)
                        mana.RestoreMana(manaReward);
                    if (manaRestoreMode == RestoreMode.Percent || manaRestoreMode == RestoreMode.Both)
                        mana.RestoreManaPercent(manaRestorePercent / 100f);
                }
                else Debug.LogWarning("[LootPickup] No ManaSystem on collecting player.");
                break;

            case LootType.Material:
                if (itemReward != null)
                {
                    PlayerInventory inventory = playerObj.GetComponent<PlayerInventory>();
                    if (inventory != null) inventory.AddItem(itemReward);
                    else Debug.LogWarning("[LootPickup] No PlayerInventory on collecting player.");
                }
                else Debug.LogWarning("[LootPickup] Material pickup has no ItemData assigned.");
                break;

            case LootType.Items:
                if (itemPool == null || itemPool.Count == 0)
                {
                    Debug.LogWarning("[LootPickup] Items pickup has an empty itemPool.");
                    break;
                }
                ItemData chosen = FindItemInPoolByGuid(roll.itemGuid);
                if (chosen == null)
                {
                    Debug.LogWarning($"[LootPickup] Items pickup could not resolve GUID '{roll.itemGuid}' inside its itemPool.");
                    break;
                }
                PlayerInventory poolInventory = playerObj.GetComponent<PlayerInventory>();
                if (poolInventory != null) poolInventory.AddItem(chosen);
                else Debug.LogWarning("[LootPickup] No PlayerInventory on collecting player.");
                break;

            case LootType.RandomItem:
                if (ItemGenerator.Instance == null)
                {
                    Debug.LogWarning("[LootPickup] RandomItem: ItemGenerator.Instance is null on collecting client.");
                    break;
                }
                SubTypeData sub = ItemGenerator.Instance.GetSubTypeByGuid(roll.subTypeGuid.ToString());
                RarityData  rar = ItemGenerator.Instance.GetRarityByGuid(roll.rarityGuid.ToString());
                if (sub == null || rar == null)
                {
                    Debug.LogWarning($"[LootPickup] RandomItem: server-synced GUIDs not in catalog (sub='{roll.subTypeGuid}', rar='{roll.rarityGuid}').");
                    break;
                }
                ItemData rolled = ItemGenerator.Instance.GenerateItem(sub, rar);
                PlayerInventory randInv = playerObj.GetComponent<PlayerInventory>();
                if (randInv != null) randInv.AddItem(rolled);
                else Debug.LogWarning("[LootPickup] No PlayerInventory on collecting player.");
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
    private GameObject FindPlayerByClientId(ulong clientId)
    {
        foreach (GameObject p in PlayerController.All)
        {
            NetworkObject net = p.GetComponent<NetworkObject>();
            if (net != null && net.OwnerClientId == clientId)
                return p;
        }
        return null;
    }

    private GameObject FindLocalPlayer()
    {
        foreach (GameObject p in PlayerController.All)
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
