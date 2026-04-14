using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Attach this to your Fireball prefab.
/// Moves forward, explodes on impact with AOE falloff damage.
///
/// Damage formula:
///   raw = (baseDamage + TotalSpellDamageBonus + TotalFireDamageBonus) * SpellDamageMultiplier (INT)
///   targets inside blastRadius   → raw
///   targets inside falloffRadius → lerp raw → raw * minDamageFraction
///
/// Multiplayer: must be a NetworkObject registered in NetworkManager.
///   SpellCaster spawns this server-side and passes precomputedDamage so the
///   server never needs to read per-player singletons (ExperienceManager, SkillTree).
///   OnTriggerEnter and Explode run only on the server; movement runs everywhere.
/// </summary>
public class Fireball : NetworkBehaviour
{
    // ─── Damage ───────────────────────────────────────────────────────────────

    [Header("Damage")]
    [Tooltip("Base damage before any stat or skill tree bonuses.")]
    public float baseDamage = 20f;

    // ─── AOE ──────────────────────────────────────────────────────────────────

    [Header("AOE")]
    [Tooltip("Radius of full damage on impact.")]
    public float blastRadius = 2f;

    [Tooltip("Outer radius where damage falls off. Must be >= blastRadius.")]
    public float falloffRadius = 5f;

    [Tooltip("Fraction of damage dealt at the outer falloff edge (0 = none, 1 = full).")]
    [Range(0f, 1f)]
    public float minDamageFraction = 0.25f;

    // ─── Projectile ───────────────────────────────────────────────────────────

    [Header("Projectile")]
    [Tooltip("Forward movement speed.")]
    public float speed = 15f;

    [Tooltip("Seconds before the fireball destroys itself if it hits nothing.")]
    public float lifetime = 3f;

    [Tooltip("Uniform scale applied on spawn.")]
    public float projectileScale = 1f;

    // ─── Audio ────────────────────────────────────────────────────────────────

    [Header("Audio")]
    [Tooltip("Played on all clients when the fireball launches (3D positional).")]
    [SerializeField] private AudioClip launchSound;

    [Tooltip("Played on all clients at the impact point when the fireball explodes.")]
    [SerializeField] private AudioClip hitSound;

    private AudioSource _audio;

    // ─── Runtime (set by SpellCaster before Spawn) ────────────────────────────

    /// <summary>
    /// Pre-computed raw damage passed in by SpellCaster.SpawnProjectileServerRpc.
    /// When > 0 it bypasses ComputeRawDamage() (which reads per-player singletons
    /// that are not valid on the server).
    /// </summary>
    [HideInInspector] public float precomputedDamage = 0f;

    /// <summary>Optional VFX prefab instantiated at the explosion center on impact.</summary>
    [HideInInspector] public GameObject hitEffect = null;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        transform.localScale = Vector3.one * projectileScale;

        _audio = GetComponent<AudioSource>();
        if (_audio == null)
        {
            _audio              = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake  = false;
            _audio.spatialBlend = 1f;
        }

        if (launchSound != null)
            _audio.PlayOneShot(launchSound);

