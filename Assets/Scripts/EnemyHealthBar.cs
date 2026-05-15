using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space HP bar displayed above an enemy when it takes damage.
/// Hidden by default; shows on damage, auto-hides after a delay.
///
/// This component lives on the HealthBar prefab (not the enemy prefab).
/// EnemyHealthBarManager instantiates it as a child of each enemy and
/// calls Initialize() — no per-enemy prefab setup required.
///
/// Networked mode  : reads EnemyAI.NetworkHealth (NetworkVariable) — synced
///                   to all clients automatically when the server updates it.
/// Non-networked   : polls HealthSystem.currentHealth each frame so the bar
///                   works when testing outside the main menu (no NGO host).
///
/// Prefab structure:
///   HealthBarPrefab_Root  ← EnemyHealthBar lives here
///   └── HealthBarCanvas   ← World Space Canvas — assign to _barRoot
///       ├── Background    ← Image
///       └── Fill          ← Image (Type=Filled, Method=Horizontal) — assign to _fillImage
/// </summary>
public class EnemyHealthBar : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Root GameObject containing the health bar Canvas. Shown/hidden on damage events.")]
    [SerializeField] private GameObject _barRoot;

    [Tooltip("Fill Image for the HP bar. Image Type must be Filled, Fill Method Horizontal.")]
    [SerializeField] private Image _fillImage;

    [Header("Settings")]
    [Tooltip("Seconds before the bar hides when the enemy is at full HP.")]
    [SerializeField] private float _hideDelayFull    = 3f;

    [Tooltip("Seconds before the bar hides when the enemy is below full HP.")]
    [SerializeField] private float _hideDelayDamaged = 10f;

    // ─── Private State ────────────────────────────────────────────────────────

    private EnemyAI      _ai;
    private HealthSystem _health;
    private Camera       _cam;
    private float        _hideTimer;
    private bool         _networked;
    private float        _lastHealth;
    private Canvas       _canvas;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        if (_barRoot != null) _barRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (_ai != null)
            _ai.NetworkHealth.OnValueChanged -= OnNetworkHealthChanged;
    }

    void Update()
    {
        if (_ai == null) return;

        // ── Non-networked polling ─────────────────────────────────────────────
        if (!_networked)
        {
            float current = _health.currentHealth;
            if (current != _lastHealth)
            {
                ShowDamage(_lastHealth, current, _health.maxHealth);
                _lastHealth = current;
            }
        }

        // ── Billboard & auto-hide ─────────────────────────────────────────────
        if (_barRoot == null || !_barRoot.activeSelf) return;

        if (_cam == null) _cam = Camera.main;
        if (_cam != null)
        {
            Vector3 dir = _barRoot.transform.position - _cam.transform.position;
            if (dir != Vector3.zero)
                _barRoot.transform.rotation = Quaternion.LookRotation(dir);

            if (_canvas != null)
            {
                float dist = dir.magnitude;
                _canvas.sortingOrder = Mathf.Clamp(10000 - (int)(dist * 100f), 0, 32767);
            }
        }

        if (_hideTimer > 0f)
        {
            _hideTimer -= Time.deltaTime;
            if (_hideTimer <= 0f)
                _barRoot.SetActive(false);
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Called by EnemyHealthBarManager immediately after instantiation.</summary>
    public void Initialize(EnemyAI ai, HealthSystem health)
    {
        _ai     = ai;
        _health = health;

        _networked = Unity.Netcode.NetworkManager.Singleton != null
                  && Unity.Netcode.NetworkManager.Singleton.IsListening;

        if (_networked)
            _ai.NetworkHealth.OnValueChanged += OnNetworkHealthChanged;
        else
            _lastHealth = _health.currentHealth;

        _canvas = _barRoot != null ? _barRoot.GetComponentInChildren<Canvas>() : null;
        if (_canvas != null)
        {
            _canvas.overrideSorting = true;
            _canvas.sortingOrder    = 100;
        }

        SetLayerRecursively(_barRoot, LayerMask.NameToLayer("HudOverlay"));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null || layer < 0) return;
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursively(t.gameObject, layer);
    }

    // ─── Callbacks ────────────────────────────────────────────────────────────

    private void OnNetworkHealthChanged(float prev, float current)
    {
        ShowDamage(prev, current, _ai.NetworkMaxHealth.Value);
    }

    private void ShowDamage(float prev, float current, float max)
    {
        if (_fillImage != null && max > 0)
            _fillImage.fillAmount = current / max;

        // Only reveal on actual damage (current < prev).
        // Prevents flashing on spawn when health initialises 0 → max.
        if (current < prev)
        {
            if (_barRoot != null) _barRoot.SetActive(true);
            _hideTimer = (current < max) ? _hideDelayDamaged : _hideDelayFull;
        }
    }
}
