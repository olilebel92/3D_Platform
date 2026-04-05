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

    /// <summary>Available skill points the player has not yet spent.</summary>
    public int SkillPoints { get; private set; } = 0;

    /// <summary>Sum of all spellDamageBonus values from learned nodes.</summary>
    public float TotalSpellDamageBonus { get; private set; } = 0f;

    /// <summary>Sum of all fireDamageBonus values from learned nodes.</summary>
    public float TotalFireDamageBonus { get; private set; } = 0f;

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fired after any node is learned or a skill point is added.</summary>
    public event System.Action OnTreeChanged;

    // ─── Private State ────────────────────────────────────────────────────────

    private readonly HashSet<SkillTreeNode> _learned = new HashSet<SkillTreeNode>();
    private bool _windowOpen = false;
    private CharacterWindow _characterWindow;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        _characterWindow = FindFirstObjectByType<CharacterWindow>();
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

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && _windowOpen)
            CloseWindow();
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

    /// <summary>Returns true if the player meets the prerequisites and has enough points.</summary>
    public bool CanLearn(SkillTreeNode node)
    {
        if (node == null) return false;
        if (_learned.Contains(node)) return false;
        if (SkillPoints < node.cost) return false;

        foreach (var req in node.prerequisites)
            if (!_learned.Contains(req)) return false;

        return true;
    }

    /// <summary>Returns true if this node has already been learned.</summary>
    public bool IsLearned(SkillTreeNode node) => node != null && _learned.Contains(node);

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
        _learned.Add(node);
        TotalSpellDamageBonus += node.spellDamageBonus;
        TotalFireDamageBonus  += node.fireDamageBonus;
        Debug.Log($"[SkillTree] Learned '{node.nodeName}'. Remaining points: {SkillPoints}. Spell damage bonus: +{TotalSpellDamageBonus}");

        // If this node unlocks a spell, add it to the first empty slot
        if (node.unlocksSpell != null)
            AddSpellToBar(node.unlocksSpell);

        RefreshUI();
        OnTreeChanged?.Invoke();
        return true;
    }

    /// <summary>Show a tooltip for the hovered node.</summary>
    public void ShowTooltip(SkillTreeNode node)
    {
        if (node == null || tooltipPanel == null) return;

        tooltipPanel.SetActive(true);

        if (tooltipName != null)
            tooltipName.text = node.nodeName;

        if (tooltipDesc != null)
        {
            string status = IsLearned(node) ? " <color=#00FF88>[Learned]</color>"
                          : CanLearn(node)  ? $" <color=#FFD700>[Cost: {node.cost} pt]</color>"
                          :                   $" <color=#FF6666>[Locked]</color>";
            tooltipDesc.text = node.description + status;
        }
    }

    /// <summary>Hide the tooltip panel.</summary>
    public void HideTooltip()
    {
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

        // Close the character window if we're opening this one
        if (open && _characterWindow != null)
            _characterWindow.CloseWindow();

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
    }
}
