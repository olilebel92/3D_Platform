using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach to a section root to make it collapsible via a header button.
/// The arrow label automatically shows ▶ (closed) or ▼ (open).
/// </summary>
public class CollapsibleSection : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("The Button the player clicks to expand/collapse the section.")]
    public Button headerButton;

    [Tooltip("The GameObject that contains all child stats to show/hide.")]
    public GameObject content;

    [Tooltip("TMP label used as the arrow indicator (▶ / ▼).")]
    public TextMeshProUGUI arrowLabel;

    [Header("Settings")]
    [Tooltip("Whether the section starts open or closed.")]
    public bool startOpen = true;

    // ─── Private State ────────────────────────────────────────────────────────

    private bool _isOpen;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        _isOpen = startOpen;

        if (headerButton != null)
            headerButton.onClick.AddListener(Toggle);

        Refresh();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public void Toggle()
    {
        _isOpen = !_isOpen;
        Refresh();
    }

    public void Open()
    {
        _isOpen = true;
        Refresh();
    }

    public void Close()
    {
        _isOpen = false;
        Refresh();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void Refresh()
    {
        if (content != null)
            content.SetActive(_isOpen);

        if (arrowLabel != null)
            arrowLabel.text = _isOpen ? "v" : ">";
    }
}
