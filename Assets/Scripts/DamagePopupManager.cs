using UnityEngine;
using TMPro;

/// <summary>
/// Singleton that spawns floating damage numbers in world space.
/// Call DamagePopupManager.Instance.ShowDamage() from HealthSystem or anywhere damage is dealt.
/// </summary>
public class DamagePopupManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static DamagePopupManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Prefab")]
    [Tooltip("Drag your DamagePopup prefab here (a World Space TextMeshPro object).")]
    public GameObject popupPrefab;

    [Header("Colors")]
    [Tooltip("Color for damage dealt BY the player (outgoing).")]
    public Color dealColor = new Color(0.2f, 1f, 0.2f);   // green

    [Tooltip("Color for damage received BY the player (incoming).")]
    public Color receiveColor = new Color(1f, 0.2f, 0.2f); // red

    [Tooltip("Color for critical hits dealt by the player.")]
    public Color critColor = new Color(1f, 0.5f, 0f);      // orange

    [Tooltip("Color for HP healed (potions, coins, etc.).")]
    public Color healColor = new Color(0.4f, 1f, 0.4f);    // bright green

    [Tooltip("Color for XP gained.")]
    public Color xpColor = new Color(1f, 0.85f, 0f);       // yellow

    [Tooltip("Color for the Level Up popup.")]
    public Color levelUpColor = new Color(1f, 0.85f, 0f);  // gold

    [Header("Text Size")]
    [Tooltip("Font size for damage dealt by the player.")]
    public float dealFontSize = 6f;

    [Tooltip("Font size for damage received by the player.")]
    public float receiveFontSize = 6f;

    [Tooltip("Font size for critical hits.")]
    public float critFontSize = 9f;

    [Tooltip("Font size for HP healed.")]
    public float healFontSize = 6f;

    [Tooltip("Font size for XP gained.")]
    public float xpFontSize = 5f;

    [Tooltip("Font size for the Level Up popup.")]
    public float levelUpFontSize = 10f;

    [Header("Spawn Offset")]
    [Tooltip("Height above the damaged object's origin where the popup appears.")]
    public float heightOffset = 1.8f;

    [Header("Appearance")]
    [Tooltip("Font asset for all popups. Leave empty to use the prefab's default font.")]
    public TMP_FontAsset popupFont;

    [Tooltip("Outline colour painted on every popup.")]
    public Color outlineColor = Color.black;

    [Tooltip("Outline thickness (0 = no outline, 1 = maximum).")]
    [Range(0f, 1f)]
    public float outlineWidth = 0f;

    [Header("Timing")]
    [Tooltip("Seconds the popup stays immobile before it starts rising.")]
    public float moveDelay = 0f;

    [Tooltip("How long the popup is fully visible before it starts fading.")]
    public float holdDuration = 0.4f;

    [Tooltip("How long the fade-out takes after the hold phase.")]
    public float fadeDuration = 0.5f;

    // ─── Private State ────────────────────────────────────────────────────────

    private int _hudOverlayLayer = -1;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        _hudOverlayLayer = LayerMask.NameToLayer("HudOverlay");
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a floating damage number above the target position.
    /// </summary>
    /// <param name="worldPosition">World position of the damaged object.</param>
    /// <param name="amount">Damage value to display.</param>
    /// <param name="isPlayerReceiving">True = red (player hurt). False = green (enemy hurt).</param>
    public void ShowDamage(Vector3 worldPosition, int amount, bool isPlayerReceiving, bool isCrit = false)
    {
        if (popupPrefab == null)
        {
            Debug.LogWarning("[DamagePopupManager] No popup prefab assigned!");
            return;
        }

        Vector3 spawnPos = worldPosition + Vector3.up * heightOffset;
        GameObject obj = Instantiate(popupPrefab, spawnPos, Quaternion.identity);
        if (_hudOverlayLayer >= 0) obj.layer = _hudOverlayLayer;

        DamagePopup popup = obj.GetComponent<DamagePopup>();
        if (popup != null)
        {
            Color color = isPlayerReceiving ? receiveColor : (isCrit ? critColor : dealColor);
            float size  = isPlayerReceiving ? receiveFontSize : (isCrit ? critFontSize : dealFontSize);
            popup.Initialize(amount, color, size,
                font: popupFont, moveDelay: moveDelay, holdDuration: holdDuration, fadeDuration: fadeDuration,
                outlineColor: outlineColor, outlineWidth: outlineWidth);
        }
    }

    /// <summary>
    /// Spawns a floating "+N" heal number above the target position.
    /// </summary>
    /// <param name="worldPosition">World position of the healed object.</param>
    /// <param name="amount">HP restored to display.</param>
    public void ShowHeal(Vector3 worldPosition, int amount)
    {
        if (amount <= 0) return;

        if (popupPrefab == null)
        {
            Debug.LogWarning("[DamagePopupManager] No popup prefab assigned!");
            return;
        }

        Vector3 spawnPos = worldPosition + Vector3.up * heightOffset;
        GameObject obj = Instantiate(popupPrefab, spawnPos, Quaternion.identity);
        if (_hudOverlayLayer >= 0) obj.layer = _hudOverlayLayer;

        DamagePopup popup = obj.GetComponent<DamagePopup>();
        if (popup != null)
            popup.Initialize(amount, healColor, healFontSize,
                font: popupFont, moveDelay: moveDelay, holdDuration: holdDuration, fadeDuration: fadeDuration,
                outlineColor: outlineColor, outlineWidth: outlineWidth);
    }

    /// <summary>
    /// Spawns a floating "+N EXP" label above the target position.
    /// </summary>
    /// <param name="worldPosition">World position where the popup appears.</param>
    /// <param name="amount">XP amount to display.</param>
    public void ShowXP(Vector3 worldPosition, int amount)
    {
        if (popupPrefab == null)
        {
            Debug.LogWarning("[DamagePopupManager] No popup prefab assigned!");
            return;
        }

        Vector3 spawnPos = worldPosition + Vector3.up * heightOffset;
        GameObject obj = Instantiate(popupPrefab, spawnPos, Quaternion.identity);
        if (_hudOverlayLayer >= 0) obj.layer = _hudOverlayLayer;

        DamagePopup popup = obj.GetComponent<DamagePopup>();
        if (popup != null)
            popup.Initialize(amount, xpColor, xpFontSize, suffix: " EXP",
                font: popupFont, moveDelay: moveDelay, holdDuration: holdDuration, fadeDuration: fadeDuration,
                outlineColor: outlineColor, outlineWidth: outlineWidth);
    }

    /// <summary>
    /// Spawns a floating "LEVEL UP!" label above the target position.
    /// </summary>
    public void ShowLevelUp(Vector3 worldPosition)
    {
        if (popupPrefab == null) return;

        Vector3 spawnPos = worldPosition + Vector3.up * (heightOffset + 1f);
        GameObject obj = Instantiate(popupPrefab, spawnPos, Quaternion.identity);
        if (_hudOverlayLayer >= 0) obj.layer = _hudOverlayLayer;

        DamagePopup popup = obj.GetComponent<DamagePopup>();
        if (popup != null)
            popup.InitializeText("LEVEL UP!", levelUpColor, levelUpFontSize,
                font: popupFont, moveDelay: moveDelay, holdDuration: 1.5f, fadeDuration: 0.8f,
                outlineColor: outlineColor, outlineWidth: outlineWidth);
    }

}