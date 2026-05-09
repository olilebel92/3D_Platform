using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent singleton that plays background music across all scenes.
/// Starts after a 1 second delay with a 2 second fade in.
/// Fades out and stops when the GameScene is loaded.
/// Place this on a GameObject in your Menu scene — it will survive scene loads.
/// </summary>
public class MusicManager : MonoBehaviour
{
    [Header("Stop On Scene")]
    [Tooltip("Music fades out and stops when this scene is loaded.")]
    public string stopOnScene = "GameScene";

    [Tooltip("Seconds to fade out before stopping.")]
    public float fadeOutDuration = 1f;

    // ─── Singleton ────────────────────────────────────────────────────────────

    public static MusicManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Music")]
    [Tooltip("The background music clip to play.")]
    public AudioClip musicClip;

    [Header("Settings")]
    [Tooltip("Volume of the music at full (0 = silent, 1 = full).")]
    [Range(0f, 1f)]
    public float volume = 0.5f;

    [Tooltip("Seconds to wait before music starts.")]
    public float startDelay = 1f;

    [Tooltip("Seconds to fade from silent to full volume.")]
    public float fadeInDuration = 2f;

    // ─── Private State ────────────────────────────────────────────────────────

    private AudioSource _audioSource;

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

        SceneManager.sceneLoaded += OnSceneLoaded;

        // Load saved music volume so the fade-in targets the correct level
        volume = PlayerPrefs.GetFloat("vol_music", volume);

        // Set up AudioSource
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();

        _audioSource.clip = musicClip;
        _audioSource.loop = true;
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f;
        _audioSource.ignoreListenerPause = true;
        _audioSource.volume = 0f; // start silent, fade in

        // Apply master volume (AudioListener) from saved settings
        AudioListener.volume = PlayerPrefs.GetFloat("vol_master", 1f);
    }

    void Start()
    {
        if (musicClip == null)
        {
            Debug.LogWarning("[MusicManager] No AudioClip assigned!");
            return;
        }

        StartCoroutine(DelayedFadeIn());
    }

    // ─── Fade In ──────────────────────────────────────────────────────────────

    private IEnumerator DelayedFadeIn()
    {
        // Wait 1 second before starting
        yield return new WaitForSecondsRealtime(startDelay);

        _audioSource.Play();
        Debug.Log("[MusicManager] Music started, fading in...");

        // Fade from 0 to target volume over fadeInDuration
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _audioSource.volume = Mathf.Lerp(0f, volume, elapsed / fadeInDuration);
            yield return null;
        }

        _audioSource.volume = volume;
        Debug.Log("[MusicManager] Fade in complete.");
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Pause the music.</summary>
    public void Pause()
    {
        if (_audioSource != null && _audioSource.isPlaying)
            _audioSource.Pause();
    }

    /// <summary>Stop the music entirely.</summary>
    public void Stop()
    {
        if (_audioSource != null)
            _audioSource.Stop();
    }

    /// <summary>Set volume at runtime (0–1).</summary>
    public void SetVolume(float value)
    {
        volume = Mathf.Clamp01(value);
        if (_audioSource != null)
            _audioSource.volume = volume;
    }

    // ─── Scene Handling ───────────────────────────────────────────────────────

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.IsNullOrEmpty(stopOnScene) && scene.name == stopOnScene)
            StartCoroutine(FadeOutAndStop());
    }

    private IEnumerator FadeOutAndStop()
    {
        float startVolume = _audioSource.volume;
        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeOutDuration);
            yield return null;
        }

        _audioSource.Stop();
        _audioSource.volume = 0f;
        Debug.Log("[MusicManager] Music stopped for GameScene.");
    }
}