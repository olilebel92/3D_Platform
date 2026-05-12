using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages enemy target selection for TargetLocked spells.
///
/// Hovering over an enemy during targeting mode makes it glow (hover color).
/// Left-clicking confirms the target. Escape cancels.
///
/// Glow is applied via MaterialPropertyBlock — no material instances are created.
/// On hover, overrides _RimColor + _RimExtension + _RimThresholds on every child
/// renderer so that the toon shader's rim term lights up the full silhouette
/// regardless of the material's authored rim settings. On un-hover, the property
/// block is cleared with SetPropertyBlock(null) to revert all overrides.
///
/// Multiplayer: disabled on non-owner clients via OnNetworkSpawn.
/// Singleplayer: active immediately via Start().
/// </summary>
public class TargetSelector : NetworkBehaviour
{
    [Header("Targeting")]
    [Tooltip("Layer mask for target-selection raycasts. Restrict to enemy layers to avoid false hits.")]
    [SerializeField] private LayerMask _targetLayerMask = ~0;

    [Header("Glow")]
    [Tooltip("Highlight color applied to an enemy while hovering over it. Drives the toon shader's _RimColor.")]
    [SerializeField] private Color _hoverGlowColor    = new Color(0.4f, 0.8f, 1f);

    [Tooltip("Intensity multiplier for the hover glow (higher = brighter).")]
    [SerializeField] private float _hoverGlowIntensity = 2f;

    private static readonly int RimColorId      = Shader.PropertyToID("_RimColor");
    private static readonly int RimExtensionId  = Shader.PropertyToID("_RimExtension");
    private static readonly int RimThresholdsId = Shader.PropertyToID("_RimThresholds");

    // ─── Public State ─────────────────────────────────────────────────────────

    /// <summary>Currently confirmed enemy target, or null.</summary>
    public Transform SelectedTarget { get; private set; }

    /// <summary>NetworkObject of the confirmed target — valid in multiplayer.</summary>
    public NetworkObject SelectedNetworkObject { get; private set; }

    /// <summary>True while the player is being prompted to click an enemy.</summary>
    public bool IsTargeting { get; private set; }

    /// <summary>Fired when the player successfully clicks a valid enemy.</summary>
    public event System.Action<Transform> OnTargetSelected;

    /// <summary>Fired when targeting is cancelled (Escape, stun, or spell switch).</summary>
    public event System.Action OnTargetingCancelled;

    // ─── Private State ────────────────────────────────────────────────────────

    private Transform            _hoveredTarget;
    private MaterialPropertyBlock _propBlock;

    // ─── NGO Lifecycle ────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) { enabled = false; return; }
        _propBlock = new MaterialPropertyBlock();
    }

    // ─── Singleplayer Fallback ────────────────────────────────────────────────

    void Start()
    {
        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (!networkActive) _propBlock = new MaterialPropertyBlock();
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    void Update()
    {
        if (!IsTargeting) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CancelTargeting();
            return;
        }

        UpdateHover();

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            TrySelectTarget();
    }

    void UpdateHover()
    {
        Transform newHover = GetEnemyUnderCursor();
        if (newHover == _hoveredTarget) return;

        // Clear glow on previous hover
        if (_hoveredTarget != null)
            ClearGlow(_hoveredTarget);

        _hoveredTarget = newHover;

        // Apply glow + cursor on new hover
        if (_hoveredTarget != null)
        {
            SetGlow(_hoveredTarget, _hoverGlowColor * _hoverGlowIntensity);
            CursorManager.Instance?.ApplyEnemyHover();
        }
        else
        {
            CursorManager.Instance?.ApplyTargeting();
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Enters targeting mode — player must left-click an enemy to confirm.</summary>
    public void BeginTargeting()
    {
        IsTargeting = true;
        ClearTarget();
        CursorManager.Instance?.ApplyTargeting();
        Debug.Log("[TargetSelector] Awaiting target — left-click an enemy.");
    }

    /// <summary>Exits targeting mode and clears glow + selection.</summary>
    public void CancelTargeting()
    {
        if (!IsTargeting) return;
        IsTargeting = false;
        if (_hoveredTarget != null) { ClearGlow(_hoveredTarget); _hoveredTarget = null; }
        ClearTarget();
        CursorManager.Instance?.ApplyDefault();
        OnTargetingCancelled?.Invoke();
        Debug.Log("[TargetSelector] Targeting cancelled.");
    }

    /// <summary>Clears the confirmed target without firing any event.</summary>
    public void ClearTarget()
    {
        SelectedTarget        = null;
        SelectedNetworkObject = null;
    }

    // ─── Selection ────────────────────────────────────────────────────────────

    void TrySelectTarget()
    {
        if (_hoveredTarget == null) return;

        ClearGlow(_hoveredTarget);

        SelectedTarget = _hoveredTarget;
        _hoveredTarget = null;
        SelectedTarget.TryGetComponent<NetworkObject>(out NetworkObject no);
        SelectedNetworkObject = no;
        IsTargeting = false;

        CursorManager.Instance?.ApplyDefault();
        Debug.Log($"[TargetSelector] Target confirmed: {SelectedTarget.name}");
        OnTargetSelected?.Invoke(SelectedTarget);
    }

    Transform GetEnemyUnderCursor()
    {
        if (Camera.main == null || Mouse.current == null) return null;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, _targetLayerMask)) return null;

        Transform t = hit.collider.transform;
        while (t != null && !t.CompareTag("Enemy"))
            t = t.parent;
        return t;
    }

    // ─── Glow ─────────────────────────────────────────────────────────────────

    void SetGlow(Transform target, Color highlight)
    {
        // Force the toon shader's rim term visible across the whole silhouette:
        //   rimAmount = (1 - NoV) * saturate(rawDiffuseAmount + _RimExtension)
        //   rimAmount = smoothstep(_RimThresholds.x, _RimThresholds.y, rimAmount)
        // _RimExtension = 1 makes the diffuse-side gating evaluate to 1 everywhere,
        // and _RimThresholds = (0, 1) gives a smooth (1 - NoV) falloff.
        foreach (Renderer rend in target.GetComponentsInChildren<Renderer>())
        {
            rend.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(RimColorId,      highlight);
            _propBlock.SetFloat(RimExtensionId,  1f);
            _propBlock.SetVector(RimThresholdsId, new Vector4(0f, 1f, 0f, 0f));
            rend.SetPropertyBlock(_propBlock);
        }
    }

    void ClearGlow(Transform target)
    {
        if (target == null) return;
        foreach (Renderer rend in target.GetComponentsInChildren<Renderer>())
            rend.SetPropertyBlock(null);
    }
}
