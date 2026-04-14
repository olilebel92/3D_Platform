using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Singleton that manages skill points, learned nodes, and the skill tree window.
///
/// Inspector wiring:
///   - skillTreeWindow   → root Panel of the skill tree UI
///   - skillPointsText   → TMP label showing "Skill Points: N"
///   - tooltipPanel      → small panel that shows node name + description
///   - tooltipName       → TMP label inside tooltip
///   - tooltipDesc       → TMP label inside tooltip
///
/// Press K (or the configured key) to open / close the window.
/// </summary>
public class SkillTreeManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static SkillTreeManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Window")]
    [Tooltip("Root panel of the entire skill tree UI.")]
    public GameObject skillTreeWindow;

    [Tooltip("Key that toggles the skill tree window open / closed.")]
    public Key toggleKey = Key.K;

    [Tooltip("Pause the game (Time.timeScale = 0) while the skill tree is open.")]
    public bool pauseGameWhileOpen = true;

    [Header("Starting Points")]
    [Tooltip("Skill points granted to the player at the start of the game.")]
    public int startingSkillPoints = 0;

    [Header("HUD")]
    [Tooltip("TMP label showing available skill points (e.g. on HUD or in window).")]
    public TextMeshProUGUI skillPointsText;

    [Header("Tooltip")]
    [Tooltip("Panel that appears when hovering over a node.")]
    public GameObject tooltipPanel;

    [Tooltip("TMP label for the hovered node's name.")]
    public TextMeshProUGUI tooltipName;

    [Tooltip("TMP label for the hovered node's description.")]
    public TextMeshProUGUI tooltipDesc;

    // ─── Public State ─────────────────────────────────────────────────────────

    /// <summary>True while the skill tree window is visible.</summary>
    public bool IsOpen => _windowOpen;

    /// <summary>Available skill points the player has not yet spent.</summary>
    public int SkillPoints { get; private set; } = 0;

    /// <summary>Sum of all flat STR bonuses from learned nodes.</summary>
    public int TotalStrBonus { get; private set; } = 0;

    /// <summary>Sum of all flat AGI bonuses from learned nodes.</summary>
    public int TotalAgiBonus { get; private set; } = 0;

    /// <summary>Sum of all flat INT bonuses from learned nodes.</summary>
    public int TotalIntBonus { get; private set; } = 0;

    /// <summary>Sum of all spellDamageBonus values from learned nodes.</summary>
    public float TotalSpellDamageBonus { get; private set; } = 0f;

    /// <summary>Sum of all fireDamageBonus values from learned nodes.</summary>
    public float TotalFireDamageBonus { get; private set; } = 0f;

    /// <summary>Sum of all percent spell damage bonuses from learned nodes (0.10 = +10%).</summary>
    public float TotalSpellDamagePctBonus { get; private set; } = 0f;

    /// <summary>Sum of all percent fire damage bonuses from learned nodes (0.10 = +10%).</summary>
    public float TotalFireDamagePctBonus { get; private set; } = 0f;

    /// <summary>Sum of all flat heal bonuses from learned nodes.</summary>
    public float TotalHealBonus { get; private set; } = 0f;

    /// <summary>Sum of all percent heal bonuses from learned nodes (0.10 = +10%).</summary>
    public float TotalHealPctBonus { get; private set; } = 0f;

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fired after any node is learned or a skill point is added.</summary>
    public event System.Action OnTreeChanged;

    // ─── Private State ────────────────────────────────────────────────────────

    private readonly Dictionary<SkillTreeNode, int> _nodeLevels = new Dictionary<SkillTreeNode, int>();
    private bool _windowOpen = false;
    private CharacterWindow _characterWindow;
    private InventoryUI _inventoryUI;
    private SkillTreeNode _hoveredNode;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Hide immediately in Awake so the panel never flashes on the first frame.
        if (skillTreeWindow != null)
            skillTreeWindow.SetActive(false);
    }

    void Start()
    {
        _characterWindow = FindFirstObjectByType<CharacterWindow>();
        _inventoryUI = FindFirstObjectByType<InventoryUI>();
        HideTooltip();
        SetWindowOpen(false, force: true);

        if (startingSkillPoints > 0)
        {
            SkillPoints += startingSkillPoints;
            Debug.Log($"[SkillTree] Granted {startingSkillPoints} starting skill point(s).");
        }

        RefreshUI();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            SetWindowOpen(!_windowOpen);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Grant one skill point (call this from ExperienceManager on level-up).</summary>
    public void AddSkillPoint()
    {
        SkillPoints++;
        Debug.Log($"[SkillTree] Skill point awarded. Total: {SkillPoints}");
        RefreshUI();
        OnTreeChanged?.Invoke();
    }

    /// <summary>Returns the current level of a node (0 = not learned).</summary>
    public int GetNodeLevel(SkillTreeNode node) =>
        node != null && _nodeLevels.TryGetValue(node, out int lvl) ? lvl : 0;

    /// <summary>Returns true if the player meets the prerequisites and can gain another level.</summary>
    public bool CanLearn(SkillTreeNode node)
    {
        if (node == null) return false;
        if (GetNodeLevel(node) >= node.maxLevel) return false;
        if (SkillPoints < node.cost) return false;

        foreach (var req in node.prerequisites)
            if (GetNodeLevel(req) < 1) return false;

        return true;
    }

    /// <summary>Returns true if this node has been learned at least once.</summary>
    public bool IsLearned(SkillTreeNode node) => GetNodeLevel(node) > 0;

    /// <summary>
    /// Attempt to learn a node. Returns true on success.
    /// Called by SkillNodeUI when the player clicks a node button.
    /// </summary>
    public bool LearnNode(SkillTreeNode node)
    {
        if (!CanLearn(node))
        {
            Debug.Log($"[SkillTree] Cannot learn '{node.nodeName}' — requirements not met.");
            return false;
        }

        SkillPoints -= node.cost;

        _nodeLevels.TryGetValue(node, out int currentLevel);
        int newLevel = currentLevel + 1;
        _nodeLevels[node] = newLevel;

        // Bonus delta per level = baseStat * scalingFactor
        TotalStrBonus             += Mathf.RoundToInt(node.strBonus           * node.scalingFactor);
        TotalAgiBonus             += Mathf.RoundToInt(node.agiBonus           * node.scalingFactor);
        TotalIntBonus             += Mathf.RoundToInt(node.intBonus           * node.scalingFactor);
        TotalSpellDamageBonus     += node.spellDamageBonus    * node.scalingFactor;
        TotalFireDamageBonus      += node.fireDamageBonus     * node.scalingFactor;
        TotalSpellDamagePctBonus  += node.spellDamagePctBonus * node.scalingFactor;
        TotalFireDamagePctBonus   += node.fireDamagePctBonus  * node.scalingFactor;
        TotalHealBonus            += node.healBonus           * node.scalingFactor;
        TotalHealPctBonus         += node.healPctBonus        * node.scalingFactor;

        Debug.Log($"[SkillTree] '{node.nodeName}' leveled to {newLevel}/{node.maxLevel}. " +
                  $"Points left: {SkillPoints}. Spell bonus total: +{TotalSpellDamageBonus}");

        // Unlock spell only on first learn
        if (newLevel == 1 && node.unlocksSpell != null)
            AddSpellToBar(node.unlocksSpell);

        RefreshUI();
        OnTreeChanged?.Invoke();
        return true;
    }

    /// <summary>Show a tooltip for the hovered node.</summary>
    public void ShowTooltip(SkillTreeNode node)
    {
        if (node == null || tooltipPanel == null) return;

        _hoveredNode = node;
        tooltipPanel.SetActive(true);

        if (tooltipName != null)
            tooltipName.text = node.nodeName;

        if (tooltipDesc != null)
        {
            int lvl    = GetNodeLevel(node);
            bool maxed = lvl >= node.maxLevel;

            string levelInfo = node.maxLevel > 1
                ? $" <color=#AAAAFF>[Lv {lvl}/{node.maxLevel}]</color>"
                : "";

            string status = maxed          ? $" <color=#00FF88>[Maxed]</color>"
                          : CanLearn(node) ? $" <color=#FFD700>[Cost: {node.cost} pt]</color>"
                          : lvl > 0        ? $" <color=#FFD700>[Cost: {node.cost} pt — Locked]</color>"
                          :                  $" <color=#FF6666>[Locked]</color>";

            tooltipDesc.text = node.description + levelInfo + status;
        }
    }

    /// <summary>Hide the tooltip panel.</summary>
    public void HideTooltip()
    {
        _hoveredNode = null;
        if (tooltipPanel != null)
            tooltipPanel.SetActive(false);
    }

    // ─── Window Toggle ────────────────────────────────────────────────────────

    /// <summary>Closes the skill tree window externally (e.g. when another panel opens).</summary>
    public void CloseWindow()
    {
        SetWindowOpen(false);
    }

    private void SetWindowOpen(bool open, bool force = false)
    {
        if (!force && _windowOpen == open) return;
        _windowOpen = open;

        // Dismiss any active tutorial popup when opening this panel
        if (open && PopupManager.IsShowing)
            PopupManager.Instance?.Hide();

        // Close other panels if we're opening this one
        if (open && _characterWindow != null)
            _characterWindow.CloseWindow();

        if (open && _inventoryUI != null)
            _inventoryUI.CloseInventory();

        if (skillTreeWindow != null)
            skillTreeWindow.SetActive(open);

        if (open)
        {
            if (pauseGameWhileOpen) PauseManager.RequestPause();
        }
        else
        {
            if (pauseGameWhileOpen) PauseManager.ReleasePause();
        }

        // Lock / unlock camera rotation
        CameraControllerThirdPerson.IsLocked = open;

        Cursor.lockState = open ? CursorLockMode.None  : CursorLockMode.Locked;
        Cursor.visible   = open;

        if (!open) HideTooltip();

        Debug.Log($"[SkillTree] Window {(open ? "opened" : "closed")}.");
    }

    // ─── Spell Bar Integration ────────────────────────────────────────────────

    private void AddSpellToBar(SpellData spell)
    {
        if (SpellBarManager.Instance == null)
        {
            Debug.LogWarning("[SkillTree] SpellBarManager not found — cannot add spell.");
            return;
        }

        // Find the first empty slot
        for (int i = 0; i < SpellBarManager.Instance.slots.Count; i++)
        {
            if (SpellBarManager.Instance.slots[i] != null &&
                SpellBarManager.Instance.slots[i].CurrentSpell == null)
            {
                SpellBarManager.Instance.SetSpell(i, spell);
                SpellBarManager.Instance.ShowSpellBar();
                Debug.Log($"[SkillTree] '{spell.spellName}' added to spell bar slot {i}.");
                return;
            }
        }

        Debug.LogWarning($"[SkillTree] No empty spell bar slot found for '{spell.spellName}'.");
    }

    // ─── UI Refresh ───────────────────────────────────────────────────────────

    private void RefreshUI()
    {
        if (skillPointsText != null)
        {
            skillPointsText.gameObject.SetActive(SkillPoints > 0);
            skillPointsText.text = $"Skill Points: {SkillPoints}";
        }

        // Re-render the tooltip if the player is hovering a node while state changes
        if (_hoveredNode != null)
            ShowTooltip(_hoveredNode);
    }
}
