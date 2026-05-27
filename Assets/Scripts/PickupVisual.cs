using UnityEngine;

/// <summary>
/// Pure visual behaviour for ground pickups — bob/spin and a rarity-driven glow.
/// Pair with LootPickup on the same prefab; LootPickup owns collection flow and
/// calls ApplyItemVisuals() to instantiate the SubType's 3D model and tint the
/// glow with the rarity's colour.
/// </summary>
public class PickupVisual : MonoBehaviour
{
    [Header("Spin")]
    [Tooltip("Degrees per second the pickup rotates around its Y axis.")]
    public float rotateSpeed = 90f;

    [Header("Bob")]
    [Tooltip("Height of the up/down bob in world units.")]
    public float bobHeight = 0.3f;

    [Tooltip("Speed of the bob cycle.")]
    public float bobSpeed = 2f;

    [Tooltip("How far above the spawn point the item floats (added to the bob origin).")]
    public float spawnHeightOffset = 0.5f;

    [Header("Model")]
    [Tooltip("Empty child transform under which the SubType.worldModelPrefab is instanced. " +
             "If null, the model is parented directly to this transform.")]
    public Transform modelAnchor;

    [Tooltip("Optional placeholder mesh shown when no SubType model has been applied yet " +
             "(e.g. the default sword.001 on the prefab). Automatically hidden when a real " +
             "SubType model is spawned, and restored when visuals are cleared.")]
    public GameObject placeholderModel;

    [Header("Glow")]
    [Tooltip("Optional Renderer whose emissive colour is tinted by rarity. Leave null to skip.")]
    public Renderer glowRenderer;

    [Tooltip("Optional ParticleSystem whose start colour is tinted by rarity. Leave null to skip.")]
    public ParticleSystem glowParticles;

    [Tooltip("Optional Light whose colour is tinted by rarity. Leave null to skip.")]
    public Light glowLight;

    [Header("VFX Ground Snap")]
    [Tooltip("Layers considered 'ground' when snapping the rarity VFX. The VFX is placed at " +
             "hit.y + rarity.particleYOffset so designers can author it relative to ground.")]
    public LayerMask vfxGroundMask = ~0;

    [Tooltip("How far down to raycast from above the pickup when looking for the ground surface.")]
    public float vfxGroundRayLength = 10f;

    [Tooltip("Extra multiplier applied on top of rarity.particleScale when spawning the rarity VFX. " +
             "1.0 = use the rarity's scale as-is. Bump this to make all rarity VFX bigger without " +
             "editing every RarityData asset.")]
    public float vfxScaleMultiplier = 1.4f;

    // ─── Internal ─────────────────────────────────────────────────────────────
    private Vector3 _startPosition;
    private bool _bobOriginCaptured;
    private GameObject _spawnedModel;
    private GameObject _spawnedRarityParticles;
    private float _spawnedRarityParticlesGroundY;
    private float _spawnedRarityParticlesYOffset;
    private bool _bobSpinSuppressed;
    private MaterialPropertyBlock _mpb;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    void OnEnable()
    {
        // Capture the bob origin lazily on the first Update, NOT here: on networked
        // clients NGO applies the spawn transform AFTER OnEnable, so reading position
        // now pins the bob to the prefab default and sinks the pickup underground.
        _bobOriginCaptured = false;
    }

    void Update()
    {
        // Bob/spin is suppressed by LootDropAnimation during the drop arc so it doesn't
        // fight the parabolic motion. The VFX-follow block below still runs every frame
        // so the rarity particles track the pickup's X/Z through the entire animation.
        if (!_bobSpinSuppressed)
        {
            if (!_bobOriginCaptured)
            {
                _startPosition = transform.position + new Vector3(0f, spawnHeightOffset, 0f);
                _bobOriginCaptured = true;
            }

            transform.Rotate(0f, rotateSpeed * Time.deltaTime, 0f);
            float newY = _startPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.position = new Vector3(transform.position.x, newY, transform.position.z);
        }

        // VFX is unparented (so it doesn't inherit the bob), but it should still follow
        // the pickup's X/Z — even during the drop animation. Y stays pinned to the snapped
        // ground level (refreshed by RefreshVfxGroundSnap when the pickup lands).
        if (_spawnedRarityParticles != null)
        {
            _spawnedRarityParticles.transform.position = new Vector3(
                transform.position.x,
                _spawnedRarityParticlesGroundY,
                transform.position.z);
        }
    }

    // ─── Drop-Animation Coordination ──────────────────────────────────────────

