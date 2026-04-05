/// <summary>
/// Central pause counter. Multiple panels can request a pause independently;
/// the game only resumes when every panel that paused has released.
///
/// In a multiplayer session Time.timeScale is never touched — pausing one
/// client must not desync the server. Cursor visibility is still managed
/// normally so players can interact with their panels.
///
/// Usage:
///   PauseManager.RequestPause();   // call when a panel opens
///   PauseManager.ReleasePause();   // call when a panel closes
/// </summary>
public static class PauseManager
{
    private static int _pauseCount = 0;

    // Reset static state when a new scene loads so the count never leaks.
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => _pauseCount = 0;

    /// <summary>True whenever any panel has an active pause request.</summary>
    public static bool IsPaused => _pauseCount > 0;

    /// <summary>
    /// True when a networked multiplayer session is active.
    /// In this mode Time.timeScale is left at 1 so all clients stay in sync.
    /// </summary>
    private static bool IsMultiplayer =>
        Unity.Netcode.NetworkManager.Singleton != null &&
        Unity.Netcode.NetworkManager.Singleton.IsListening;

    public static void RequestPause()
    {
        _pauseCount++;

        if (!IsMultiplayer)
            UnityEngine.Time.timeScale = 0f;

        UnityEngine.Debug.Log($"[PauseManager] Paused (multiplayer={IsMultiplayer}). Active requests: " + _pauseCount);
    }

    public static void ReleasePause()
    {
        _pauseCount = UnityEngine.Mathf.Max(0, _pauseCount - 1);

        if (_pauseCount == 0)
        {
            if (!IsMultiplayer)
                UnityEngine.Time.timeScale = 1f;

            UnityEngine.Debug.Log($"[PauseManager] Resumed (multiplayer={IsMultiplayer}).");
        }
        else
        {
            UnityEngine.Debug.Log("[PauseManager] Release — still paused. Active requests: " + _pauseCount);
        }
    }
}
