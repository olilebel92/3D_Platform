using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Draws prerequisite connector lines between skill tree node buttons.
/// Each line shows the required level above its midpoint.
///
/// Setup:
///   1. Create an empty child GameObject inside the skill tree window panel.
///   2. Set its RectTransform to stretch-fill the window (anchors 0,0 → 1,1).
///   3. Attach this component to it.
///   4. Make sure it sits BELOW the node buttons in the hierarchy so lines
///      render behind them.
///
/// Line colours:
///   Gold  — prerequisite node is already learned at the required level.
///   Grey  — prerequisite not yet met.
/// </summary>
public class SkillTreeConnectorUI : MonoBehaviour
{
    [Header("Line Style")]
    [Tooltip("Thickness of each connector line in UI pixels.")]
    [SerializeField] private float lineThickness = 3f;

    [Tooltip("Line colour when the prerequisite has been met.")]
    [SerializeField] private Color colorMet   = new Color(1f,   0.84f, 0f,   0.8f);

    [Tooltip("Line colour when the prerequisite has not yet been met.")]
    [SerializeField] private Color colorUnmet = new Color(0.4f, 0.4f, 0.4f, 0.6f);

    [Header("Level Label")]
    [Tooltip("Font size of the required-level label drawn above each line.")]
    [SerializeField] private float labelFontSize = 12f;

    [Tooltip("How many UI pixels above the line midpoint the label is offset.")]
    [SerializeField] private float labelOffset = 10f;

    [Tooltip("Label colour when the prerequisite has been met.")]
    [SerializeField] private Color labelColorMet   = new Color(1f,   0.84f, 0f,   1f);

    [Tooltip("Label colour when the prerequisite has not yet been met.")]
    [SerializeField] private Color labelColorUnmet = new Color(0.8f, 0.8f, 0.8f, 1f);

    // ─── Runtime ──────────────────────────────────────────────────────────────

    private readonly Dictionary<SkillTreeNode, RectTransform> _nodeRects = new();

    private struct Connector
    {
        public SkillTreeNode prereq;
        public int           requiredLevel;
        public Image         line;
        public TextMeshProUGUI label;
    }
    private readonly List<Connector> _connectors = new();

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    void OnEnable()
    {
        StartCoroutine(BuildLinesAfterLayout());
        if (SkillTreeManager.Instance != null)
            SkillTreeManager.Instance.OnTreeChanged += RefreshColors;
    }

    // Wait one frame so Unity's Layout Groups finish positioning nodes before
    // we read their RectTransform positions.
    IEnumerator BuildLinesAfterLayout()
    {
        yield return null;
        BuildLines();
    }

    void OnDisable()
    {
        if (SkillTreeManager.Instance != null)
            SkillTreeManager.Instance.OnTreeChanged -= RefreshColors;
    }

    // ─── Build ────────────────────────────────────────────────────────────────

    void BuildLines()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);
        _connectors.Clear();
        _nodeRects.Clear();

        // Search from the skill tree window root so nodes in all tier groups are found,
        // regardless of where this Connectors panel sits in the hierarchy.
        Transform searchRoot = SkillTreeManager.Instance?.skillTreeWindow?.transform ?? transform.parent;
        SkillNodeUI[] allNodes = searchRoot.GetComponentsInChildren<SkillNodeUI>(true);

        foreach (SkillNodeUI ui in allNodes)
            if (ui.node != null)
                _nodeRects[ui.node] = ui.GetComponent<RectTransform>();

        foreach (SkillNodeUI ui in allNodes)
        {
            if (ui.node == null) continue;

            RectTransform fromRect = ui.GetComponent<RectTransform>();

            foreach (NodePrerequisite prereq in ui.node.prerequisites)
            {
                if (prereq.node == null) continue;
                if (!_nodeRects.TryGetValue(prereq.node, out RectTransform toRect)) continue;

                Vector2 a = LocalCenter(fromRect);
                Vector2 b = LocalCenter(toRect);

                Image          line  = SpawnLine(a, b);
                TextMeshProUGUI lbl  = SpawnLabel(a, b, prereq.requiredLevel);

                _connectors.Add(new Connector
                {
                    prereq        = prereq.node,
                    requiredLevel = prereq.requiredLevel,
                    line          = line,
                    label         = lbl,
                });
            }
        }

        RefreshColors();
    }

    // ─── Positioning ──────────────────────────────────────────────────────────

    Image SpawnLine(Vector2 a, Vector2 b)
    {
        var go = new GameObject("Connector", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);

        Vector2 dir = b - a;
        rt.anchoredPosition = (a + b) * 0.5f;
        rt.sizeDelta        = new Vector2(dir.magnitude, lineThickness);
        rt.localRotation    = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);

        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    TextMeshProUGUI SpawnLabel(Vector2 a, Vector2 b, int requiredLevel)
    {
        var go = new GameObject("ConnectorLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(60f, 20f);

        // Perpendicular-up offset so the label floats above the line.
        Vector2 dir  = (b - a).normalized;
        Vector2 perp = new Vector2(-dir.y, dir.x); // rotate 90°
        // Always push toward screen-up regardless of line angle.
        if (perp.y < 0f) perp = -perp;
        rt.anchoredPosition = (a + b) * 0.5f + perp * labelOffset;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text          = $"Lv {requiredLevel}";
        tmp.fontSize      = labelFontSize;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    // Converts a node button's world center into this panel's local 2-D space.
    Vector2 LocalCenter(RectTransform rt)
    {
        Vector3 world = rt.TransformPoint(rt.rect.center);
        return ((RectTransform)transform).InverseTransformPoint(world);
    }

    // ─── Colors ───────────────────────────────────────────────────────────────

    void RefreshColors()
    {
        if (SkillTreeManager.Instance == null) return;

        foreach (Connector c in _connectors)
        {
            bool met      = SkillTreeManager.Instance.GetNodeLevel(c.prereq) >= c.requiredLevel;
            c.line.color  = met ? colorMet        : colorUnmet;
            c.label.color = met ? labelColorMet   : labelColorUnmet;
        }
    }
}
