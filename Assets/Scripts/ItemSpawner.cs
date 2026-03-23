using UnityEngine;

public class ItemSpawner : MonoBehaviour
{
    [Header("Spawning")]
    public GameObject itemPrefab;       // Drag your item prefab here
    public float spawnInterval = 4f;    // Seconds between each spawn
    public int maxItems = 10;           // Cap so the scene doesn't get flooded

    [Header("Spawn Area (flat area, no terrain raycast needed)")]
    public float areaWidth = 20f;       // X range: -areaWidth to +areaWidth
    public float areaLength = 20f;      // Z range: -areaLength to +areaLength
    public float spawnHeight = 50f;     // Raycast shoots down from this height

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

        Vector3 rayOrigin = new Vector3(randomX, spawnHeight, randomZ);

        // Raycast downward to land exactly on the terrain surface
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, spawnHeight + 10f))
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
    }

    // Called by PickupItem when collected
    public void ItemCollected()
    {
        currentItemCount--;
    }
}