using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Displays a character stats window when the player presses C.
/// Reads live values from HealthSystem, StaminaSystem, and ExperienceManager.
///
/// Setup:
///   1. Create a Canvas panel for the window and assign it to windowRoot.
///   2. Add TextMeshProUGUI labels inside the panel and wire them in the Inspector.
///   3. Add this component anywhere in the scene (e.g. on the Player or a UI Manager object).
///   4. Optionally assign playerHealthSystem and playerStaminaSystem, or let it auto-find by tag.
/// </summary>
public class CharacterWindow : MonoBehaviour
{
    // ─── Inspector — Panel Root ───────────────────────────────────────────────

    [Header("Window Root")]
    [Tooltip("The top-level panel GameObject to show/hide.")]
    public GameObject windowRoot;

    // ─── Inspector — Player Component References ──────────────────────────────

    [Header("Player References (auto-found if blank)")]
    [Tooltip("HealthSystem on the Player. Leave blank to auto-find by 'Player' tag.")]
    public HealthSystem playerHealthSystem;

    [Tooltip("StaminaSystem on the Player. Leave blank to auto-find by 'Player' tag.")]
    public StaminaSystem playerStaminaSystem;

    // ─── Inspector — Stat Labels ──────────────────────────────────────────────

    [Header("Stat Labels — drag your TMP labels here")]
    [Tooltip("Shows the character's current level.")]
    public TextMeshProUGUI levelLabel;

    [Tooltip("Shows current XP and XP required for next level.")]
    public TextMeshProUGUI xpLabel;

    [Tooltip("Shows current and max HP.")]
    public TextMeshProUGUI hpLabel;

    [Tooltip("Shows max stamina pool.")]
    public TextMeshProUGUI staminaLabel;

    [Tooltip("Shows the Strength stat.")]
    public TextMeshProUGUI strengthLabel;

    [Tooltip("Shows the XP scaling factor (how fast XP requirements grow).")]
    public TextMeshProUGUI xpScalingLabel;

    [Tooltip("Shows attack damage (sourced from PlayerAttack if present).")]
    public TextMeshProUGUI attackDamageLabel;

    [Tooltip("Shows attack radius (sourced from PlayerAttack if present).")]
    public TextMeshProUGUI attackRadiusLabel;

    [Tooltip("Shows move speed (sourced from PlayerController if present).")]
    public TextMeshProUGUI moveSpeedLabel;

    [Tooltip("Shows sprint speed (sourced from PlayerController if present).")]
    public TextMeshProUGUI sprintSpeedLabel;

    // ─── Inspector — Optional XP Bar ─────────────────────────────────────────

    [Header("Optional XP Progress Bar")]
    [Tooltip("A Slider used as a mini XP bar inside the character window.")]
    public Slider xpProgressBar;

    // ─── Inspector — Cursor Behaviour ────────────────────────────────────────

    [Header("Cursor")]
    [Tooltip("Unlock and show the cursor while the character window is open.")]
    public bool unlockCursorWhileOpen = true;

    // ─── Private State ────────────────────────────────────────────────────────

    private bool _isOpen = false;

