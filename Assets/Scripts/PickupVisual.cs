using UnityEngine;

/// <summary>
/// Pure visual behaviour for ground pickups — bob and spin only.
/// Pair with LootPickup on the same prefab to handle rewards and collection.
/// No trigger logic here; LootPickup owns the collection flow.
/// </summary>
public class PickupVisual : MonoBehaviour
{
    [Header("Spin")]
    [Tooltip("Degrees per second the pickup rotates around its Y axis.")]
    public float rotateSpeed = 90f;

    [Header("Bob")]
    [Tooltip("Height of the up/down bob in world units.")]
    public float bobHeight = 0.3f;

    [Tooltip("Speed of the bob cycle.")]
    public float bobSpeed = 2f;

    // ─── Internal ─────────────────────────────────────────────────────────────
    private Vector3 _startPosition;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    void Start()
    {
        _startPosition = transform.position;
    }

    void Update()
    {
        // Spin around Y axis
        transform.Rotate(0f, rotateSpeed * Time.deltaTime, 0f);

        // Bob up and down using a sine wave
        float newY = _startPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }
}
