using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// UI component for a single node button in the skill tree.
///
/// Attach to each node button GameObject inside the skill tree window.
/// Wire the node asset and child UI references in the Inspector.
///
/// Visual states:
///   Learned   — gold border, full opacity, checkmark visible
///   Available — normal border, full opacity, clickable
///   Locked    — greyed out, not clickable
/// </summary>
public class SkillNodeUI : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Data")]
    [Tooltip("The SkillTreeNode ScriptableObject this button represents.")]
    public SkillTreeNode node;

    [Header("UI References")]
    [Tooltip("Image that displays the spell / skill icon.")]
    public Image iconImage;

    [Tooltip("Image used as the border / background — tinted by state.")]
    public Image borderImage;

    [Tooltip("Overlay shown when the node is already learned (e.g. a checkmark icon).")]
    public GameObject learnedOverlay;

    [Tooltip("Overlay shown when the node is locked (e.g. a padlock icon).")]
    public GameObject lockedOverlay;

    [Tooltip("(Optional) TMP label showing the node's cost.")]
    public TextMeshProUGUI costLabel;

    [Tooltip("(Optional) TMP label showing current level (e.g. '2/3').")]
    public TextMeshProUGUI levelLabel;

    [Header("State Colors")]
    public Color colorLearned   = new Color(1f,   0.84f, 0f,   1f); // gold
    public Color colorAvailable = new Color(1f,   1f,    1f,   1f); // white
    public Color colorLocked    = new Color(0.35f,0.35f, 0.35f,1f); // grey

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void OnEnable()
    {
        if (SkillTreeManager.Instance != null)
            SkillTreeManager.Instance.OnTreeChanged += Refresh;

        Refresh();
    }

    void OnDisable()
    {
        if (SkillTreeManager.Instance != null)
            SkillTreeManager.Instance.OnTreeChanged -= Refresh;
    }

    // ─── Pointer Events ───────────────────────────────────────────────────────

    public void OnPointerEnter(PointerEventData _)
    {
        if (SkillTreeManager.Instance != null)
            SkillTreeManager.Instance.ShowTooltip(node);
    }

    public void OnPointerExit(PointerEventData _)
    {
        if (SkillTreeManager.Instance != null)
            SkillTreeManager.Instance.HideTooltip();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (SkillTreeManager.Instance == null) return;

        if (SkillTreeManager.Instance.LearnNode(node))
            Refresh();
    }

    // ─── Visual Refresh ───────────────────────────────────────────────────────

    /// <summary>Update all visuals to reflect the current state of this node.</summary>
    public void Refresh()
    {
        if (node == null || SkillTreeManager.Instance == null) return;

        bool learned   = SkillTreeManager.Instance.IsLearned(node);
        bool available = SkillTreeManager.Instance.CanLearn(node);
        int  lvl       = SkillTreeManager.Instance.GetNodeLevel(node);
        bool maxed     = lvl >= node.maxLevel;

        // Icon — fall back to the unlocked spell's icon if the node has no icon of its own
        if (iconImage != null)
        {
            iconImage.sprite = node.icon != null ? node.icon
                             : node.unlocksSpell  != null ? node.unlocksSpell.icon
                             : null;
            iconImage.color  = learned || available ? Color.white : colorLocked;
        }

        // Border tint — maxed nodes keep gold
        if (borderImage != null)
            borderImage.color = maxed     ? colorLearned
                              : available ? colorAvailable
                              : colorLocked;

        // Overlays
        if (learnedOverlay != null) learnedOverlay.SetActive(maxed);
        if (lockedOverlay  != null) lockedOverlay.SetActive(!learned && !available);

        // Cost label — hide when maxed
        if (costLabel != null)
            costLabel.text = maxed ? "" : $"{node.cost} pt";

        // Level label — only show for multi-level nodes
        if (levelLabel != null)
            levelLabel.text = node.maxLevel > 1 ? $"{lvl}/{node.maxLevel}" : "";
    }
}
