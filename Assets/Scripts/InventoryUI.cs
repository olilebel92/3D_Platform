using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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

    [Header("Cursor")]
    public bool unlockCursorWhileOpen = true;

    [Header("Pause")]
    [Tooltip("Pause the game (Time.timeScale = 0) while the inventory is open.")]
    public bool pauseGameWhileOpen = true;

    // ─── Private State ────────────────────────────────────────────────────────

    public static bool IsDragging { get; set; }

    public bool IsOpen => _isOpen;

    private bool _isOpen = false;
    private bool _pendingRefresh = false;
    private readonly List<GameObject> _spawnedSlots = new();
    private CharacterWindow _characterWindow;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);

        _characterWindow = FindFirstObjectByType<CharacterWindow>();

        if (PlayerInventory.Instance != null)
            PlayerInventory.Instance.OnInventoryChanged += OnInventoryChanged;
    }

    void OnDestroy()
    {
        if (PlayerInventory.Instance != null)
            PlayerInventory.Instance.OnInventoryChanged -= OnInventoryChanged;
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

    // ─── Toggle ───────────────────────────────────────────────────────────────

    private void ToggleInventory()
    {
        _isOpen = !_isOpen;

        if (inventoryPanel != null)
            inventoryPanel.SetActive(_isOpen);

        if (_isOpen)
        {
            RefreshGrid();
            if (pauseGameWhileOpen) PauseManager.RequestPause();
        }
        else
        {
            ItemTooltip.Instance?.Hide();
            if (pauseGameWhileOpen) PauseManager.ReleasePause();
        }

        bool anyPanelOpen = _isOpen || (_characterWindow != null && _characterWindow.IsOpen);
        CameraControllerThirdPerson.IsLocked = anyPanelOpen;

        if (unlockCursorWhileOpen)
        {
            Cursor.lockState = anyPanelOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible   = anyPanelOpen;
        }

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
        if (pauseGameWhileOpen) PauseManager.ReleasePause();

        bool anyPanelOpen = _characterWindow != null && _characterWindow.IsOpen;
        CameraControllerThirdPerson.IsLocked = anyPanelOpen;

        if (unlockCursorWhileOpen)
        {
            Cursor.lockState = anyPanelOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible   = anyPanelOpen;
        }
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
