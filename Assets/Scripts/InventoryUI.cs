using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Opens and closes the inventory panel with the I key.
/// Dynamically populates a grid of DraggableItem slots from PlayerInventory.
/// Attach to any persistent GameObject (e.g. the Canvas or Player).
/// </summary>
public class InventoryUI : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Panel")]
    [Tooltip("Root panel GameObject to show/hide.")]
    public GameObject inventoryPanel;

    [Header("Item Grid")]
    [Tooltip("Parent transform with a Grid Layout Group where item slots are spawned.")]
    public Transform itemGridContainer;

    [Tooltip("Prefab used for each item slot (needs DraggableItem component).")]
    public GameObject itemSlotPrefab;


    [Header("Pause")]
    [Tooltip("Pause the game (Time.timeScale = 0) while the inventory is open.")]
    public bool pauseGameWhileOpen = true;

    [Header("Batch Delete")]
    [Tooltip("Dropdown for selecting the max rarity threshold to delete.")]
    [SerializeField] private TMP_Dropdown batchRarityDropdown;

    [Tooltip("Button that opens the batch-delete confirmation panel.")]
    [SerializeField] private Button batchDeleteButton;

    [Tooltip("Confirmation overlay panel (hidden by default).")]
    [SerializeField] private GameObject batchConfirmPanel;

    [Tooltip("Label inside the confirmation panel describing what will be deleted.")]
    [SerializeField] private TMP_Text batchConfirmLabel;

    [Tooltip("Confirm button inside the confirmation panel.")]
    [SerializeField] private Button batchConfirmButton;

    [Tooltip("Cancel button inside the confirmation panel.")]
    [SerializeField] private Button batchCancelButton;

    [Tooltip("All RarityData assets to show in the batch-delete dropdown. Drag every rarity asset here, in ascending sortOrder.")]
    [SerializeField] private List<RarityData> rarityCatalog = new();

    // ─── Private State ────────────────────────────────────────────────────────

    public static bool IsDragging { get; set; }

    public bool IsOpen => _isOpen;

    private bool _isOpen = false;
    private bool _pendingRefresh = false;
    private readonly List<GameObject> _spawnedSlots = new();
    private CharacterWindow _characterWindow;
    private PlayerInventory _subscribedInventory = null;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);
    }

    // Rarities sorted by sortOrder. Built from rarityCatalog on Start; used to populate the dropdown.
    private List<RarityData> _sortedRarities = new();

    void Start()
    {
        _characterWindow = FindFirstObjectByType<CharacterWindow>();

        // In solo mode the player instance is ready at Start; in MP it may arrive later
        // via PlayerInventory.SetAsLocalInstance() → call SubscribeToInventoryIfNeeded()
        // there and also whenever the inventory is opened as a safety net.
        SubscribeToInventoryIfNeeded();

        InitBatchDeleteUI();
    }

    void OnDestroy()
    {
        if (_subscribedInventory != null)
            _subscribedInventory.OnInventoryChanged -= OnInventoryChanged;
    }

    /// <summary>
    /// Subscribes to the current PlayerInventory.Instance if we haven't already.
    /// Safe to call multiple times — no-ops if already subscribed to the same instance.
    /// Handles the MP case where the local player spawns after this Start() has run.
    /// </summary>
    public void SubscribeToInventoryIfNeeded()
    {
        if (PlayerInventory.Instance == null) return;
        if (_subscribedInventory == PlayerInventory.Instance) return;  // already up to date

        // Unsubscribe from any old instance (e.g. after a respawn that replaces the player)
        if (_subscribedInventory != null)
            _subscribedInventory.OnInventoryChanged -= OnInventoryChanged;

        _subscribedInventory = PlayerInventory.Instance;
        _subscribedInventory.OnInventoryChanged += OnInventoryChanged;
        Debug.Log("[InventoryUI] Subscribed to local PlayerInventory.");
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame)
            ToggleInventory();

        if (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame)
            ToggleInventory();

        // Deferred refresh — runs after a drag completes
        if (_pendingRefresh && !IsDragging)
        {
            _pendingRefresh = false;
            RefreshGrid();
        }
    }

    // ─── Batch Delete ─────────────────────────────────────────────────────────

    private void InitBatchDeleteUI()
    {
        if (batchConfirmPanel != null)
            batchConfirmPanel.SetActive(false);

        // Build the sorted-rarity list once. Falls back to ItemGenerator's catalog
        // if rarityCatalog was left empty in the Inspector.
        _sortedRarities = new List<RarityData>();
        if (rarityCatalog != null && rarityCatalog.Count > 0)
            _sortedRarities.AddRange(rarityCatalog);
        else if (ItemGenerator.Instance != null && ItemGenerator.Instance.rarities != null)
            _sortedRarities.AddRange(ItemGenerator.Instance.rarities);

        _sortedRarities.RemoveAll(r => r == null);
        _sortedRarities.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));

        if (batchRarityDropdown != null)
        {
            batchRarityDropdown.ClearOptions();
            var options = new List<TMP_Dropdown.OptionData>();
            foreach (RarityData r in _sortedRarities)
            {
                string hex = r.Hex;
                options.Add(new TMP_Dropdown.OptionData($"<color=#{hex}>{r.displayName}</color>"));
            }
            batchRarityDropdown.AddOptions(options);
        }

        if (batchDeleteButton != null)
            batchDeleteButton.onClick.AddListener(OnBatchDeleteClicked);

        if (batchConfirmButton != null)
            batchConfirmButton.onClick.AddListener(OnConfirmBatchDelete);

        if (batchCancelButton != null)
            batchCancelButton.onClick.AddListener(OnCancelBatchDelete);
    }

    private RarityData GetDropdownSelectedRarity()
    {
        if (batchRarityDropdown == null || _sortedRarities == null) return null;
        int idx = batchRarityDropdown.value;
        if (idx < 0 || idx >= _sortedRarities.Count) return null;
        return _sortedRarities[idx];
    }

    private void OnBatchDeleteClicked()
    {
        if (batchRarityDropdown == null || batchConfirmPanel == null || batchConfirmLabel == null) return;
        if (PlayerInventory.Instance == null) return;

        RarityData maxRarity = GetDropdownSelectedRarity();
        if (maxRarity == null) return;

        int count = PlayerInventory.Instance.CountDeletable(maxRarity);
        string hex = maxRarity.Hex;

        if (count == 0)
        {
            batchConfirmLabel.text = $"No non-equipped items of <color=#{hex}>{maxRarity.displayName}</color> or below to delete.";
        }
        else
        {
            batchConfirmLabel.text =
                $"Delete all <color=#{hex}>{maxRarity.displayName}</color> and below?\n" +
                $"<b>{count} item{(count == 1 ? "" : "s")}</b> will be removed.\n" +
                "<size=80%>Equipped items are safe.</size>";
        }

        batchConfirmPanel.SetActive(true);

        if (batchConfirmButton != null)
            batchConfirmButton.interactable = count > 0;
    }

    private void OnConfirmBatchDelete()
    {
        if (PlayerInventory.Instance == null) return;

        RarityData maxRarity = GetDropdownSelectedRarity();
        if (maxRarity == null) return;

        int deleted = PlayerInventory.Instance.DeleteByMaxRarity(maxRarity);

        if (batchConfirmPanel != null)
            batchConfirmPanel.SetActive(false);

        Debug.Log($"[InventoryUI] Batch deleted {deleted} item(s) ≤ {maxRarity.displayName}.");
    }

    private void OnCancelBatchDelete()
    {
        if (batchConfirmPanel != null)
            batchConfirmPanel.SetActive(false);
    }

    // ─── Toggle ───────────────────────────────────────────────────────────────

    private void ToggleInventory()
    {
        // Dismiss any active tutorial popup when opening a panel
        if (!_isOpen && PopupManager.IsShowing)
            PopupManager.Instance.Hide();

        _isOpen = !_isOpen;

        if (_isOpen && _characterWindow != null)
            _characterWindow.CloseWindow();

        if (_isOpen && SkillTreeManager.Instance != null)
            SkillTreeManager.Instance.CloseWindow();

        if (inventoryPanel != null)
            inventoryPanel.SetActive(_isOpen);

        if (_isOpen)
        {
            // In MP the player may have spawned after this script's Start() ran,
            // so check here whether we still need to subscribe to their inventory.
            SubscribeToInventoryIfNeeded();
            RefreshGrid();
            if (pauseGameWhileOpen) PauseManager.RequestPause();
        }
        else
        {
            ItemTooltip.Instance?.Hide();
            ItemContextMenu.Instance?.Hide();
            if (pauseGameWhileOpen) PauseManager.ReleasePause();
        }

        bool anyPanelOpen = _isOpen || (_characterWindow != null && _characterWindow.IsOpen);
        CameraControllerThirdPerson.IsLocked = anyPanelOpen;

        if (_isOpen) CursorManager.Instance?.OpenMenu();
        else         CursorManager.Instance?.CloseMenu();

        Debug.Log("[InventoryUI] Inventory " + (_isOpen ? "opened." : "closed."));
    }

    /// <summary>Close the inventory externally (e.g. when another panel opens).</summary>
    public void CloseInventory()
    {
        if (!_isOpen) return;
        _isOpen = false;

        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);

        ItemTooltip.Instance?.Hide();
        ItemContextMenu.Instance?.Hide();
        if (pauseGameWhileOpen) PauseManager.ReleasePause();

        bool anyPanelOpen = _characterWindow != null && _characterWindow.IsOpen;
        CameraControllerThirdPerson.IsLocked = anyPanelOpen;

        CursorManager.Instance?.CloseMenu();
    }

    // ─── Grid ─────────────────────────────────────────────────────────────────

    public void OnInventoryChanged()
    {
        if (!_isOpen) return;
        if (IsDragging)
            _pendingRefresh = true;  // refresh safely after drag ends
        else
            RefreshGrid();
    }

    public void RefreshGrid()
    {
        // Clear old slots
        foreach (var slot in _spawnedSlots)
            if (slot != null) Destroy(slot);
        _spawnedSlots.Clear();

        if (PlayerInventory.Instance == null || itemGridContainer == null || itemSlotPrefab == null)
        {
            Debug.LogWarning("[InventoryUI] Missing references — cannot build grid.");
            return;
        }

        var items = PlayerInventory.Instance.GetAllItems();
        for (int i = 0; i < items.Count; i++)
        {
            GameObject slotGO = Instantiate(itemSlotPrefab, itemGridContainer);
            _spawnedSlots.Add(slotGO);

            DraggableItem draggable = slotGO.GetComponent<DraggableItem>();
            if (draggable != null)
                draggable.Setup(items[i], i);   // null item = empty slot visual
            else
                Debug.LogWarning("[InventoryUI] itemSlotPrefab is missing a DraggableItem component.");
        }
    }
}
