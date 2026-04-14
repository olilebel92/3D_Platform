using UnityEngine;

/// <summary>
/// Static settings store — reads/writes PlayerPrefs and applies audio settings.
/// No MonoBehaviour needed; call Apply() at startup from any scene entry point.
/// </summary>
public static class SettingsManager
{
    // ─── PlayerPrefs Keys ─────────────────────────────────────────────────────

    const string KeyMaster = "vol_master";
    const string KeyMusic  = "vol_music";

    // ─── Master Volume ────────────────────────────────────────────────────────

    /// <summary>Global volume multiplier (0–1). Affects all AudioSources via AudioListener.</summary>
    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat(KeyMaster, 1f);
        set
        {
            PlayerPrefs.SetFloat(KeyMaster, Mathf.Clamp01(value));
            AudioListener.volume = Mathf.Clamp01(value);
        }
    }

    // ─── Music Volume ─────────────────────────────────────────────────────────

    /// <summary>Music-only volume (0–1). Applied through MusicManager.</summary>
    public static float MusicVolume
    {
        get => PlayerPrefs.GetFloat(KeyMusic, 0.5f);
        set
        {
            PlayerPrefs.SetFloat(KeyMusic, Mathf.Clamp01(value));
            if (MusicManager.Instance != null)
                MusicManager.Instance.SetVolume(Mathf.Clamp01(value));
        }
    }

    // ─── Apply ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply all saved settings. Call this once at app start (e.g. from MusicManager.Awake)
    /// and again whenever entering a new scene that needs audio restored.
    /// </summary>
    public static void Apply()
    {
        AudioListener.volume = MasterVolume;

        if (MusicManager.Instance != null)
            MusicManager.Instance.SetVolume(MusicVolume);
    }
}
