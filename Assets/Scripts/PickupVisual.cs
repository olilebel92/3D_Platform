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

    // ─── Internal ─────────────────────────────────────────────────────────────
    private Vector3 _startPosition;
    private bool _bobOriginCaptured;
    private GameObject _spawnedModel;
    private GameObject _spawnedRarityParticles;
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
        if (!_bobOriginCaptured)
        {
            _startPosition = transform.position;
            _bobOriginCaptured = true;
        }

        transform.Rotate(0f, rotateSpeed * Time.deltaTime, 0f);
        float newY = _startPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
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
            Transform parent = modelAnchor != null ? modelAnchor : transform;
            _spawnedRarityParticles = Instantiate(rarity.particlePrefab, parent);
            _spawnedRarityParticles.transform.localPosition = new Vector3(0f, rarity.particleYOffset, 0f);
            _spawnedRarityParticles.transform.localRotation = Quaternion.identity;
            _spawnedRarityParticles.transform.localScale = Vector3.one * Mathf.Max(0.01f, rarity.particleScale);
        }
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
