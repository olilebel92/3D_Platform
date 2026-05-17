using UnityEngine;

// Automatically added to each spawned enemy.
// Notifies EnemySpawner and/or WaveManager when this enemy is destroyed.
public class EnemyTracker : MonoBehaviour
{
    [HideInInspector]
    public EnemySpawner spawner;

    [HideInInspector]
    public WaveManager waveManager;

    void Awake()
    {
        HealthSystem health = GetComponent<HealthSystem>();
        if (health != null) health.OnDeath += NotifyWaveDeath;
    }

    private void NotifyWaveDeath()
    {
        if (waveManager != null) waveManager.EnemyDestroyed();
    }

    void OnDestroy()
    {
        if (spawner != null)
            spawner.EnemyDestroyed();
    }
}