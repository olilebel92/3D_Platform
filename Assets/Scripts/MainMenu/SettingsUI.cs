using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Wires the Settings panel sliders to SettingsManager.
/// Attach to the root of your Settings panel GameObject.
/// </summary>
public class SettingsUI : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Volume Sliders")]
    [Tooltip("Slider for overall game volume (AudioListener).")]
    [SerializeField] private Slider masterSlider;

    [Tooltip("Slider for background music volume (MusicManager).")]
    [SerializeField] private Slider musicSlider;

    [Header("Labels (optional)")]
    [Tooltip("TMP label that shows the current master volume percentage.")]
    [SerializeField] private TMP_Text masterLabel;

    [Tooltip("TMP label that shows the current music volume percentage.")]
    [SerializeField] private TMP_Text musicLabel;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        if (masterSlider != null)
        {
            masterSlider.minValue = 0f;
            masterSlider.maxValue = 1f;
            masterSlider.value    = SettingsManager.MasterVolume;
            masterSlider.onValueChanged.AddListener(OnMasterChanged);
            UpdateLabel(masterLabel, masterSlider.value);
        }

        if (musicSlider != null)
        {
            musicSlider.minValue = 0f;
            musicSlider.maxValue = 1f;
            musicSlider.value    = SettingsManager.MusicVolume;
            musicSlider.onValueChanged.AddListener(OnMusicChanged);
            UpdateLabel(musicLabel, musicSlider.value);
        }
    }

    void OnDestroy()
    {
        if (masterSlider != null) masterSlider.onValueChanged.RemoveListener(OnMasterChanged);
        if (musicSlider  != null) musicSlider.onValueChanged.RemoveListener(OnMusicChanged);
    }

    // ─── Slider Callbacks ─────────────────────────────────────────────────────

    void OnMasterChanged(float value)
    {
        SettingsManager.MasterVolume = value;
        UpdateLabel(masterLabel, value);
    }

    void OnMusicChanged(float value)
    {
        SettingsManager.MusicVolume = value;
        UpdateLabel(musicLabel, value);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    static void UpdateLabel(TMP_Text label, float value)
    {
        if (label != null)
            label.text = Mathf.RoundToInt(value * 100f) + "%";
    }
}