        // Solo / offline: manage own lifetime here.
        // Networked lifetime is handled in OnNetworkSpawn on the server.
        if (!IsSpawned)
            Destroy(gameObject, lifetime);
    }

    // ─── NGO Lifecycle ────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Ensure SpellCaster set precomputedDamage before Spawn — server-side
            // singletons (ExperienceManager, SkillTreeManager) are not valid here.
            Debug.Assert(precomputedDamage > 0f,
                "[Fireball] precomputedDamage not set before Spawn. " +
                "Ensure SpellCaster assigns it before calling NetworkObject.Spawn().");

            Invoke(nameof(SelfDestruct), lifetime);
        }
    }

    // ─── Movement ─────────────────────────────────────────────────────────────

    void Update()
    {
        // Runs on all clients — smooth visual movement everywhere.
        transform.Translate(Vector3.forward * speed * Time.deltaTime);
    }

    // ─── Collision ────────────────────────────────────────────────────────────

    void OnTriggerEnter(Collider other)
    {
        if (!IsAuthoritative()) return;
        if (other.CompareTag("Player")) return;

        Debug.Log("[Fireball] Hit: " + other.name);
        Explode(transform.position);
    }

    // ─── Explosion ────────────────────────────────────────────────────────────

    void Explode(Vector3 origin)
    {
        float raw = precomputedDamage > 0f ? precomputedDamage : ComputeRawDamage();

        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        Collider[] hits = Physics.OverlapSphere(origin, falloffRadius);
        foreach (Collider hit in hits)
        {
            if (hit.CompareTag("Player")) continue;

            HealthSystem health = hit.GetComponent<HealthSystem>();
            if (health == null) continue;

            float dist   = Vector3.Distance(origin, hit.transform.position);
            int   damage = Mathf.RoundToInt(ComputeFalloffDamage(raw, dist));

            // Route through TakeDamageServerRpc so EnemyAI syncs NetworkHealth
            // to all clients. In singleplayer NGO is inactive, so call TakeDamage directly.
            EnemyAI enemyAI = hit.GetComponent<EnemyAI>();
            if (enemyAI != null && networked)
                enemyAI.TakeDamageServerRpc(damage, false);
            else
                health.TakeDamage(damage);

            Debug.Log($"[Fireball] Hit '{hit.name}' for {damage} damage.");
        }

        SpawnHitEffect(origin);

        if (networked)
            PlayHitSoundRpc(origin);
        else if (hitSound != null)
            AudioSource.PlayClipAtPoint(hitSound, origin);

        DespawnOrDestroy();
    }

    [Rpc(SendTo.ClientsAndHost)]
    void PlayHitSoundRpc(Vector3 pos)
    {
        if (hitSound != null)
            AudioSource.PlayClipAtPoint(hitSound, pos);
    }

    void SpawnHitEffect(Vector3 pos)
    {
        if (hitEffect == null) return;
        GameObject fx = Instantiate(hitEffect, pos, Quaternion.identity);
        // Auto-destroy: use the ParticleSystem duration if available, else 3 s.
        // startLifetime can be a curve — use duration as a safe upper-bound instead.
        ParticleSystem ps = fx.GetComponent<ParticleSystem>();
        float duration = ps != null ? ps.main.duration + ps.main.startLifetime.constantMax : 3f;
        if (ps != null && ps.main.startLifetime.mode != UnityEngine.ParticleSystemCurveMode.Constant
                       && ps.main.startLifetime.mode != UnityEngine.ParticleSystemCurveMode.TwoConstants)
            duration = ps.main.duration * 2f;
        Destroy(fx, duration);
    }

    void SelfDestruct() => DespawnOrDestroy();

    void DespawnOrDestroy()
    {
        if (IsSpawned)
            NetworkObject.Despawn(true);
        else
            Destroy(gameObject);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>True when this instance should run authoritative logic (collision, damage).</summary>
    bool IsAuthoritative()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return true;   // solo / offline
        return IsServer;   // multiplayer — server only
    }

    float ComputeRawDamage()
    {
        float spellBonus   = SkillTreeManager.Instance != null ? SkillTreeManager.Instance.TotalSpellDamageBonus   : 0f;
        float fireBonus    = SkillTreeManager.Instance != null ? SkillTreeManager.Instance.TotalFireDamageBonus    : 0f;
        float firePctBonus = SkillTreeManager.Instance != null ? SkillTreeManager.Instance.TotalFireDamagePctBonus : 0f;

        float intMultiplier = ExperienceManager.Instance != null
            ? ExperienceManager.Instance.SpellDamageMultiplier : 1f;

        return (baseDamage + spellBonus + fireBonus) * intMultiplier * (1f + firePctBonus);
    }

    float ComputeFalloffDamage(float rawDamage, float dist)
    {
        if (dist <= blastRadius)
            return rawDamage;

        float t = (dist - blastRadius) / Mathf.Max(0.001f, falloffRadius - blastRadius);
        return rawDamage * Mathf.Lerp(1f, minDamageFraction, t);
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.35f);
        Gizmos.DrawSphere(transform.position, blastRadius);

        Gizmos.color = new Color(1f, 0.8f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, falloffRadius);
    }
}
