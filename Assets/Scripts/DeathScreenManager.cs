using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using TMPro;
using UnityEngine.UI;

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

    [Tooltip("Optional TMP label on the death panel set to 'You survived X waves!' on MP game-over.")]
    public TMP_Text gameOverLabel;

    [Tooltip("Optional Restart button. In multiplayer it is disabled for non-host clients (only the host can restart).")]
    public Button restartButton;

    [Header("Spectator (Multiplayer co-op)")]
    [Tooltip("Overlay shown while spectating teammates after death. Prev/Next buttons + 'Spectating <name>' label. Default inactive.")]
    public GameObject spectatorOverlayPanel;

    [Tooltip("TMP label on the spectator overlay showing the watched player's name.")]
    public TMP_Text spectatingNameLabel;

    // ─── Private State ────────────────────────────────────────────────────────

    private CharacterWindow _characterWindow;
    private InventoryUI _inventoryUI;
    private SpectatorController _spectator;

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
        if (spectatorOverlayPanel != null)
            spectatorOverlayPanel.SetActive(false);

        _spectator = GetComponent<SpectatorController>();
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

        // Populate the survived-waves label (solo path — MP uses ShowGameOverScreen).
        if (gameOverLabel != null)
        {
            int waves = WaveManager.Instance != null ? WaveManager.Instance.CurrentWave : 0;
            gameOverLabel.text = "You survived " + waves + " wave" + (waves != 1 ? "s" : "") + "!";
        }

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
    /// Called by the Restart button on the game-over screen.
    /// Solo   → reloads the active scene.
    /// Multi  → asks the host to reload the scene for the whole session (fresh run).
    /// </summary>
    public void OnRestartButton()
    {
        if (IsMultiplayer)
        {
            // Only the host may restart the session (it owns the networked scene load).
            // Non-host clicks are ignored even if their button somehow fires.
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Debug.Log("[DeathScreenManager] Restart ignored — only the host can restart.");
                return;
            }

            // Game-over (all players down): reload the scene for everyone via the host.
            if (deathScreenPanel != null)
                deathScreenPanel.SetActive(false);

            if (WaveManager.Instance != null)
                WaveManager.Instance.RequestRestartServerRpc();
            else
                Debug.LogWarning("[DeathScreenManager] No WaveManager — cannot request restart.");
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

    // ─── Spectator Mode (Multiplayer co-op) ────────────────────────────────────

    /// <summary>
    /// Called (owner-targeted) when this player dies but teammates are still alive.
    /// No fade, no timeScale change — freeze our body and follow a living teammate.
    /// </summary>
    public void EnterSpectatorMode()
    {
        Debug.Log("[DeathScreenManager] Entering spectator mode.");

        // Close any open panels.
        if (_characterWindow != null) _characterWindow.CloseWindow();
        if (_inventoryUI != null)     _inventoryUI.CloseInventory();
        if (SkillTreeManager.Instance != null) SkillTreeManager.Instance.CloseWindow();

        // Freeze the local (dead) player's body.
        PlayerController local = FindLocalOwner();
        if (local != null) local.SetSpectating(true);

        // Free the cursor so the Prev/Next overlay buttons are clickable.
        CursorManager.Instance?.OpenMenu();

        if (_spectator != null)
            _spectator.Begin();
        else
            Debug.LogWarning("[DeathScreenManager] No SpectatorController on this GameObject — cannot spectate.");
    }

    /// <summary>
    /// Called on auto-respawn (from PlayerController.RespawnAtPositionClientRpc): stop
    /// spectating, hide the overlay, and hand the camera back to our own player.
    /// </summary>
    public void ExitSpectatorMode()
    {
        if (_spectator != null) _spectator.End();

        if (spectatorOverlayPanel != null)
            spectatorOverlayPanel.SetActive(false);

        PlayerController local = FindLocalOwner();
        if (local != null)
            SpectatorController.RetargetCamera(local.transform);
    }

    /// <summary>
    /// Called (broadcast) when every player is down. Shows the "You survived X waves"
    /// screen with the Restart button. MP never touches Time.timeScale.
    /// </summary>
    public void ShowGameOverScreen(int waves)
    {
        Debug.Log("[DeathScreenManager] Game over — all players down.");

        // Make sure any spectator UI is torn down first.
        ExitSpectatorMode();

        if (_characterWindow != null) _characterWindow.CloseWindow();
        if (_inventoryUI != null)     _inventoryUI.CloseInventory();
        if (SkillTreeManager.Instance != null) SkillTreeManager.Instance.CloseWindow();

        // Only the host can restart — disable the button for everyone else.
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        if (restartButton != null)
            restartButton.interactable = isHost;

        if (gameOverLabel != null)
            gameOverLabel.text = "You survived " + waves + " wave" + (waves != 1 ? "s" : "") + "!"
                               + (isHost ? "" : "\n<size=60%>Waiting for host to restart…</size>");

        CursorManager.Instance?.OpenMenu();

        if (SceneTransitionManager.Instance != null)
            SceneTransitionManager.Instance.FadeOutThen(OnFadeComplete);
        else
            OnFadeComplete();
    }

    /// <summary>Finds the locally-owned PlayerController (multiplayer).</summary>
    static PlayerController FindLocalOwner()
    {
        foreach (PlayerController pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            if (pc.IsOwner) return pc;
        return null;
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
