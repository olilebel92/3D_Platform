using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SpellBarManager : MonoBehaviour
{
    // ─── Singleton ───────────────────────────────────────────────────────────
    public static SpellBarManager Instance { get; private set; }

    // ─── Inspector ───────────────────────────────────────────────────────────
    [Header("Slot Setup")]
    [Tooltip("Drag your 10 SpellSlot GameObjects here (in order, 0-9).")]
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

    // ─── New Input System key mapping ─────────────────────────────────────────
    private static readonly Key[] s_hotkeys =
    {
        Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
        Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0
    };

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        InitializeSlots();
        PopulateStartingSpells();

        if (slots.Count > 0)
            SelectSlot(0);

        HideSpellBar();
    }

    private void Update()
    {
        if (enableHotkeys)
            HandleHotkeyInput();
    }

    // ─── Initialization ───────────────────────────────────────────────────────
    private void InitializeSlots()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] != null)
                slots[i].Initialize(i);
        }
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
        if (Keyboard.current == null) return;

        for (int i = 0; i < s_hotkeys.Length && i < slots.Count; i++)
        {
            if (Keyboard.current[s_hotkeys[i]].wasPressedThisFrame)
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