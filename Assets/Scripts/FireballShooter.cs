using UnityEngine;
using UnityEngine.InputSystem;

public class FireballShooter : MonoBehaviour
{
    [Header("Fireball Setup")]
    public GameObject fireballPrefab;
    public Transform firePoint;

    private PlayerInputActions inputActions;

    void Awake()
    {
        inputActions = new PlayerInputActions();
    }

    void OnEnable() { inputActions.Player.Enable(); }
    void OnDisable() { inputActions.Player.Disable(); }

    void Update()
    {
        if (inputActions.Player.Fire.WasPerformedThisFrame())
            Shoot();
    }

    void Shoot()
    {
        if (fireballPrefab == null)
        {
            Debug.LogWarning("[FireballShooter] No fireball prefab assigned!");
            return;
        }

        Transform spawnTransform = firePoint != null ? firePoint : transform;
        Instantiate(fireballPrefab, spawnTransform.position, spawnTransform.rotation);
        Debug.Log("[FireballShooter] Fired!");
    }
}