using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;          // InputAction.WasPressedThisFrame() extension

public class SpellBarManager : MonoBehaviour
{
    // ─── Singleton ───────────────────────────────────────────────────────────
    public static SpellBarManager Instance { get; private set; }

    // ─── Inspector ───────────────────────────────────────────────────────────
    // Populated automatically from children at runtime — no manual wiring needed.
    [HideInInspector]
    public List<SpellSlot> slots = new List<SpellSlot>();

    [Header("Spell Bar Panel")]
    [Tooltip("Drag the parent panel GameObject that contains all spell slots here.")]
    public GameObject spellBarPanel;

    [Header("Starting Spells (optional)")]
    [Tooltip("Assign SpellData assets here to pre-populate slots at startup.")]
    public List<SpellData> startingSpells = new List<SpellData>();

    [Header("Keyboard Selection")]
    [Tooltip("Allow keys 1–0 to select slots.")]
    public bool enableHotkeys = true;

    // ─── Private State ────────────────────────────────────────────────────────
    private int _selectedIndex = -1;

    // Hotkey bindings (keyboard 1..9, 0 for slot 10) live in
    // PlayerInputActions.inputactions under UI.Spell1..Spell10.

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) return;
        Instance = this;
    }

    private void Start()
    {
        InitializeSlots();
        PopulateStartingSpells();

        if (slots.Count > 0)
            SelectSlot(0);

        Debug.Log($"[SpellBarManager] Initialized with {slots.Count} slots.");
    }

    private void Update()
    {
        if (enableHotkeys)
            HandleHotkeyInput();
    }

    // ─── Initialization ───────────────────────────────────────────────────────
    private void InitializeSlots()
    {
        slots.Clear();
        slots.AddRange(GetComponentsInChildren<SpellSlot>());
        for (int i = 0; i < slots.Count; i++)
            slots[i].Initialize(i);
    }

    private void PopulateStartingSpells()
    {
        for (int i = 0; i < startingSpells.Count && i < slots.Count; i++)
        {
            if (slots[i] != null)
                slots[i].SetSpell(startingSpells[i]);
        }
    }

    // ─── Show / Hide ──────────────────────────────────────────────────────────

    /// <summary>
    /// Hides the entire spell bar. If a panel is assigned, hides that;
    /// otherwise falls back to hiding each slot individually.
    /// </summary>
    public void HideSpellBar()
    {
        if (spellBarPanel != null)
        {
            spellBarPanel.SetActive(false);
            return;
        }

        // Fallback: hide every slot individually
        foreach (SpellSlot slot in slots)
            if (slot != null)
                slot.gameObject.SetActive(false);

        Debug.Log("[SpellBarManager] Spell bar hidden.");
    }

    /// <summary>
    /// Shows the entire spell bar.
    /// </summary>
    public void ShowSpellBar()
    {
        if (spellBarPanel != null)
        {
            spellBarPanel.SetActive(true);
            return;
        }

        // Fallback: show every slot individually
        foreach (SpellSlot slot in slots)
            if (slot != null)
                slot.gameObject.SetActive(true);

        Debug.Log("[SpellBarManager] Spell bar shown.");
    }

    // ─── Public API ───────────────────────────────────────────────────────────
    public void SelectSlot(int index)
    {
        if (!IsValidIndex(index)) return;

        if (_selectedIndex >= 0 && _selectedIndex < slots.Count)
            slots[_selectedIndex].SetHighlight(false);

        _selectedIndex = index;
        slots[_selectedIndex].SetHighlight(true);

        Debug.Log($"[SpellBar] Selected slot {index} — " +
                  $"Spell: {slots[index].CurrentSpell?.spellName ?? "empty"}");
    }

    public SpellData GetSelectedSpell()
    {
        if (!IsValidIndex(_selectedIndex)) return null;
        return slots[_selectedIndex].CurrentSpell;
    }

    public SpellData GetSpellAt(int index)
    {
        if (!IsValidIndex(index)) return null;
        return slots[index].CurrentSpell;
    }

    public void SetSpell(int index, SpellData spell)
    {
        if (!IsValidIndex(index)) return;
        slots[index].SetSpell(spell);
    }

    public void ClearSlot(int index)
    {
        if (!IsValidIndex(index)) return;
        slots[index].ClearSpell();
    }

    public void SwapSlots(int indexA, int indexB)
    {
        if (!IsValidIndex(indexA) || !IsValidIndex(indexB)) return;
        if (indexA == indexB) return;

        SpellData temp = slots[indexA].CurrentSpell;
        slots[indexA].SetSpell(slots[indexB].CurrentSpell);
        slots[indexB].SetSpell(temp);

        Debug.Log($"[SpellBar] Swapped slot {indexA} ↔ slot {indexB}");
    }

    // ─── Hotkey Input ─────────────────────────────────────────────────────────
    private void HandleHotkeyInput()
    {
        var spells = InputManager.UI.Spell;
        if (spells == null) return;

        int count = Mathf.Min(spells.Length, slots.Count);
        for (int i = 0; i < count; i++)
        {
            if (spells[i].WasPressedThisFrame())
            {
                SelectSlot(i);
                break;
            }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────
    private bool IsValidIndex(int index) =>
        index >= 0 && index < slots.Count && slots[index] != null;
}