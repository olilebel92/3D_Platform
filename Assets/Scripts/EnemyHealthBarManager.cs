using UnityEngine;

/// <summary>
/// Scene-level singleton. Holds the shared health bar prefab and instantiates
/// one per enemy automatically when EnemyAI.Start() calls AttachTo().
/// Place on any persistent manager GameObject in the scene.
/// </summary>
public class EnemyHealthBarManager : MonoBehaviour
{
    public static EnemyHealthBarManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("Prefab with a child Canvas (World Space) and EnemyHealthBar on the root.")]
    [SerializeField] private GameObject _healthBarPrefab;

    [Header("Settings")]
    [Tooltip("Extra world-space units above the top of the enemy's mesh bounds.")]
    [SerializeField] private float _yPadding = 0.2f;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    // Returns the local-space Y offset to place the bar just above the enemy's tallest renderer.
    private float ComputeTopY(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return _yPadding;

        Bounds combined = renderers[0].bounds;
        foreach (Renderer r in renderers)
            combined.Encapsulate(r.bounds);

        // Convert world-space top to local Y (scale is already counteracted on the bar).
        float worldTop = combined.max.y - root.position.y;
        return worldTop + _yPadding;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public void AttachTo(EnemyAI ai, HealthSystem health, Transform root)
    {
        if (_healthBarPrefab == null)
        {
            Debug.LogWarning("[EnemyHealthBarManager] _healthBarPrefab is not assigned.");
            return;
        }

        // Compute height before instantiating so the bar's own children don't affect the search.
        Vector3 ps     = root.lossyScale;
        float   worldY = ComputeTopY(root);

        GameObject bar = Instantiate(_healthBarPrefab, root);

        // Counter-act parent scale so the bar renders at consistent world-space size.
        // localPosition is multiplied by parent scale, so divide worldY by ps.y to get the right offset.
        bar.transform.localScale    = new Vector3(1f / ps.x, 1f / ps.y, 1f / ps.z);
        bar.transform.localPosition = new Vector3(0f, worldY / ps.y, 0f);
        bar.transform.localRotation = Quaternion.identity;

        EnemyHealthBar hb = bar.GetComponent<EnemyHealthBar>();
        if (hb != null)
            hb.Initialize(ai, health);
        else
            Debug.LogWarning("[EnemyHealthBarManager] Prefab has no EnemyHealthBar component.");
    }
}
