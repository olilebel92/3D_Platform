using UnityEngine;

/// <summary>
/// Attach this to your Fireball prefab.
/// Moves forward, explodes on impact with AOE falloff damage.
///
/// Damage formula:
///   raw = (baseDamage + TotalSpellDamageBonus + TotalFireDamageBonus) * SpellDamageMultiplier (INT)
///   targets inside blastRadius   → raw
///   targets inside falloffRadius → lerp raw → raw * minDamageFraction
/// </summary>
public class Fireball : MonoBehaviour
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

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        transform.localScale = Vector3.one * projectileScale;
        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        transform.Translate(Vector3.forward * speed * Time.deltaTime);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) return;

        Debug.Log("[Fireball] Hit: " + other.name);
        Explode(transform.position);
    }

    // ─── Explosion ────────────────────────────────────────────────────────────

    void Explode(Vector3 origin)
    {
        float raw = ComputeRawDamage();

        Collider[] hits = Physics.OverlapSphere(origin, falloffRadius);
        foreach (Collider hit in hits)
        {
            if (hit.CompareTag("Player")) continue;

            HealthSystem health = hit.GetComponent<HealthSystem>();
            if (health == null) continue;

            float dist   = Vector3.Distance(origin, hit.transform.position);
            float damage = ComputeFalloffDamage(raw, dist);

            health.TakeDamage(Mathf.RoundToInt(damage));
            Debug.Log($"[Fireball] Hit '{hit.name}' for {damage:F1} damage.");
        }

        Destroy(gameObject);
    }

    // ─── Damage Helpers ───────────────────────────────────────────────────────

    float ComputeRawDamage()
    {
        float spellBonus = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.TotalSpellDamageBonus : 0f;

        float fireBonus = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.TotalFireDamageBonus : 0f;

        // SpellDamageMultiplier = 1 + (EffectiveINT * intSpellDamagePct)
        float intMultiplier = ExperienceManager.Instance != null
            ? ExperienceManager.Instance.SpellDamageMultiplier : 1f;

        return (baseDamage + spellBonus + fireBonus) * intMultiplier;
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