    // Optional extra component refs (auto-found from player)
    private PlayerAttack _playerAttack;
    private PlayerController _playerController;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        // Auto-find player components if not assigned
        if (playerHealthSystem == null || playerStaminaSystem == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                if (playerHealthSystem == null)
                    playerHealthSystem = playerObj.GetComponent<HealthSystem>();

                if (playerStaminaSystem == null)
                    playerStaminaSystem = playerObj.GetComponent<StaminaSystem>();

                _playerAttack = playerObj.GetComponent<PlayerAttack>();
                _playerController = playerObj.GetComponent<PlayerController>();
            }
            else
            {
                Debug.LogWarning("[CharacterWindow] No GameObject with tag 'Player' found!");
            }
        }
        else
        {
            // If manually assigned, still grab the optional components from the same object
            _playerAttack = playerHealthSystem.GetComponent<PlayerAttack>();
            _playerController = playerHealthSystem.GetComponent<PlayerController>();
        }

        // Make sure the window starts hidden
        if (windowRoot != null)
            windowRoot.SetActive(false);
    }

    void Update()
    {
        // Toggle on C key press (New Input System — no need to touch PlayerInputActions)
        if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            ToggleWindow();

        // Live refresh while the window is open
        if (_isOpen)
            RefreshStats();
    }

    // ─── Toggle ───────────────────────────────────────────────────────────────

    private void ToggleWindow()
    {
        _isOpen = !_isOpen;

        if (windowRoot != null)
            windowRoot.SetActive(_isOpen);

        if (_isOpen)
            RefreshStats();

        // Cursor management
        if (unlockCursorWhileOpen)
        {
            Cursor.lockState = _isOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = _isOpen;
        }

        Debug.Log("[CharacterWindow] Window " + (_isOpen ? "opened." : "closed."));
    }

    // ─── Stat Refresh ─────────────────────────────────────────────────────────

    /// <summary>
    /// Pulls the latest values from all systems and writes them to the TMP labels.
    /// Called every time the window is opened. You can also call this manually
    /// if you want the window to update in real-time while open.
    /// </summary>
    public void RefreshStats()
    {
        ExperienceManager xp = ExperienceManager.Instance;

        // ── Level & XP ────────────────────────────────────────────────────────
        if (xp != null)
        {
            SetLabel(levelLabel, $"Level", xp.currentLevel.ToString());
            SetLabel(xpLabel, $"Experience", $"{xp.currentXP} / {xp.xpToNextLevel} XP");
            SetLabel(strengthLabel, $"Strength", xp.strength.ToString());
            SetLabel(xpScalingLabel, $"XP Growth", $"×{xp.xpScalingFactor:F2} per level");

            if (xpProgressBar != null)
                xpProgressBar.value = (float)xp.currentXP / xp.xpToNextLevel;
        }
        else
        {
            Debug.LogWarning("[CharacterWindow] ExperienceManager instance not found!");
        }

        // ── Health ────────────────────────────────────────────────────────────
        if (playerHealthSystem != null)
        {
            SetLabel(hpLabel, "Max HP", playerHealthSystem.maxHealth.ToString());
        }
        else
        {
            Debug.LogWarning("[CharacterWindow] HealthSystem not found on player!");
        }

        // ── Stamina ───────────────────────────────────────────────────────────
        if (playerStaminaSystem != null)
        {
            SetLabel(staminaLabel, "Max Stamina", playerStaminaSystem.maxStamina.ToString("F0"));
        }
        else
        {
            Debug.LogWarning("[CharacterWindow] StaminaSystem not found on player!");
        }

        // ── Combat (PlayerAttack) ─────────────────────────────────────────────
        if (_playerAttack != null)
        {
            // Attack damage respects the STR bonus from ExperienceManager (same logic as PlayerAttack)
            int effectiveDamage = (xp != null) ? xp.strength : _playerAttack.attackDamage;
            SetLabel(attackDamageLabel, "Attack Damage", effectiveDamage.ToString());
            SetLabel(attackRadiusLabel, "Attack Radius", _playerAttack.attackRadius.ToString("F1") + " m");
        }

        // ── Movement (PlayerController) ───────────────────────────────────────
        if (_playerController != null)
        {
            SetLabel(moveSpeedLabel, "Move Speed", _playerController.moveSpeed.ToString("F1"));
            SetLabel(sprintSpeedLabel, "Sprint Speed", _playerController.sprintSpeed.ToString("F1"));
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes "Label Name: value" to a TMP label, safely handling null references.
    /// </summary>
    private void SetLabel(TextMeshProUGUI label, string statName, string value)
    {
        if (label == null) return;
        label.text = $"{statName}:  {value}";
    }
}