using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using TMPro;

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
    public FixedString64Bytes  itemGuid;         // LootType.Items      — chosen ItemData GUID
    public FixedString64Bytes  subTypeGuid;      // LootType.RandomItem — chosen SubTypeData GUID
    public FixedString64Bytes  rarityGuid;       // LootType.RandomItem — chosen RarityData GUID
    public FixedString128Bytes itemName;         // LootType.RandomItem — pre-generated display name
    public FixedString512Bytes statsSerialized;  // LootType.RandomItem — "statTypeInt:value|..." pairs

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref itemGuid);
        serializer.SerializeValue(ref subTypeGuid);
        serializer.SerializeValue(ref rarityGuid);
        serializer.SerializeValue(ref itemName);
        serializer.SerializeValue(ref statsSerialized);
    }

    public bool Equals(RolledLoot other)
        => itemGuid.Equals(other.itemGuid)
        && subTypeGuid.Equals(other.subTypeGuid)
        && rarityGuid.Equals(other.rarityGuid)
        && itemName.Equals(other.itemName)
        && statsSerialized.Equals(other.statsSerialized);

    public override bool Equals(object obj) => obj is RolledLoot r && Equals(r);
    public override int GetHashCode() => System.HashCode.Combine(itemGuid, subTypeGuid, rarityGuid, statsSerialized);
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

    // ─── Interaction Prompt (LootType.Items only) ─────────────────────────────
    [Header("Interaction Prompt (Items only)")]
    [Tooltip("World-vertical offset for the 'Press F to pick up' label above the item.")]
    public float interactionLabelHeight = 1.4f;

    [Tooltip("World-space font size of the interaction label.")]
    public float interactionLabelFontSize = 1.2f;

    [Tooltip("Tint of the interaction label text.")]
    public Color interactionLabelColor = Color.white;

    // ─── Tooltip (LootType.Items only) ────────────────────────────────────────
    [Header("Stats Tooltip (Items only)")]
    [Tooltip("World-vertical offset for the stats tooltip label below the interaction prompt.")]
    public float tooltipLabelHeight = 0.6f;

    [Tooltip("World-space font size of the stats tooltip.")]
    public float tooltipLabelFontSize = 0.9f;

    [Tooltip("Tint of the stats tooltip text.")]
    public Color tooltipLabelColor = new Color(0.85f, 0.85f, 0.85f, 1f);

    // ─── Pre-Init (inventory drop support) ───────────────────────────────────
    // Set via PreInitDroppedItem before Spawn so dropped items work in both SP and MP.
    private RolledLoot _preInitRoll;
    private ItemData   _soloPreInitItem; // SP fallback: direct reference, no ItemGenerator needed

    // ─── Internal ─────────────────────────────────────────────────────────────
    private bool _collected = false;

    // RandomItem: fully generated at spawn so the tooltip shows real stats.
    // Set on server in RollLootLocally(); reconstructed on clients from synced RolledLoot data.
    private ItemData _generatedItem;

    // ─── One-at-a-time Focus (static, per-client) ─────────────────────────────
    // Only the focused pickup shows its interaction prompt. When the player enters
    // range of multiple items, only the nearest one is focused at a time.
    // Tab cycles through overlapping pickups in the order they were added.
    private static readonly HashSet<LootPickup> _localNearby  = new();
    private static          LootPickup           _localFocused = null;

    // Items and RandomItem loot are interactable — player must press F to collect.
    private bool RequiresInteraction => lootType == LootType.Items || lootType == LootType.RandomItem;

    private TextMeshPro _interactionLabel;
    private TextMeshPro _tooltipLabel;
    private GameObject  _localOwnerInRange;
    private InputAction _interactAction;

    // Single atomic NetworkVariable carrying the server's roll. Subscribing once and
    // reading on every change avoids the double-Instantiate/Destroy flicker that the
    // previous 3-NV layout produced for RandomItem pickups.
    private readonly NetworkVariable<RolledLoot> _rolledLoot =
        new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // lootType is a plain field, so server-side changes (player drops, enemy ItemPool drops)
    // never replicate — remote clients keep the prefab default. Sync it explicitly so clients
    // resolve visuals, interaction prompts and tooltips for the correct type.
    private readonly NetworkVariable<LootType> _netLootType =
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

        if (RequiresInteraction)
        {
            CreateInteractionLabel();
            CreateTooltipLabel();
        }
    }

    void Start()
    {
        // Solo (no NGO) — roll and apply visuals immediately. In MP this is deferred
        // until OnNetworkSpawn so the server can roll first and sync.
        if (!IsNetworkActive())
        {
            bool hasPreInit = _preInitRoll.itemName.Length > 0 || _preInitRoll.statsSerialized.Length > 0;
            _soloRolledLoot = hasPreInit ? _preInitRoll : RollLootLocally();
            if (lootType == LootType.RandomItem && hasPreInit && _generatedItem == null)
                ReconstructGeneratedItem(_soloRolledLoot);
            if (_generatedItem == null && _soloPreInitItem != null)
                _generatedItem = _soloPreInitItem;
            ApplyVisualForCurrentItem();
        }
    }

    // ─── Network Lifecycle ────────────────────────────────────────────────────
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _netLootType.Value = lootType;

            bool hasPreInit = _preInitRoll.itemName.Length > 0 || _preInitRoll.statsSerialized.Length > 0;
            _rolledLoot.Value = hasPreInit ? _preInitRoll : RollLootLocally();
            // Resolve _generatedItem for visuals — needed on host-client which renders the scene.
            if (hasPreInit && lootType == LootType.RandomItem && _generatedItem == null)
                ReconstructGeneratedItem(_rolledLoot.Value);
            if (_generatedItem == null && _soloPreInitItem != null)
                _generatedItem = _soloPreInitItem;
            if (lifetime > 0f) Invoke(nameof(ExpirePickup), lifetime);
        }
        else
        {
            // lootType is a plain field — sync it from the server so server-configured
            // pickups (player drops, enemy ItemPool drops) behave correctly on clients.
            lootType = _netLootType.Value;

            // Awake ran with the prefab-default lootType — create interaction labels now if needed.
            if (RequiresInteraction)
            {
                if (_interactionLabel == null) CreateInteractionLabel();
                if (_tooltipLabel == null)     CreateTooltipLabel();
            }

            // Initial NetworkVariable state is already populated on the client by the time
            // OnNetworkSpawn runs — reconstruct _generatedItem before showing visuals/tooltip.
            if (lootType == LootType.RandomItem)
                ReconstructGeneratedItem(_rolledLoot.Value);
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

                // Generate the full item now so stats are known before pickup.
                SubTypeData sub = ItemGenerator.Instance.GetSubTypeByGuid(subGuid);
                RarityData  rar = ItemGenerator.Instance.GetRarityByGuid(rarGuid);
                _generatedItem  = ItemGenerator.Instance.GenerateItem(sub, rar);

                if (_generatedItem != null)
                {
                    roll.itemName        = _generatedItem.itemName ?? "";
                    roll.statsSerialized = SerializeStats(_generatedItem.statLines);
                }
            }
        }

        return roll;
    }

    private void OnRolledLootChanged(RolledLoot prev, RolledLoot next)
    {
        // Reconstruct on clients if not already done in OnNetworkSpawn (safety fallback).
        if (!IsServer && lootType == LootType.RandomItem && _generatedItem == null)
            ReconstructGeneratedItem(next);
        ApplyVisualForCurrentItem();
    }

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
                // Fallback for player-dropped items whose subType/rarity aren't in the ItemGenerator catalog.
                if (sub == null && _generatedItem != null) sub = _generatedItem.subType;
                if (rar == null && _generatedItem != null) rar = _generatedItem.rarity;
                break;
        }

        _visual.ApplyItemVisuals(sub, rar);

        // Keep labels in sync with the rolled item if the player is already in range.
        if (RequiresInteraction && _localOwnerInRange != null)
        {
            if (_interactionLabel != null) _interactionLabel.text = BuildInteractionText();
            if (_tooltipLabel != null)     _tooltipLabel.text     = BuildTooltipText();
        }
    }

    // ─── Interaction Prompt ───────────────────────────────────────────────────
    private void CreateInteractionLabel()
    {
        GameObject obj = new GameObject("InteractionPrompt");
        obj.transform.SetParent(transform, worldPositionStays: false);
        obj.transform.localPosition = new Vector3(0f, interactionLabelHeight, 0f);
        obj.transform.localRotation = Quaternion.identity;

        _interactionLabel = obj.AddComponent<TextMeshPro>();
        _interactionLabel.fontSize  = interactionLabelFontSize;
        _interactionLabel.color     = interactionLabelColor;
        _interactionLabel.alignment = TextAlignmentOptions.Center;
        _interactionLabel.fontStyle = FontStyles.Bold;
        _interactionLabel.text      = "";

        MeshRenderer mr = _interactionLabel.GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = 20000;

        int hudLayer = LayerMask.NameToLayer("HudOverlay");
        if (hudLayer >= 0) obj.layer = hudLayer;

        obj.SetActive(false);
    }

    private void CreateTooltipLabel()
    {
        GameObject obj = new GameObject("StatsTooltip");
        obj.transform.SetParent(transform, worldPositionStays: false);
        obj.transform.localPosition = new Vector3(0f, tooltipLabelHeight, 0f);
        obj.transform.localRotation = Quaternion.identity;

        _tooltipLabel = obj.AddComponent<TextMeshPro>();
        _tooltipLabel.fontSize  = tooltipLabelFontSize;
        _tooltipLabel.color     = tooltipLabelColor;
        _tooltipLabel.alignment = TextAlignmentOptions.Center;
        _tooltipLabel.text      = "";

        MeshRenderer mr = _tooltipLabel.GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = 20000;

        int hudLayer = LayerMask.NameToLayer("HudOverlay");
        if (hudLayer >= 0) obj.layer = hudLayer;

        obj.SetActive(false);
    }

    private void ShowInteractionPrompt()
    {
        if (_interactionLabel != null)
        {
            _interactionLabel.text = BuildInteractionText();
            _interactionLabel.gameObject.SetActive(true);
        }
        if (_tooltipLabel != null)
        {
            _tooltipLabel.text = BuildTooltipText();
            _tooltipLabel.gameObject.SetActive(true);
        }
    }

    private void HideInteractionPrompt()
    {
        if (_interactionLabel != null) _interactionLabel.gameObject.SetActive(false);
        if (_tooltipLabel != null)     _tooltipLabel.gameObject.SetActive(false);
    }

    private string BuildInteractionText()
    {
        if (lootType == LootType.RandomItem)
        {
            if (_generatedItem != null)
            {
                string rarHex  = _generatedItem.rarity != null ? _generatedItem.rarity.Hex : "FFFFFF";
                string rarName = _generatedItem.rarity != null
                    ? $"<color=#{rarHex}>{_generatedItem.rarity.displayName}</color> "
                    : "";
                return $"[F] Pick up\n{rarName}{_generatedItem.itemName}";
            }
            return "[F] Pick up\nRandom Item";
        }

        ItemData pooled = FindItemInPoolByGuid(CurrentRoll.itemGuid);
        if (pooled != null)
        {
            string rarHex  = pooled.rarity != null ? pooled.rarity.Hex : "FFFFFF";
            string rarName = pooled.rarity != null
                ? $"<color=#{rarHex}>{pooled.rarity.displayName}</color> "
                : "";
            return $"[F] Pick up\n{rarName}{pooled.itemName}";
        }
        return "[F] Pick up\nItem";
    }

    private string BuildTooltipText()
    {
        if (lootType == LootType.RandomItem)
            return _generatedItem != null ? _generatedItem.BuildStatSummary() : "";

        ItemData pooled = FindItemInPoolByGuid(CurrentRoll.itemGuid);
        return pooled != null ? pooled.BuildStatSummary() : "";
    }

    void LateUpdate()
    {
        // Billboard both labels toward the local camera.
        Camera cam = Camera.main;
        if (cam == null) return;
        Quaternion rot = Quaternion.LookRotation(cam.transform.forward);

        if (_interactionLabel != null && _interactionLabel.gameObject.activeSelf)
            _interactionLabel.transform.rotation = rot;
        if (_tooltipLabel != null && _tooltipLabel.gameObject.activeSelf)
            _tooltipLabel.transform.rotation = rot;
    }

    // ─── Trigger ──────────────────────────────────────────────────────────────
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (_collected) return;

        // Items / RandomItem loot: show the interaction prompt for the local owner only.
        // Only the nearest pickup in range is focused at a time.
        if (RequiresInteraction)
        {
            // In MP, only the local owner sees the prompt and can interact.
            if (IsNetworkActive())
            {
                NetworkObject playerNet = other.GetComponent<NetworkObject>();
                if (playerNet == null || !playerNet.IsOwner) return;
            }

            _localOwnerInRange = other.gameObject;
            PlayerController pc = other.GetComponent<PlayerController>();
            if (pc != null && pc.InputActions != null)
                _interactAction = pc.InputActions.FindAction("Interact");

            _localNearby.Add(this);
            RefreshLocalFocus(other.gameObject);
            return;
        }

        // Solo / offline — or a non-networked pickup (e.g. LootVisualTester preview):
        // collect directly without an RPC, which would NRE on an unspawned object.
        if (!IsNetworkActive() || !IsSpawned)
        {
            _collected = true;
            ApplyReward(other.gameObject, _soloRolledLoot);
            if (spawner != null) spawner.ItemCollected();
            PlayFX();
            Destroy(gameObject);
            return;
        }

        // Multiplayer — only the local owner of the player triggers collection
        NetworkObject net = other.GetComponent<NetworkObject>();
        if (net == null || !net.IsOwner) return;
        CollectServerRpc(net.OwnerClientId);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!RequiresInteraction) return;
        if (other.gameObject != _localOwnerInRange) return;

        _localNearby.Remove(this);
        if (_localFocused == this)
        {
            HideInteractionPrompt();
            _localFocused = null;
        }

        _localOwnerInRange = null;
        _interactAction    = null;

        // Refocus on the next closest pickup still in range (if any).
        RefreshLocalFocus(other.gameObject);
    }

    void Update()
    {
        // Only the focused pickup listens for the Interact input.
        if (!RequiresInteraction || _collected) return;
        if (_localFocused != this) return;

        // Tab cycles to the next nearby pickup when multiple items overlap.
        if (_localNearby.Count > 1 && Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
        {
            CycleLocalFocus();
            return;
        }

        if (_interactAction == null || !_interactAction.enabled) return;
        if (!_interactAction.WasPressedThisFrame()) return;
        TryInteractCollect();
    }

    private void TryInteractCollect()
    {
        if (_collected) return;

        // Release focus so the next nearest pickup can become active immediately.
        _localNearby.Remove(this);
        if (_localFocused == this) _localFocused = null;
        HideInteractionPrompt();
        RefreshLocalFocus(_localOwnerInRange);

        // Solo / offline — or a non-networked pickup (e.g. LootVisualTester preview):
        // collect directly without an RPC, which would NRE on an unspawned object.
        if (!IsNetworkActive() || !IsSpawned)
        {
            _collected = true;
            ApplyReward(_localOwnerInRange, _soloRolledLoot);
            if (spawner != null) spawner.ItemCollected();
            PlayFX();
            Destroy(gameObject);
            return;
        }

        NetworkObject net = _localOwnerInRange.GetComponent<NetworkObject>();
        if (net == null || !net.IsOwner) return;
        CollectServerRpc(net.OwnerClientId);
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
                // Use the item generated at spawn time so stats match the tooltip.
                // Fall back to a fresh roll only if _generatedItem is somehow missing.
                ItemData randomItem = _generatedItem ?? _soloPreInitItem;
                if (randomItem == null)
                {
                    Debug.LogWarning("[LootPickup] RandomItem: _generatedItem is null at collection — regenerating from synced GUIDs.");
                    if (ItemGenerator.Instance == null) break;
                    SubTypeData sub = ItemGenerator.Instance.GetSubTypeByGuid(roll.subTypeGuid.ToString());
                    RarityData  rar = ItemGenerator.Instance.GetRarityByGuid(roll.rarityGuid.ToString());
                    if (sub == null || rar == null)
                    {
                        Debug.LogWarning($"[LootPickup] RandomItem: server-synced GUIDs not in catalog (sub='{roll.subTypeGuid}', rar='{roll.rarityGuid}').");
                        break;
                    }
                    randomItem = ItemGenerator.Instance.GenerateItem(sub, rar);
                }
                if (randomItem == null) break;
                PlayerInventory randInv = playerObj.GetComponent<PlayerInventory>();
                if (randInv != null) randInv.AddItem(randomItem);
                else Debug.LogWarning("[LootPickup] No PlayerInventory on collecting player.");
                break;
        }
    }

    // ─── Focus Helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the nearest RequiresInteraction pickup in _localNearby and makes it the
    /// sole focused pickup. The previously focused one hides its prompt; the new one
    /// shows its prompt. Pass the player GameObject for distance comparison.
    /// </summary>
    private static void RefreshLocalFocus(GameObject player)
    {
        if (player == null) return;

        LootPickup closest     = null;
        float      closestDist = float.MaxValue;

        foreach (LootPickup p in _localNearby)
        {
            if (p == null || p._collected) continue;
            float d = Vector3.Distance(p.transform.position, player.transform.position);
            if (d < closestDist) { closestDist = d; closest = p; }
        }

        if (closest == _localFocused) return; // no change

        // Hand off the prompt.
        _localFocused?.HideInteractionPrompt();
        _localFocused = closest;
        _localFocused?.ShowInteractionPrompt();
    }

    private static void CycleLocalFocus()
    {
        var list = new System.Collections.Generic.List<LootPickup>();
        foreach (LootPickup p in _localNearby)
            if (p != null && !p._collected) list.Add(p);
        if (list.Count == 0) return;

        // Sort by distance so Tab always walks nearest → farthest, never bouncing.
        GameObject player = null;
        foreach (LootPickup p in list)
            if (p._localOwnerInRange != null) { player = p._localOwnerInRange; break; }

        if (player != null)
            list.Sort((a, b) =>
                Vector3.Distance(a.transform.position, player.transform.position)
                    .CompareTo(Vector3.Distance(b.transform.position, player.transform.position)));

        int currentIndex = _localFocused != null ? list.IndexOf(_localFocused) : -1;
        int nextIndex    = (currentIndex + 1) % list.Count;

        _localFocused?.HideInteractionPrompt();
        _localFocused = list[nextIndex];
        _localFocused?.ShowInteractionPrompt();
    }

    // ─── RandomItem Helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds _generatedItem on non-server clients from the synced RolledLoot data.
    /// Called in OnNetworkSpawn and OnRolledLootChanged.
    /// </summary>
    private void ReconstructGeneratedItem(RolledLoot roll)
    {
        if (roll.statsSerialized.Length == 0) return;
        if (ItemGenerator.Instance == null) return;

        SubTypeData sub = ItemGenerator.Instance.GetSubTypeByGuid(roll.subTypeGuid.ToString());
        RarityData  rar = ItemGenerator.Instance.GetRarityByGuid(roll.rarityGuid.ToString());
        if (sub == null) return;

        bool isWeapon = sub.mainType != null && sub.mainType.isWeapon;
        _generatedItem = isWeapon
            ? ScriptableObject.CreateInstance<WeaponItemData>()
            : ScriptableObject.CreateInstance<ItemData>();

        _generatedItem.subType   = sub;
        _generatedItem.rarity    = rar;
        _generatedItem.itemName  = roll.itemName.ToString();
        _generatedItem.name      = _generatedItem.itemName;
        _generatedItem.statLines = DeserializeStats(roll.statsSerialized.ToString());
    }

    // ─── Inventory Drop API ───────────────────────────────────────────────────

    /// <summary>
    /// Configure this pickup as a player-dropped inventory item before instantiation or NGO Spawn.
    /// Uses the RandomItem serialization path so item data syncs to all clients via NetworkVariable.
    /// Pass soloFallback to guarantee the item survives without ItemGenerator (singleplayer only).
    /// </summary>
    public void PreInitDroppedItem(RolledLoot roll, ItemData soloFallback = null)
    {
        lootType         = LootType.RandomItem;
        _preInitRoll     = roll;
        _soloPreInitItem = soloFallback;

        // Awake() ran before lootType was set, so labels weren't created — create them now.
        if (_interactionLabel == null) CreateInteractionLabel();
        if (_tooltipLabel     == null) CreateTooltipLabel();
    }

    /// <summary>
    /// Builds a RolledLoot from an ItemData so it can be serialized for PreInitDroppedItem or a ServerRpc.
    /// </summary>
    public static RolledLoot BuildRolledLootForItem(ItemData item)
    {
        if (item == null) return default;
        return new RolledLoot
        {
            subTypeGuid     = item.subType?.AssetGuid ?? "",
            rarityGuid      = item.rarity?.AssetGuid  ?? "",
            itemName        = item.itemName             ?? "",
            statsSerialized = SerializeStats(item.statLines),
        };
    }

    /// <summary>Encodes a stat line list as "typeInt:value|typeInt:value|..." for NetworkVariable sync.</summary>
    private static string SerializeStats(List<StatLine> lines)
    {
        if (lines == null || lines.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append('|');
            sb.Append((int)lines[i].type);
            sb.Append(':');
            sb.Append(lines[i].value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>Decodes a stat line list from the format produced by SerializeStats.</summary>
    private static List<StatLine> DeserializeStats(string s)
    {
        var lines = new List<StatLine>();
        if (string.IsNullOrEmpty(s)) return lines;
        foreach (string part in s.Split('|'))
        {
            int colon = part.IndexOf(':');
            if (colon < 0) continue;
            if (!int.TryParse(part.Substring(0, colon), out int typeInt)) continue;
            if (!float.TryParse(part.Substring(colon + 1),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float val)) continue;
            lines.Add(new StatLine { type = (StatType)typeInt, value = val });
        }
        return lines;
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
