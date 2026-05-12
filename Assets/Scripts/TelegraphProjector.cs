using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Draws a coloured ground decal (circle, cone, or line) to telegraph a spell's
/// area before it fires — Wildstar-style visual cues.
///
/// Uses URP's DecalProjector to project the shape downward onto any surface,
/// automatically conforming to terrain without Y-sampling or ZTest hacks.
///
/// Setup:
///   1. Add this component to the Player prefab.
///   2. URP Renderer asset → Add Renderer Feature → Decal (Screen Space or D-Buffer).
///   3. Create a Material using shader "Shader Graphs/Decal". Assign to Decal Material.
///   4. In Project Settings → Graphics → URP Asset, add a custom Rendering Layer
///      named "Ground" (index 1 = value 2, index 2 = value 4, etc.).
///   5. On your terrain/ground MeshRenderer, enable that Rendering Layer.
///   6. Set Ground Rendering Layer Mask here to match that layer's bit value.
/// </summary>
public class TelegraphProjector : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Material")]
    [Tooltip("A URP Decal material (Shader Graphs/Decal). Instanced at runtime.")]
    [SerializeField] private Material decalMaterial;

    [Header("Projector Volume")]
    [Tooltip("Height above the aim point to place the DecalProjector pivot.")]
    [SerializeField] private float projectorHeight = 5f;

    [Tooltip("Total projection depth (Size Z). Must exceed projectorHeight so " +
             "the decal always reaches the ground below the aim point.")]
    [SerializeField] private float projectorDepth = 12f;

    [Header("Rendering Layer")]
    [Tooltip("URP Rendering Layer Mask that the decal projects onto. " +
             "Set this to match the Rendering Layer assigned to your ground/terrain meshes only. " +
             "Pick the dedicated Ground layer — leaving 'Default' selected projects onto everything.")]
    [SerializeField] private RenderingLayerMask groundRenderingLayerMask = 1;

    [Header("Ground Snap")]
    [Tooltip("Physics layer mask used to snap the projector to ground level when the caster is airborne.")]
    [SerializeField] private LayerMask groundSnapMask = ~0;

    [Header("Texture")]
    [Tooltip("Width/height in pixels of the generated mask texture. 256 is fine for most telegraphs.")]
    [SerializeField] private int textureResolution = 256;

    // ─── Runtime ──────────────────────────────────────────────────────────────

    private SpellData      _spell;
    private Transform      _caster;
    private bool           _active;

    private GameObject     _projectorGO;
    private DecalProjector _decalProjector;
    private Material       _activeMat;
    private Texture2D      _activeTexture;

    // ─── Range Ring ───────────────────────────────────────────────────────────

    private GameObject     _rangeProjectorGO;
    private DecalProjector _rangeDecalProjector;
    private Material       _rangeMat;
    private Texture2D      _rangeTexture;
    private Transform      _rangeCaster;
    private float          _rangeRadius;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        _projectorGO = new GameObject("TelegraphDecal") { hideFlags = HideFlags.HideInHierarchy };
        _projectorGO.transform.SetParent(null);

        _decalProjector                    = _projectorGO.AddComponent<DecalProjector>();
        _decalProjector.enabled            = false;
        _decalProjector.renderingLayerMask = groundRenderingLayerMask;
        _projectorGO.transform.rotation    = Quaternion.Euler(90f, 0f, 0f);

        _rangeProjectorGO = new GameObject("RangeRingDecal") { hideFlags = HideFlags.HideInHierarchy };
        _rangeProjectorGO.transform.SetParent(null);
        _rangeDecalProjector                    = _rangeProjectorGO.AddComponent<DecalProjector>();
        _rangeDecalProjector.enabled            = false;
        _rangeDecalProjector.renderingLayerMask = groundRenderingLayerMask;
        _rangeProjectorGO.transform.rotation    = Quaternion.Euler(90f, 0f, 0f);
    }

    void OnDestroy()
    {
        if (_projectorGO      != null) Destroy(_projectorGO);
        if (_activeMat        != null) Destroy(_activeMat);
        if (_activeTexture    != null) Destroy(_activeTexture);
        if (_rangeProjectorGO != null) Destroy(_rangeProjectorGO);
        if (_rangeMat         != null) Destroy(_rangeMat);
        if (_rangeTexture     != null) Destroy(_rangeTexture);
    }

    void LateUpdate()
    {
        if (_active && _spell != null && _caster != null && _spell.telegraphShape != TelegraphShape.None)
            UpdateProjectorTransform();

        if (_rangeDecalProjector != null && _rangeDecalProjector.enabled && _rangeCaster != null)
        {
            float d       = _rangeRadius * 2f;
            float groundY = GetGroundY(_rangeCaster.position.x, _rangeCaster.position.z, _rangeCaster.position.y);
            _rangeProjectorGO.transform.position = new Vector3(
                _rangeCaster.position.x,
                groundY + projectorHeight,
                _rangeCaster.position.z);
            _rangeDecalProjector.size = new Vector3(d, d, projectorDepth);
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Activates the telegraph decal for <paramref name="spell"/>.
    /// Does nothing if the spell has no telegraph shape configured.
    /// </summary>
    public void Show(SpellData spell, Transform caster)
    {
        _spell  = spell;
        _caster = caster;
        _active = true;

        if (spell.telegraphShape == TelegraphShape.None)
        {
            _decalProjector.enabled = false;
            return;
        }

        RebuildDecal();
        _decalProjector.enabled = true;
    }

    /// <summary>Hides the spell telegraph and the range ring.</summary>
    public void Hide()
    {
        _active = false;
        _spell  = null;
        _caster = null;
        if (_decalProjector != null)
            _decalProjector.enabled = false;
        HideRange();
    }

    /// <summary>
    /// Shows a range ring on the ground centred on <paramref name="caster"/>.
    /// Radius matches the spell's castRange so the player can see the cast boundary.
    /// </summary>
    public void ShowRange(float radius, Transform caster)
    {
        if (decalMaterial == null || radius <= 0f) return;

        _rangeCaster = caster;
        _rangeRadius = radius;

        if (_rangeTexture != null) { Destroy(_rangeTexture); _rangeTexture = null; }
        if (_rangeMat     != null) { Destroy(_rangeMat);     _rangeMat     = null; }

        _rangeTexture = BuildRingTexture(textureResolution, new Color32(255, 210, 40, 130));
        _rangeMat     = new Material(decalMaterial);

        bool set = false;
        if (_rangeMat.HasProperty("Base_Map"))      { _rangeMat.SetTexture("Base_Map",      _rangeTexture); set = true; }
        if (_rangeMat.HasProperty("_BaseColorMap")) { _rangeMat.SetTexture("_BaseColorMap", _rangeTexture); set = true; }
        if (_rangeMat.HasProperty("_BaseMap"))      { _rangeMat.SetTexture("_BaseMap",      _rangeTexture); set = true; }
        if (!set) _rangeMat.mainTexture = _rangeTexture;

        // Set rendering layer BEFORE material — see RebuildDecal() for why.
        _rangeDecalProjector.renderingLayerMask = groundRenderingLayerMask;
        _rangeDecalProjector.material           = _rangeMat;

        float d = radius * 2f;
        _rangeProjectorGO.transform.position = new Vector3(
            caster.position.x, caster.position.y + projectorHeight, caster.position.z);
        _rangeDecalProjector.size    = new Vector3(d, d, projectorDepth);
        _rangeDecalProjector.enabled = true;
    }

    /// <summary>Hides the range ring without affecting the spell telegraph.</summary>
    public void HideRange()
    {
        _rangeCaster = null;
        if (_rangeDecalProjector != null)
            _rangeDecalProjector.enabled = false;
    }

    // ─── Ground Snap ─────────────────────────────────────────────────────────

    private static readonly RaycastHit[] _groundHits = new RaycastHit[16];

    /// <summary>
    /// Returns the ground Y beneath (worldX, worldZ), ignoring the caster's own
    /// collider by only accepting hits at or below <paramref name="fallbackY"/>
    /// (the caster's feet). The highest such hit is returned so the projector
    /// snaps to the surface the caster is standing on, not a pit below.
    /// </summary>
    private float GetGroundY(float worldX, float worldZ, float fallbackY)
    {
        Vector3 origin = new Vector3(worldX, fallbackY + 50f, worldZ);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, 100f,
                                            groundSnapMask, QueryTriggerInteraction.Ignore);

        float bestY = float.MinValue;
        bool  found = false;
        for (int i = 0; i < count; i++)
        {
            float y = _groundHits[i].point.y;
            if (y <= fallbackY && y > bestY) { bestY = y; found = true; }
        }
        return found ? bestY : fallbackY;
    }

    // ─── Projector Transform ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the flat aim direction. When IsoAim has no valid hit (gamepad right
    /// stick neutral), falls back to the caster's forward so the telegraph tracks
    /// movement direction instead of pointing toward world origin.
    /// </summary>
    private Vector3 GetAimDirection()
    {
        if (IsoAim.HasHit)
            return IsoAim.AimDirectionFrom(_caster.position);

        Vector3 fwd = _caster.forward;
        fwd.y = 0f;
        return fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
    }

    private void UpdateProjectorTransform()
    {
        switch (_spell.telegraphShape)
        {
            case TelegraphShape.Circle: UpdateCircle(); break;
            case TelegraphShape.Cone:   UpdateCone();   break;
            case TelegraphShape.Line:   UpdateLine();   break;
        }
    }

    private void UpdateCircle()
    {
        Vector3 center  = _spell.telegraphFollowsCursor ? IsoAim.WorldPoint : _caster.position;
        float   groundY = GetGroundY(center.x, center.z, center.y);
        float   d       = _spell.telegraphRadius * 2f;

        _projectorGO.transform.position = new Vector3(center.x, groundY + projectorHeight, center.z);
        _projectorGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        _decalProjector.size            = new Vector3(d, d, projectorDepth);
    }

    private void UpdateCone()
    {
        Vector3 aimDir  = GetAimDirection();
        Vector3 origin  = _caster.position + aimDir * _spell.telegraphOriginOffset;
        float   yaw     = Mathf.Atan2(aimDir.x, aimDir.z) * Mathf.Rad2Deg;

        float   half    = _spell.telegraphAngle * 0.5f * Mathf.Deg2Rad;
        float   w       = _spell.telegraphLength * 2f * Mathf.Sin(half);
        Vector3 pivot   = origin + aimDir * (_spell.telegraphLength * 0.5f);
        float   groundY = GetGroundY(pivot.x, pivot.z, pivot.y);

        _projectorGO.transform.position = new Vector3(pivot.x, groundY + projectorHeight, pivot.z);
        _projectorGO.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
        _decalProjector.size            = new Vector3(w, _spell.telegraphLength, projectorDepth);
    }

    private void UpdateLine()
    {
        Vector3 aimDir  = GetAimDirection();
        Vector3 origin  = _caster.position + aimDir * _spell.telegraphOriginOffset;
        float   yaw     = Mathf.Atan2(aimDir.x, aimDir.z) * Mathf.Rad2Deg;

        Vector3 pivot   = origin + aimDir * (_spell.telegraphLength * 0.5f);
        float   groundY = GetGroundY(pivot.x, pivot.z, pivot.y);

        _projectorGO.transform.position = new Vector3(pivot.x, groundY + projectorHeight, pivot.z);
        _projectorGO.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
        _decalProjector.size            = new Vector3(_spell.telegraphWidth, _spell.telegraphLength, projectorDepth);
    }

    // ─── Decal Material ───────────────────────────────────────────────────────

    private void RebuildDecal()
    {
        if (_activeTexture != null) { Destroy(_activeTexture); _activeTexture = null; }
        if (_activeMat     != null) { Destroy(_activeMat);     _activeMat     = null; }

        if (decalMaterial == null) return;

        Color32 tint = _spell.ResolvedTelegraphColor;

        _activeTexture = _spell.telegraphShape switch
        {
            TelegraphShape.Circle => BuildCircleTexture(textureResolution, tint),
            TelegraphShape.Cone   => BuildConeTexture(textureResolution, _spell.telegraphAngle, tint),
            TelegraphShape.Line   => BuildLineTexture(textureResolution, tint),
            _                     => null
        };

        if (_activeTexture == null) return;

        _activeMat = new Material(decalMaterial);

        // Try texture slot names used across URP Decal shader versions.
        // "Base_Map" is the Shader Graph reference name (no leading underscore).
        // "_BaseColorMap" / "_BaseMap" are HLSL property names in other variants.
        bool set = false;
        if (_activeMat.HasProperty("Base_Map"))      { _activeMat.SetTexture("Base_Map",      _activeTexture); set = true; }
        if (_activeMat.HasProperty("_BaseColorMap")) { _activeMat.SetTexture("_BaseColorMap", _activeTexture); set = true; }
        if (_activeMat.HasProperty("_BaseMap"))      { _activeMat.SetTexture("_BaseMap",      _activeTexture); set = true; }
        if (!set) _activeMat.mainTexture = _activeTexture;

        // URP quirk: DecalProjector.renderingLayerMask setter does NOT call OnValidate,
        // so the cached entity mask only updates when something else (material change)
        // triggers re-registration. Set the mask FIRST so the material-change handler
        // re-registers with the correct value — otherwise decals bleed onto every layer.
        _decalProjector.renderingLayerMask = groundRenderingLayerMask;
        _decalProjector.material           = _activeMat;

        UpdateProjectorTransform();
    }

    // ─── Procedural Textures ──────────────────────────────────────────────────

    static Texture2D BuildCircleTexture(int res, Color32 tint)
    {
        var tex    = new Texture2D(res, res, TextureFormat.RGBA32, false) { name = "TelegraphCircle" };
        var pixels = new Color32[res * res];
        float half = res * 0.5f;
        var   clear = new Color32(0, 0, 0, 0);

        for (int py = 0; py < res; py++)
        for (int px = 0; px < res; px++)
        {
            float dx = px - half + 0.5f;
            float dy = py - half + 0.5f;
            pixels[py * res + px] = dx * dx + dy * dy <= half * half ? tint : clear;
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D BuildConeTexture(int res, float angleDeg, Color32 tint)
    {
        var tex    = new Texture2D(res, res, TextureFormat.RGBA32, false) { name = "TelegraphCone" };
        var pixels = new Color32[res * res];
        var clear  = new Color32(0, 0, 0, 0);

        float half    = angleDeg * 0.5f * Mathf.Deg2Rad;
        float cosHalf = Mathf.Cos(half);
        float sinHalf = Mathf.Sin(half);
        float sin2    = sinHalf * sinHalf;

        for (int py = 0; py < res; py++)
        for (int px = 0; px < res; px++)
        {
            float u  = (px + 0.5f) / res;
            float v  = (py + 0.5f) / res;
            float cu = u - 0.5f;

            bool inside = v > 0f
                       && 2f * Mathf.Abs(cu) * cosHalf <= v
                       && 4f * cu * cu * sin2 + v * v  <= 1f;

            pixels[py * res + px] = inside ? tint : clear;
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D BuildLineTexture(int res, Color32 tint)
    {
        var tex    = new Texture2D(res, res, TextureFormat.RGBA32, false) { name = "TelegraphLine" };
        var pixels = new Color32[res * res];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = tint;
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D BuildRingTexture(int res, Color32 tint)
    {
        var tex    = new Texture2D(res, res, TextureFormat.RGBA32, false) { name = "RangeRing" };
        var pixels = new Color32[res * res];
        float half  = res * 0.5f;
        float outer = half;
        float inner = half * 0.95f; // ring is 5% of radius thick
        var   clear = new Color32(0, 0, 0, 0);

        for (int py = 0; py < res; py++)
        for (int px = 0; px < res; px++)
        {
            float dx   = px - half + 0.5f;
            float dy   = py - half + 0.5f;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            pixels[py * res + px] = dist >= inner && dist <= outer ? tint : clear;
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        if (!_active || _spell == null) return;
        Gizmos.color = _spell.telegraphColor;
        Gizmos.DrawWireSphere(IsoAim.WorldPoint, 0.3f);
    }
}
