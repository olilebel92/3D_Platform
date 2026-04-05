using UnityEngine;

/// <summary>
/// Attach this to your Coin prefab.
/// The coin awards XP on pickup and destroys itself.
/// </summary>
public class CoinPickup : MonoBehaviour
{
    [Header("Coin Settings")]
    [Tooltip("Tag used to identify the player.")]
    public string playerTag = "Player";

    [Header("XP Reward")]
    [Tooltip("If true, grants XP to the player when this coin is picked up.")]
    public bool giveXP = true;

    [Tooltip("Amount of XP granted on pickup. Only used if Give XP is enabled.")]
    public int xpReward = 25;

    [Header("HP Reward")]
    [Tooltip("If true, heals the player when this coin is picked up.")]
    public bool giveHP = false;

    [Tooltip("Amount of HP restored on pickup. Only used if Give HP is enabled.")]
    public int hpReward = 1;

    [Header("Lifetime")]
    [Tooltip("Seconds before this pickup auto-destroys if not collected. 0 = never.")]
    public float lifetime = 30f;

    [Header("Effects (optional)")]
    [Tooltip("Particle prefab spawned at the coin's position on pickup.")]
    public GameObject pickupParticles;

    [Tooltip("Sound played on pickup.")]
    public AudioClip pickupSound;

    void Start()
    {
        if (lifetime > 0f)
            Destroy(gameObject, lifetime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        // ── XP ────────────────────────────────────────────────────────────────
        if (giveXP && xpReward > 0)
        {
            ExperienceManager xp = other.GetComponent<ExperienceManager>();
            if (xp != null)
                xp.GainXP(xpReward);
            else
                Debug.LogWarning("[CoinPickup] No ExperienceManager found on the player that triggered this pickup!");
        }

        // ── HP ────────────────────────────────────────────────────────────────
        if (giveHP && hpReward > 0)
        {
            HealthSystem health = other.GetComponent<HealthSystem>();
            if (health != null)
                health.Heal(hpReward);
        }

        // ── Effects ───────────────────────────────────────────────────────────
        if (pickupParticles != null)
            Instantiate(pickupParticles, transform.position, Quaternion.identity);

        if (pickupSound != null)
            AudioSource.PlayClipAtPoint(pickupSound, transform.position);

        Destroy(gameObject);
    }
}