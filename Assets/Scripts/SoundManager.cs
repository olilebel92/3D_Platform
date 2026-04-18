using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Persistent singleton that provides UI and SFX audio channels.
/// Attach to a SoundManager GameObject in your Menu scene.
/// Route SFX and UI AudioSources to their respective mixer groups in the Inspector.
/// </summary>
public class SoundManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static SoundManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Audio Sources")]
    [Tooltip("AudioSource routed to the SFX mixer group.")]
    [SerializeField] private AudioSource sfxSource;

    [Tooltip("AudioSource routed to the UI mixer group.")]
    [SerializeField] private AudioSource uiSource;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Play a sound effect clip.</summary>
    public void PlaySFX(AudioClip clip)
    {
        if (clip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(clip);
    }

    /// <summary>Play a UI sound clip.</summary>
    public void PlayUI(AudioClip clip)
    {
        if (clip == null || uiSource == null) return;
        uiSource.PlayOneShot(clip);
    }
}
