using UnityEngine;

// Automatically added to each spawned enemy by EnemySpawner
// Notifies the spawner when this enemy is destroyed
public class EnemyTracker : MonoBehaviour
{
    [HideInInspector]
    public EnemySpawner spawner;

    void OnDestroy()
    {
        if (spawner != null)
            spawner.EnemyDestroyed();
    }
}