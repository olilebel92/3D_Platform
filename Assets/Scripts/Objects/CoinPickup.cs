using UnityEngine;

/// <summary>
/// Attach this to your Coin prefab.
/// The coin awards XP on pickup and destroys itself.
/// </summary>
public class CoinPickup : MonoBehaviour
{
    [Header("Coin Settings")]
    public int xpReward = 25;              // XP granted when collected
    public string playerTag = "Player";    // Tag on the player GameObject

    [Header("Effects (optional)")]
    public GameObject pickupParticles;     // Drag a particle prefab here if you have one
    public AudioClip pickupSound;          // Drag an AudioClip here if you have one

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        // Award XP
        if (ExperienceManager.Instance != null)
        {
            ExperienceManager.Instance.GainXP(xpReward);
        }
        else
        {
            Debug.LogWarning("[CoinPickup] ExperienceManager instance not found in scene!");
        }

        // Spawn particles
        if (pickupParticles != null)
            Instantiate(pickupParticles, transform.position, Quaternion.identity);

        // Play sound at position (survives object destruction)
        if (pickupSound != null)
            AudioSource.PlayClipAtPoint(pickupSound, transform.position);

        // Destroy the coin
        Destroy(gameObject);
    }
}