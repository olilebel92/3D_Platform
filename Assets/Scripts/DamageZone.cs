using UnityEngine;
using Unity.Netcode;

public class DamageZone : MonoBehaviour
{
    public int damageAmount = 1;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        // In MP, physics is simulated on every machine so this trigger can fire
        // on machines that don't own the entering player. Only the owning client
        // should request damage — HealthSystem auto-routes to the server.
        bool networkActive = NetworkManager.Singleton != null
                          && NetworkManager.Singleton.IsListening;
        if (networkActive)
        {
            NetworkObject playerNet = other.GetComponent<NetworkObject>();
            if (playerNet != null && !playerNet.IsOwner) return;
        }

        HealthSystem health = other.GetComponent<HealthSystem>();
        if (health != null)
            health.TakeDamage(damageAmount);
    }
}