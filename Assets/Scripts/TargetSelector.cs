using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages enemy target selection for TargetLocked spells.
///
/// Three hover modes selectable from the Inspector:
///   Outline          — adds a HackNSLASH/TargetOutline material pass to the enemy's
///                      renderers. Uses smooth normals (averaged per vertex position,
///                      baked into UV2 on first hover) so hard-edge meshes have no
///                      corner gaps, and the Y-clamp in the shader prevents underground.
///   RimGlow          — overrides _RimColor/_RimExtension/_RimThresholds via
///                      MaterialPropertyBlock for a full-surface rim glow.
///   OutlineAndRimGlow — both effects simultaneously.
///
/// Smooth-normal meshes are cached statically per original mesh so the bake only
/// runs once per unique mesh across the lifetime of the session.
///
/// Multiplayer: disabled on non-owner clients via OnNetworkSpawn.
/// Singleplayer: active immediately via Start().
/// </summary>
public class TargetSelector : NetworkBehaviour
{
    public enum HoverMode { Outline, RimGlow, OutlineAndRimGlow }

    [Header("Targeting")]
    [Tooltip("Layer mask for target-selection raycasts. Restrict to enemy layers to avoid false hits.")]
    [SerializeField] private LayerMask _targetLayerMask = ~0;

    [Header("Hover Mode")]
    [Tooltip("Outline: clean hull outline via a custom shader.\nRimGlow: full-surface rim glow.\nOutlineAndRimGlow: both simultaneously.")]
    [SerializeField] private HoverMode _hoverMode = HoverMode.Outline;

    [Header("Outline")]
    [Tooltip("Outline color applied to an enemy while hovering over it.")]
    [SerializeField] private Color _outlineColor = Color.red;

    [Tooltip("Outline thickness in world units.")]
    [SerializeField] private float _outlineThickness = 0.03f;

    [Header("Rim Glow")]
    [Tooltip("Highlight color applied to an enemy while hovering over it. Drives the toon shader's _RimColor.")]
    [SerializeField] private Color _hoverGlowColor = new Color(0.4f, 0.8f, 1f);

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

    private Transform             _hoveredTarget;
    private Material              _outlineMat;
    private MaterialPropertyBlock _propBlock;

    // Smooth-normal meshes cached by original mesh instance ID — static so baking
    // runs once per unique mesh across the whole session.
    private static readonly Dictionary<int, Mesh> s_smoothMeshCache = new();

