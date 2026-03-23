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
    public void ShowDamage(Vector3 worldPosition, int amount, bool isPlayerReceiving)
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
            popup.Initialize(amount, isPlayerReceiving ? receiveColor : dealColor);
    }
}