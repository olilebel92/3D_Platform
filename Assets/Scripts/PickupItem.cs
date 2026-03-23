using UnityEngine;

public class PickupItem : MonoBehaviour
{
    [Header("Visual Feedback")]
    public float rotateSpeed = 90f;     // Spins so it's easy to spot
    public float bobHeight = 0.3f;      // Bobs up and down
    public float bobSpeed = 2f;

    [HideInInspector]
    public ItemSpawner spawner;         // Set automatically by the spawner

    private Vector3 startPosition;

    void Start()
    {
        startPosition = transform.position;
    }

    void Update()
    {
        // Rotate around Y axis
        transform.Rotate(0f, rotateSpeed * Time.deltaTime, 0f);

        // Bob up and down using a sine wave
        float newY = startPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }

    // Triggered when the player walks through the item
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (spawner != null)
                spawner.ItemCollected();

            Destroy(gameObject);
        }
    }
}