using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space HP bar displayed above an enemy when it takes damage.
/// Hidden by default; shows on damage, auto-hides after hideDelay seconds.
///
/// Networked mode: reads from EnemyAI.NetworkHealth (NetworkVariable) — synced
/// to all clients automatically whenever the server updates it.
///
/// Non-networked mode (solo play without NGO host): polls HealthSystem.currentHealth
/// directly each frame so the bar works when testing without going through the main menu.
///
/// Setup on the enemy prefab:
///   1. Add a child GameObject "HealthBarCanvas" — Canvas, Render Mode = World Space.
///   2. Under it add a Background Image and a Fill Image (Type = Filled, Method = Horizontal).
///   3. Assign _barRoot (the canvas GO) and _fillImage (the Fill image) in the Inspector.
///   4. Add this component to the enemy root (same object as EnemyAI).
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
    [SerializeField] private float _hideDelayFull     = 3f;

    [Tooltip("Seconds before the bar hides when the enemy is below full HP.")]
    [SerializeField] private float _hideDelayDamaged  = 10f;

    // ─── Private State ────────────────────────────────────────────────────────

    private EnemyAI      _ai;
    private HealthSystem _health;
    private Camera       _cam;
    private float        _hideTimer;
    private bool         _networked;
    private int          _lastHealth;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        _ai     = GetComponent<EnemyAI>();
        _health = GetComponent<HealthSystem>();

        if (_ai == null || _health == null)
        {
            Debug.LogWarning("[EnemyHealthBar] Missing EnemyAI or HealthSystem on " + gameObject.name);
            enabled = false;
            return;
        }

        if (_barRoot != null) _barRoot.SetActive(false);

        _networked = Unity.Netcode.NetworkManager.Singleton != null
                  && Unity.Netcode.NetworkManager.Singleton.IsListening;

        if (_networked)
        {
            // Networked: driven by NetworkVariable — fires on all clients when server updates.
            _ai.NetworkHealth.OnValueChanged += OnNetworkHealthChanged;
        }
        else
        {
            // Non-networked: seed the last-known value so the first hit is detected correctly.
            _lastHealth = _health.maxHealth;
        }
    }

    void OnDestroy()
    {
        if (_ai != null)
            _ai.NetworkHealth.OnValueChanged -= OnNetworkHealthChanged;
    }

    void Update()
    {
        // ── Non-networked polling ─────────────────────────────────────────────
        // When NGO isn't running, NetworkVariables are never updated.
        // Compare currentHealth against the last known value each frame.
        if (!_networked)
        {
            int current = _health.currentHealth;
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
        }

        if (_hideTimer > 0f)
        {
            _hideTimer -= Time.deltaTime;
            if (_hideTimer <= 0f)
                _barRoot.SetActive(false);
        }
    }

    // ─── Callbacks ────────────────────────────────────────────────────────────

    private void OnNetworkHealthChanged(int prev, int current)
    {
        ShowDamage(prev, current, _ai.NetworkMaxHealth.Value);
    }

    private void ShowDamage(int prev, int current, int max)
    {
        if (_fillImage != null && max > 0)
            _fillImage.fillAmount = (float)current / max;

        // Only reveal the bar on actual damage (current < prev).
        // This prevents it from flashing on spawn when health initialises 0 → max.
        if (current < prev)
        {
            if (_barRoot != null) _barRoot.SetActive(true);
            _hideTimer = (current < max) ? _hideDelayDamaged : _hideDelayFull;
        }
    }
}
