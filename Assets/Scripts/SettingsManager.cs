using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Static settings store — reads/writes PlayerPrefs and applies audio settings.
/// No MonoBehaviour needed; call Apply() at startup from any scene entry point.
/// </summary>
public static class SettingsManager
{
    // ─── PlayerPrefs Keys ─────────────────────────────────────────────────────

    const string KeyMaster    = "vol_master";
    const string KeyMusic     = "vol_music";
    const string KeySfx       = "vol_sfx";
    const string KeyUi        = "vol_ui";
    const string KeyFramerate = "framerate";

    // ─── Allowed Framerate Values ─────────────────────────────────────────────

    /// <summary>Values exposed to the player in the FPS-cap dropdown. -1 = uncapped.</summary>
    public static readonly int[] AllowedFramerates = { 30, 60, 120, -1 };

    // ─── Mixer Reference ──────────────────────────────────────────────────────

    /// <summary>Assigned by SettingsUI on Start. Required for SFX and UI volume control.</summary>
    public static AudioMixer Mixer;

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

    // ─── SFX Volume ───────────────────────────────────────────────────────────

    /// <summary>Sound effects volume (0–1). Applied through the SFX mixer group.</summary>
    public static float SfxVolume
    {
        get => PlayerPrefs.GetFloat(KeySfx, 1f);
        set
        {
            PlayerPrefs.SetFloat(KeySfx, Mathf.Clamp01(value));
            SetMixerVolume("SFXVolume", Mathf.Clamp01(value));
        }
    }

    // ─── UI Volume ────────────────────────────────────────────────────────────

    /// <summary>UI sounds volume (0–1). Applied through the UI mixer group.</summary>
    public static float UiVolume
    {
        get => PlayerPrefs.GetFloat(KeyUi, 1f);
        set
        {
            PlayerPrefs.SetFloat(KeyUi, Mathf.Clamp01(value));
            SetMixerVolume("UIVolume", Mathf.Clamp01(value));
        }
    }

    // ─── Framerate Cap ────────────────────────────────────────────────────────

    /// <summary>Target framerate cap. -1 = uncapped. VSync is forced off when applied.</summary>
    public static int FramerateCap
    {
        get => PlayerPrefs.GetInt(KeyFramerate, 60);
        set
        {
            PlayerPrefs.SetInt(KeyFramerate, value);
            ApplyFramerate(value);
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

        SetMixerVolume("SFXVolume", SfxVolume);
        SetMixerVolume("UIVolume",  UiVolume);

        ApplyFramerate(FramerateCap);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    static void SetMixerVolume(string param, float linear)
    {
        if (Mixer == null) return;
        float db = linear > 0f ? Mathf.Log10(linear) * 20f : -80f;
        Mixer.SetFloat(param, db);
    }

    static void ApplyFramerate(int fps)
    {
        // VSync silently overrides targetFrameRate when > 0, so always disable it.
        QualitySettings.vSyncCount   = 0;
        Application.targetFrameRate  = fps;
    }
}
