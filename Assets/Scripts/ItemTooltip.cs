using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Singleton tooltip panel that follows the cursor and displays item info on hover.
/// Place the TooltipPanel as a direct child of the root Canvas so it always renders on top.
/// </summary>
public class ItemTooltip : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static ItemTooltip Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Panel")]
    [Tooltip("The tooltip panel root to show/hide.")]
    public GameObject tooltipPanel;

    [Header("Labels")]
    public TextMeshProUGUI titleLabel;
    public TextMeshProUGUI rarityLabel;
    public TextMeshProUGUI statsLabel;

    [Header("Offset from slot center (pixels)")]
    [Tooltip("Offset from the slot centre so the tooltip doesn't cover the icon.")]
    public Vector2 slotOffset = new(90f, 0f);

    // ─── Private ──────────────────────────────────────────────────────────────

    private RectTransform _panelRect;
    private RectTransform _canvasRect;
    private Canvas        _canvas;
    private CanvasGroup   _canvasGroup;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        _canvas     = GetComponentInParent<Canvas>();
        _panelRect  = tooltipPanel.GetComponent<RectTransform>();
        _canvasRect = _canvas.transform as RectTransform;

        // Never block raycasts — prevents tooltip overlapping slot from causing flicker
        _canvasGroup = tooltipPanel.GetComponent<CanvasGroup>();
        if (_canvasGroup == null) _canvasGroup = tooltipPanel.AddComponent<CanvasGroup>();
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable   = false;

        Hide();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public void Show(ItemData item, RectTransform anchor)
    {
        if (item == null) return;

        tooltipPanel.SetActive(true);
        tooltipPanel.transform.SetAsLastSibling();

        if (titleLabel != null)
            titleLabel.text = $"<color=#{item.RarityHex}>{item.itemName}</color>";

        if (rarityLabel != null)
        {
            rarityLabel.text  = item.rarity.ToString();
            rarityLabel.color = item.RarityColor;
        }

        if (statsLabel != null)
            statsLabel.text = item.BuildStatSummary();

        // Hide while repositioning to avoid one-frame flicker
        _canvasGroup.alpha = 0f;
        StartCoroutine(PositionNextTo(anchor));
    }

    public void Hide()
    {
        if (tooltipPanel != null)
        {
            StopAllCoroutines();
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
            tooltipPanel.SetActive(false);
        }
    }

    // ─── Positioning ──────────────────────────────────────────────────────────

    private System.Collections.IEnumerator PositionNextTo(RectTransform anchor)
    {
        // Wait one frame so Content Size Fitter updates the panel size
        yield return null;

        // Convert slot centre (world space) to canvas local space
        Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(null, anchor.position);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect,
            screenCenter,
            _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera,
            out Vector2 localPos);

        // Offset to the right of the slot so the icon stays visible
        localPos += slotOffset;

        // Clamp so tooltip doesn't go off screen
        Vector2 panelSize  = _panelRect.sizeDelta;
        Vector2 canvasSize = _canvasRect.sizeDelta;

        // If tooltip goes off right edge, flip it to the left of the slot
        if (localPos.x + panelSize.x * 0.5f > canvasSize.x * 0.5f)
            localPos.x -= panelSize.x + slotOffset.x * 2f;

        // If tooltip goes off bottom edge, push it up
        if (localPos.y - panelSize.y * 0.5f < -canvasSize.y * 0.5f)
            localPos.y += panelSize.y;

        _panelRect.localPosition = new Vector3(localPos.x, localPos.y, 0f);

        // Reveal after positioning
        _canvasGroup.alpha = 1f;
    }
}
