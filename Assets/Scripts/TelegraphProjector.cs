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
             "Default (1) = 'Default' rendering layer — change to your dedicated Ground layer bit.")]
    [SerializeField] private uint groundRenderingLayerMask = 1;

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

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        _projectorGO = new GameObject("TelegraphDecal") { hideFlags = HideFlags.HideInHierarchy };
        _projectorGO.transform.SetParent(null);

        _decalProjector                  = _projectorGO.AddComponent<DecalProjector>();
        _decalProjector.enabled          = false;
        _decalProjector.renderingLayerMask = groundRenderingLayerMask;

        // Project downward: Euler(90, 0, 0) tilts local +Z to world -Y.
        _projectorGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    void OnDestroy()
    {
        if (_projectorGO   != null) Destroy(_projectorGO);
        if (_activeMat     != null) Destroy(_activeMat);
        if (_activeTexture != null) Destroy(_activeTexture);
    }

    void LateUpdate()
    {
        if (!_active || _spell == null || _caster == null) return;
        if (_spell.telegraphShape == TelegraphShape.None) return;

        UpdateProjectorTransform();
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

    /// <summary>Hides the telegraph and resets state.</summary>
    public void Hide()
    {
        _active = false;
        _spell  = null;
        _caster = null;
        if (_decalProjector != null)
            _decalProjector.enabled = false;
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
        Vector3 center = _spell.telegraphFollowsCursor ? IsoAim.WorldPoint : _caster.position;
        float   d      = _spell.telegraphRadius * 2f;

        _projectorGO.transform.position = new Vector3(center.x, center.y + projectorHeight, center.z);
        _projectorGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        _decalProjector.size            = new Vector3(d, d, projectorDepth);
    }

    private void UpdateCone()
    {
        Vector3 aimDir = GetAimDirection();
        Vector3 origin = _caster.position + aimDir * _spell.telegraphOriginOffset;
        float   yaw    = Mathf.Atan2(aimDir.x, aimDir.z) * Mathf.Rad2Deg;

        float   half   = _spell.telegraphAngle * 0.5f * Mathf.Deg2Rad;
        float   w      = _spell.telegraphLength * 2f * Mathf.Sin(half);
        Vector3 pivot  = origin + aimDir * (_spell.telegraphLength * 0.5f);

        _projectorGO.transform.position = new Vector3(pivot.x, pivot.y + projectorHeight, pivot.z);
        _projectorGO.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
        _decalProjector.size            = new Vector3(w, _spell.telegraphLength, projectorDepth);
    }

    private void UpdateLine()
    {
        Vector3 aimDir = GetAimDirection();
        Vector3 origin = _caster.position + aimDir * _spell.telegraphOriginOffset;
        float   yaw    = Mathf.Atan2(aimDir.x, aimDir.z) * Mathf.Rad2Deg;

        Vector3 pivot  = origin + aimDir * (_spell.telegraphLength * 0.5f);

        _projectorGO.transform.position = new Vector3(pivot.x, pivot.y + projectorHeight, pivot.z);
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

        _decalProjector.material           = _activeMat;
        _decalProjector.renderingLayerMask = groundRenderingLayerMask;

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

    // ─── Gizmos ───────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        if (!_active || _spell == null) return;
        Gizmos.color = _spell.telegraphColor;
        Gizmos.DrawWireSphere(IsoAim.WorldPoint, 0.3f);
    }
}
