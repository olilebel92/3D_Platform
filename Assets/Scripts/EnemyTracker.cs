using UnityEngine;

// Automatically added to each spawned enemy.
// Notifies EnemySpawner and/or WaveManager when this enemy is destroyed.
public class EnemyTracker : MonoBehaviour
{
    [HideInInspector]
    public EnemySpawner spawner;

    [HideInInspector]
    public WaveManager waveManager;

    void OnDestroy()
    {
        if (spawner != null)
            spawner.EnemyDestroyed();

        if (waveManager != null)
            waveManager.EnemyDestroyed();
    }
}