using System.Collections;
using UnityEngine;
using Unity.Netcode;

// ─── Loot Drop Animation ──────────────────────────────────────────────────────

/// <summary>
/// Add to a LootPickup prefab to play a burst animation when the item drops.
/// EnemyReward spawns the item at the enemy's centre, then this component animates
/// it flying outward to the scatter target position in a parabolic arc.
/// Server sets the target via PreInit() before NGO Spawn(); clients receive it
/// through the NetworkVariable's initial state and start the same animation.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LootDropAnimation : NetworkBehaviour
{
    [Header("Animation")]
    [Tooltip("Time in seconds for the item to reach its target position.")]
    [SerializeField] private float duration = 0.45f;

    [Tooltip("Peak height above the start point during the arc.")]
    [SerializeField] private float arcHeight = 1.1f;

    [Tooltip("Ease curve applied to the horizontal movement. Default: ease-in-out.")]
    [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // ─── Sync ─────────────────────────────────────────────────────────────────

    // Synced scatter target. Server writes in OnNetworkSpawn; initial value is
    // included in the spawn message so clients have it before their OnNetworkSpawn.
    private readonly NetworkVariable<Vector3> _target = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Solo/pre-spawn target storage — set by EnemyReward before Spawn().
    private Vector3 _preSpawnTarget;

    // ─── Internal ─────────────────────────────────────────────────────────────
    private Collider      _col;
    private PickupVisual  _visual;
    private bool          _animating;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        _col    = GetComponent<Collider>();
        _visual = GetComponent<PickupVisual>();
    }

    void Start()
    {
        // Solo path — NGO is not running; use the pre-spawn target directly.
        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (!networkActive && _preSpawnTarget != Vector3.zero)
            StartCoroutine(Burst(transform.position, _preSpawnTarget));
    }

    // ─── Network Lifecycle ────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // No scatter target set (inventory drops, LootVisualTester previews, any
            // non-EnemyReward spawn) — skip the burst so the pickup stays where it
            // spawned instead of flying to world origin. Mirrors the Start() solo guard.
            if (_preSpawnTarget == Vector3.zero) return;

            // Publish target so all clients get it in the spawn state.
            _target.Value = _preSpawnTarget;
            StartCoroutine(Burst(transform.position, _target.Value));
        }
        else
        {
            // Initial NV state arrives with the spawn message — check it first.
            if (_target.Value != default)
                StartCoroutine(Burst(transform.position, _target.Value));
            else
                _target.OnValueChanged += OnTargetReceived; // safety fallback
        }
    }

    private void OnTargetReceived(Vector3 _, Vector3 next)
    {
        _target.OnValueChanged -= OnTargetReceived;
        if (!_animating)
            StartCoroutine(Burst(transform.position, next));
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called by EnemyReward immediately after Instantiate, before NGO Spawn().
    /// Sets the scatter target the item will fly to.
    /// </summary>
    public void PreInit(Vector3 target) => _preSpawnTarget = target;

    // ─── Burst Coroutine ──────────────────────────────────────────────────────

    private IEnumerator Burst(Vector3 from, Vector3 to)
    {
        _animating = true;

        // Disable collider so the pickup can't be collected mid-flight, and suppress
        // bob/spin so it doesn't fight the parabolic arc. PickupVisual stays enabled so
        // its Update() can keep the unparented rarity VFX tracking the pickup's X/Z.
        if (_col    != null) _col.enabled    = false;
        if (_visual != null) _visual.SetBobSpinSuppressed(true);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / duration);
            float curve = moveCurve.Evaluate(t);

            // Horizontal: interpolated with easing curve.
            // Vertical:   parabolic arc peaking at arcHeight.
            Vector3 pos = Vector3.Lerp(from, to, curve);
            pos.y += arcHeight * Mathf.Sin(t * Mathf.PI);

            transform.position = pos;
            yield return null;
        }

        transform.position = to;

        if (_col    != null) _col.enabled    = true;
        if (_visual != null)
        {
            // Re-raycast ground at the landed position so the VFX snaps to local terrain
            // height, not whatever ground the enemy died on. Then release bob/spin — the
            // bob origin is re-captured from the landed transform on the next Update().
            _visual.RefreshVfxGroundSnap();
            _visual.SetBobSpinSuppressed(false);
        }

        _animating = false;
    }
}
