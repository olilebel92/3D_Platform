using UnityEngine;

public class ItemSpawner : MonoBehaviour
{
    [Header("Spawning")]
    public GameObject itemPrefab;       // Drag your item prefab here
    public float spawnInterval = 4f;    // Seconds between each spawn
    public int maxItems = 10;           // Cap so the scene doesn't get flooded

    [Header("Spawn Area")]
    public float areaWidth  = 20f;      // X range around spawner: -areaWidth  to +areaWidth
    public float areaLength = 20f;      // Z range around spawner: -areaLength to +areaLength

    [Tooltip("Fixed world-space Y the raycast starts from. Must be above the highest point of your terrain.")]
    public float raycastOriginY = 10000f;

    private float timer = 0f;
    private int currentItemCount = 0;

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= spawnInterval)
        {
            timer = 0f;
            if (currentItemCount < maxItems)
                SpawnItem();
        }
    }

    void SpawnItem()
    {
        // Pick a random X/Z position within the defined area
        float randomX = Random.Range(-areaWidth, areaWidth);
        float randomZ = Random.Range(-areaLength, areaLength);

        Vector3 rayOrigin = new Vector3(
            transform.position.x + randomX,
            raycastOriginY,
            transform.position.z + randomZ);

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, raycastOriginY * 2f))
        {
            // Spawn slightly above the hit point so it sits on top
            Vector3 spawnPos = hit.point + Vector3.up * 0.5f;
            GameObject item = Instantiate(itemPrefab, spawnPos, Quaternion.identity);

            // Give the item a reference back to this spawner
            PickupItem pickup = item.GetComponent<PickupItem>();
            if (pickup != null)
                pickup.spawner = this;

            currentItemCount++;
        }
        else
        {
            Debug.LogWarning("[ItemSpawner] Raycast missed terrain at " + rayOrigin +
                             " — increase Raycast Origin Y in the Inspector if your terrain is tall.");
        }
    }

    // Called by PickupItem when collected
    public void ItemCollected()
    {
        currentItemCount--;
    }
}