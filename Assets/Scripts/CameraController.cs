using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Orbit Settings")]
    public float distance = 5f;
    public float heightOffset = 1.5f;

    [Header("Sensitivity")]
    public float mouseSensitivity = 0.2f;
    public float stickSensitivity = 100f;

    [Header("Clamp")]
    public float minPitch = -20f;
    public float maxPitch = 60f;

    private PlayerInputActions inputActions;
    private float yaw = 0f;
    private float pitch = 20f;

    void Awake()
    {
        inputActions = new PlayerInputActions();
    }

    void OnEnable() { inputActions.Player.Enable(); }
    void OnDisable() { inputActions.Player.Disable(); }

    void LateUpdate()
    {
        Vector2 lookInput = inputActions.Player.Look.ReadValue<Vector2>();

        // Check if input came from mouse or stick
        bool usingMouse = Mouse.current != null &&
                          Mouse.current.delta.ReadValue().sqrMagnitude > 0.1f;

        float sensitivity = usingMouse ?
                            mouseSensitivity :
                            stickSensitivity * Time.deltaTime;

        yaw += lookInput.x * sensitivity;
        pitch -= lookInput.y * sensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        if (target == null) return;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -distance);
        transform.position = target.position + Vector3.up * heightOffset + offset;
        transform.LookAt(target.position + Vector3.up * heightOffset);
    }
}