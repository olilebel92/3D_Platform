using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;          // InputAction.WasPressedThisFrame() extension
using UnityEngine.Profiling;

public class UI_FPSCounter : MonoBehaviour
{
    [Header("UI")]
    public GameObject panel;             // Parent panel (background)
    public TextMeshProUGUI fpsText;      // TMP text

    [Header("Settings")]
    public float updateInterval = 0.5f;
    public bool showMilliseconds = true;

    [Header("Smoothing")]
    [Range(0.01f, 1f)]
    public float smoothFactor = 0.1f;

    private float time = 0f;
    private int frames = 0;
    private float smoothedFPS = 0f;

    void Start()
    {
        if (panel != null)
            panel.SetActive(true); // Start visible
    }

    void Update()
    {
        // Toggle panel with F3 (UI.ToggleFPS action — bound in PlayerInputActions.inputactions)
        if (panel != null && InputManager.UI.ToggleFPS.WasPressedThisFrame())
            panel.SetActive(!panel.activeSelf);

        // Stop if hidden or missing refs
        if (panel == null || !panel.activeSelf || fpsText == null)
            return;

        frames++;
        time += Time.deltaTime;

        if (time >= updateInterval)
        {
            float fps = (time > 0f) ? (frames / time) : 0f;
            smoothedFPS = Mathf.Lerp(smoothedFPS, fps, smoothFactor);

            UpdateDisplay(smoothedFPS);

            frames = 0;
            time = 0f;
        }
    }

    void UpdateDisplay(float fps)
    {
        if (float.IsNaN(fps) || float.IsInfinity(fps) || fps < 0f)
            fps = 0f;

        float ms = (fps > 0f) ? (1000f / fps) : 0f;
        float memory = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);

        if (showMilliseconds)
            fpsText.text = $"{Mathf.RoundToInt(fps)} FPS\n{ms:0.0} ms\n{memory:0} MB";
        else
            fpsText.text = $"{Mathf.RoundToInt(fps)} FPS\n{memory:0} MB";

        if (fps >= 55f)
            fpsText.color = Color.green;
        else if (fps >= 30f)
            fpsText.color = Color.yellow;
        else
            fpsText.color = Color.red;
    }
}