using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

/// <summary>
/// Handles the YOU DIED death screen.
///
/// Setup:
///   1. Add this script to the same Canvas as SceneTransitionManager (or any persistent GameObject).
///   2. Create a child Panel with a "YOU DIED" TMP label and a Restart Button.
///   3. Drag that Panel into the deathScreenPanel slot.
///   4. Wire the Restart Button's OnClick to DeathScreenManager.OnRestartButton().
/// </summary>
public class DeathScreenManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static DeathScreenManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Death Screen")]
    [Tooltip("Root panel containing the YOU DIED text and Restart button.")]
    public GameObject deathScreenPanel;

    [Tooltip("Seconds to wait after the fade before showing the death screen.")]
    public float showDelay = 0.2f;

    // ─── Private State ────────────────────────────────────────────────────────

    private CharacterWindow _characterWindow;
    private InventoryUI _inventoryUI;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (deathScreenPanel != null)
            deathScreenPanel.SetActive(false);
    }

    void Start()
    {
        _characterWindow = FindFirstObjectByType<CharacterWindow>();
        _inventoryUI     = FindFirstObjectByType<InventoryUI>();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    // ─── Helpers ──────────────────────────────────────────────────────────────

    static bool IsMultiplayer =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the player dies. Fades to black then reveals the death screen.
    /// In multiplayer Time.timeScale is never touched — only one client must not
    /// freeze the whole server.
    /// </summary>
    public void ShowDeathScreen()
    {
        Debug.Log("[DeathScreenManager] Player died — triggering death screen.");

        // Close any open panels
        if (_characterWindow != null) _characterWindow.CloseWindow();
        if (_inventoryUI != null)     _inventoryUI.CloseInventory();
        if (SkillTreeManager.Instance != null) SkillTreeManager.Instance.CloseWindow();

        // Solo only: freeze time
        if (!IsMultiplayer)
            Time.timeScale = 0f;

        CursorManager.Instance?.OpenMenu();

        if (SceneTransitionManager.Instance != null)
            SceneTransitionManager.Instance.FadeOutThen(OnFadeComplete);
        else
        {
            Debug.LogWarning("[DeathScreenManager] SceneTransitionManager not found — skipping fade.");
            OnFadeComplete();
        }
    }

    /// <summary>
    /// Called by the Restart / Respawn button.
    /// Solo   → reloads the active scene.
    /// Multi  → asks the server to respawn the local player in-place (no scene reload).
    /// </summary>
    public void OnRestartButton()
    {
        if (IsMultiplayer)
        {
            // Hide the death panel — the screen stays black until the server replies
            // and RespawnClientRpc fades back in.
            if (deathScreenPanel != null)
                deathScreenPanel.SetActive(false);

            // Request the server to heal + teleport us back to a spawn point.
            PlayerController localPlayer = null;
            foreach (PlayerController pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (pc.IsOwner) { localPlayer = pc; break; }
            }

            if (localPlayer != null)
            {
                localPlayer.RespawnServerRpc();
            }
            else
            {
                Debug.LogWarning("[DeathScreenManager] Could not find local PlayerController to respawn.");
            }
            return;
        }

        // Solo: restore time and reload scene
        Time.timeScale = 1f;
        int currentScene = SceneManager.GetActiveScene().buildIndex;

        if (SceneTransitionManager.Instance != null)
            SceneTransitionManager.Instance.LoadSceneAlreadyFaded(currentScene);
        else
            SceneManager.LoadScene(currentScene);
    }

    // ─── Internal ─────────────────────────────────────────────────────────────

    private void OnFadeComplete()
    {
        if (showDelay > 0f)
            StartCoroutine(ShowAfterDelay());
        else if (deathScreenPanel != null)
            deathScreenPanel.SetActive(true);
    }

    private System.Collections.IEnumerator ShowAfterDelay()
    {
        yield return new WaitForSecondsRealtime(showDelay);
        if (deathScreenPanel != null)
            deathScreenPanel.SetActive(true);
    }
}