    /// <summary>
    /// Called by LootDropAnimation to suppress the bob/spin transform writes during the
    /// parabolic arc without disabling the whole component (so VFX-follow keeps running).
    /// On release (true → false) the bob origin is re-captured from the landed position.
    /// </summary>
    public void SetBobSpinSuppressed(bool suppressed)
    {
        if (_bobSpinSuppressed && !suppressed)
            _bobOriginCaptured = false;
        _bobSpinSuppressed = suppressed;
    }

    /// <summary>
    /// Re-runs the downward ground raycast from the current transform position and updates
    /// the VFX's pinned Y. Called by LootDropAnimation right after the pickup lands, so the
    /// VFX snaps to the actual terrain elevation at the scatter target instead of the
    /// terrain elevation at the enemy's death position.
    /// </summary>
    public void RefreshVfxGroundSnap()
    {
        if (_spawnedRarityParticles == null) return;
        _spawnedRarityParticlesGroundY = RaycastGroundY() + _spawnedRarityParticlesYOffset;
    }

    private float RaycastGroundY()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit,
                            vfxGroundRayLength + 0.5f, vfxGroundMask, QueryTriggerInteraction.Ignore))
        {
            return hit.point.y;
        }
        return transform.position.y;
    }

    // ─── Apply Item-Specific Visuals ──────────────────────────────────────────

    /// <summary>Convenience overload — pulls subType/rarity from the given ItemData.</summary>
    public void ApplyItemVisuals(ItemData item)
        => ApplyItemVisuals(item != null ? item.subType : null,
                            item != null ? item.rarity  : null);

    /// <summary>
    /// Instantiate the SubType's world model under modelAnchor and tint the glow to match
    /// the rarity. Accepts the SOs directly so procedural pickups (with no ItemData yet)
    /// can still set up visuals from server-synced catalog indices.
    /// Safe to call multiple times — old visuals are cleared first.
    /// </summary>
    public void ApplyItemVisuals(SubTypeData subType, RarityData rarity)
    {
        ClearSpawnedVisuals();

        // ── 3D model ──────────────────────────────────────────────────────────
        if (subType != null && subType.worldModelPrefab != null)
        {
            if (placeholderModel != null) placeholderModel.SetActive(false);
            Transform parent = modelAnchor != null ? modelAnchor : transform;
            _spawnedModel = Instantiate(subType.worldModelPrefab, parent);
            _spawnedModel.transform.localPosition = subType.worldModelPrefab.transform.localPosition;
            _spawnedModel.transform.localRotation = subType.worldModelPrefab.transform.localRotation;
        }
        else
        {
            // No subtype model available — fall back to the placeholder.
            if (placeholderModel != null) placeholderModel.SetActive(true);
        }

        // ── Rarity glow ───────────────────────────────────────────────────────
        if (rarity == null) return;

        Color glow = rarity.glowColor * Mathf.Max(0f, rarity.glowIntensity);

        if (glowRenderer != null)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            glowRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionColorId, glow);
            glowRenderer.SetPropertyBlock(_mpb);
        }

        if (glowParticles != null)
        {
            var main = glowParticles.main;
            main.startColor = rarity.glowColor;
        }

        if (glowLight != null)
        {
            glowLight.color = rarity.glowColor;
            glowLight.intensity = Mathf.Max(0.1f, rarity.glowIntensity);
        }

        if (rarity.particlePrefab != null)
        {
            // VFX is intentionally NOT parented to the pickup: the pickup transform bobs and
            // sits spawnHeightOffset above the ground, but the VFX should stay pinned to
            // the ground (raycast down, then add rarity.particleYOffset) so designers can
            // author it at e.g. Y=0.05 relative to whatever terrain it lands on.
            _spawnedRarityParticles = Instantiate(rarity.particlePrefab);
            _spawnedRarityParticlesYOffset = rarity.particleYOffset;
            _spawnedRarityParticlesGroundY = RaycastGroundY() + _spawnedRarityParticlesYOffset;
            _spawnedRarityParticles.transform.position = new Vector3(
                transform.position.x,
                _spawnedRarityParticlesGroundY,
                transform.position.z);
            _spawnedRarityParticles.transform.rotation = Quaternion.identity;
            _spawnedRarityParticles.transform.localScale = Vector3.one * Mathf.Max(0.01f, rarity.particleScale) * Mathf.Max(0.01f, vfxScaleMultiplier);
        }
    }

    void OnDestroy()
    {
        // VFX is unparented, so it won't be destroyed with the pickup automatically.
        if (_spawnedRarityParticles != null) Destroy(_spawnedRarityParticles);
    }

    private void ClearSpawnedVisuals()
    {
        if (_spawnedModel != null) Destroy(_spawnedModel);
        if (_spawnedRarityParticles != null) Destroy(_spawnedRarityParticles);
        _spawnedModel = null;
        _spawnedRarityParticles = null;
        if (placeholderModel != null) placeholderModel.SetActive(true);
    }
}