    // ─── NGO Lifecycle ────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) { enabled = false; return; }
        Init();
    }

    // ─── Singleplayer Fallback ────────────────────────────────────────────────

    void Start()
    {
        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (!networkActive) Init();
    }

    void Init()
    {
        _propBlock = new MaterialPropertyBlock();

        var shader = Shader.Find("HackNSLASH/TargetOutline");
        if (shader == null)
            Debug.LogWarning("[TargetSelector] HackNSLASH/TargetOutline shader not found — outline disabled.");
        else
            _outlineMat = new Material(shader);
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (_outlineMat != null) Destroy(_outlineMat);
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    void Update()
    {
        if (!IsTargeting) return;

        if (InputManager.UI.Cancel.WasPressedThisFrame())
        {
            CancelTargeting();
            return;
        }

        UpdateHover();

        if (InputManager.Player.Fire.WasPressedThisFrame())
            TrySelectTarget();
    }

    void UpdateHover()
    {
        Transform newHover = GetEnemyUnderCursor();
        if (newHover == _hoveredTarget) return;

        if (_hoveredTarget != null)
            ClearHover(_hoveredTarget);

        _hoveredTarget = newHover;

        if (_hoveredTarget != null)
        {
            ApplyHover(_hoveredTarget);
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

    /// <summary>Exits targeting mode and clears hover effect + selection.</summary>
    public void CancelTargeting()
    {
        if (!IsTargeting) return;
        IsTargeting = false;
        if (_hoveredTarget != null) { ClearHover(_hoveredTarget); _hoveredTarget = null; }
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

        ClearHover(_hoveredTarget);

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
        // Pointer position for a screen-to-world raycast is a device read by design
        // (allowed exception, like CursorManager) — the Input System has no cleaner
        // equivalent for projecting the cursor into the world.
        if (Camera.main == null || Mouse.current == null) return null;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, _targetLayerMask)) return null;

        Transform t = hit.collider.transform;
        while (t != null && !t.CompareTag("Enemy"))
            t = t.parent;

        if (t == null) return null;

        // Reject dead enemies (e.g. boss keeps collider after death) — mirrors LockOnSystem
        HealthSystem hs = t.GetComponent<HealthSystem>();
        if (hs != null && hs.currentHealth <= 0f) return null;

        return t;
    }

    // ─── Hover Dispatch ───────────────────────────────────────────────────────

    void ApplyHover(Transform target)
    {
        if (_hoverMode == HoverMode.Outline || _hoverMode == HoverMode.OutlineAndRimGlow)
            SetOutline(target);
        if (_hoverMode == HoverMode.RimGlow || _hoverMode == HoverMode.OutlineAndRimGlow)
            SetRimGlow(target, _hoverGlowColor * _hoverGlowIntensity);
    }

    void ClearHover(Transform target)
    {
        if (_hoverMode == HoverMode.Outline || _hoverMode == HoverMode.OutlineAndRimGlow)
            ClearOutline(target);
        if (_hoverMode == HoverMode.RimGlow || _hoverMode == HoverMode.OutlineAndRimGlow)
            ClearRimGlow(target);
    }

    // ─── Outline ──────────────────────────────────────────────────────────────

    void SetOutline(Transform target)
    {
        if (_outlineMat == null) return;
        _outlineMat.SetColor("_OutlineColor", _outlineColor);
        _outlineMat.SetFloat("_OutlineThickness", _outlineThickness);

        foreach (Renderer rend in target.GetComponentsInChildren<Renderer>())
        {
            EnsureSmoothNormals(rend);

            var mats = rend.sharedMaterials;
            var withOutline = new Material[mats.Length + 1];
            mats.CopyTo(withOutline, 0);
            withOutline[mats.Length] = _outlineMat;
            rend.sharedMaterials = withOutline;
        }
    }

    void ClearOutline(Transform target)
    {
        if (target == null) return;
        foreach (Renderer rend in target.GetComponentsInChildren<Renderer>())
        {
            var mats = rend.sharedMaterials;
            if (mats.Length > 0 && mats[mats.Length - 1] == _outlineMat)
            {
                var restored = new Material[mats.Length - 1];
                System.Array.Copy(mats, restored, restored.Length);
                rend.sharedMaterials = restored;
            }
        }
    }

    // Ensures the renderer's mesh has averaged normals stored in UV2 so the
    // outline shader can use them for gap-free extrusion. Result is cached.
    static void EnsureSmoothNormals(Renderer rend)
    {
        Mesh src = GetMesh(rend);
        if (src == null) return;

        int id = src.GetInstanceID();
        if (!s_smoothMeshCache.TryGetValue(id, out Mesh smooth))
        {
            smooth = Object.Instantiate(src);
            smooth.name = src.name + "_SN";
            BakeSmoothNormals(src, smooth);

            // Cache by both IDs so a second hover on the same enemy returns immediately.
            s_smoothMeshCache[id]                       = smooth;
            s_smoothMeshCache[smooth.GetInstanceID()]   = smooth;
        }

        SetMesh(rend, smooth);
    }

    static void BakeSmoothNormals(Mesh src, Mesh dst)
    {
        var verts   = src.vertices;
        var normals = src.normals;

        var sums = new Dictionary<Vector3, Vector3>(verts.Length);
        for (int i = 0; i < verts.Length; i++)
        {
            sums.TryGetValue(verts[i], out Vector3 s);
            sums[verts[i]] = s + normals[i];
        }

        var smooth = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
            smooth[i] = sums[verts[i]].normalized;

        dst.SetUVs(2, new List<Vector3>(smooth));
    }

    static Mesh GetMesh(Renderer rend)
    {
        if (rend is SkinnedMeshRenderer smr) return smr.sharedMesh;
        if (rend.TryGetComponent<MeshFilter>(out var mf)) return mf.sharedMesh;
        return null;
    }

    static void SetMesh(Renderer rend, Mesh mesh)
    {
        if (rend is SkinnedMeshRenderer smr) smr.sharedMesh = mesh;
        else if (rend.TryGetComponent<MeshFilter>(out var mf)) mf.sharedMesh = mesh;
    }

    // ─── Rim Glow ─────────────────────────────────────────────────────────────

    void SetRimGlow(Transform target, Color highlight)
    {
        // _RimExtension = 1 makes the diffuse-side gating evaluate to 1 everywhere.
        // _RimThresholds = (0, 1) gives a smooth (1 - NoV) falloff across the full surface.
        foreach (Renderer rend in target.GetComponentsInChildren<Renderer>())
        {
            rend.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(RimColorId,       highlight);
            _propBlock.SetFloat(RimExtensionId,   1f);
            _propBlock.SetVector(RimThresholdsId, new Vector4(0f, 1f, 0f, 0f));
            rend.SetPropertyBlock(_propBlock);
        }
    }

    void ClearRimGlow(Transform target)
    {
        if (target == null) return;
        foreach (Renderer rend in target.GetComponentsInChildren<Renderer>())
            rend.SetPropertyBlock(null);
    }
}
