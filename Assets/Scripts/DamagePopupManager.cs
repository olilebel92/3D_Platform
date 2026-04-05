using UnityEngine;

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

    [Header("Spawn Offset")]
    [Tooltip("Height above the damaged object's origin where the popup appears.")]
    public float heightOffset = 1.8f;

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

        DamagePopup popup = obj.GetComponent<DamagePopup>();
        if (popup != null)
        {
            Color color = isPlayerReceiving ? receiveColor : (isCrit ? critColor : dealColor);
            popup.Initialize(amount, color);
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

        DamagePopup popup = obj.GetComponent<DamagePopup>();
        if (popup != null)
            popup.Initialize(amount, healColor, "+");
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

        DamagePopup popup = obj.GetComponent<DamagePopup>();
        if (popup != null)
            popup.Initialize(amount, xpColor, "+", " EXP");
    }
}