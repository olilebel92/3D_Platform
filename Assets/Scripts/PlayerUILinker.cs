using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Attach this to your HUD Canvas (or any scene GameObject).
/// Wire all UI fields here ONCE in the Inspector — they stay on the Canvas
/// and never break, even when the Player is a spawnable prefab.
///
/// At runtime this script waits for the locally owned PlayerController to spawn,
/// then pushes references into StaminaSystem, HealthSystem, and CharacterWindow.
/// Works for both solo play and networked sessions.
/// </summary>
public class PlayerUILinker : MonoBehaviour
{
    // ─── Stamina UI ───────────────────────────────────────────────────────────

    [Header("Stamina UI — drag from your Canvas")]
    [Tooltip("The fill Image on the stamina bar.")]
    public Image staminaBarFill;

    [Tooltip("The root panel that shows/hides the stamina bar.")]
    public GameObject staminaBarPanel;

    // ─── Mana UI ──────────────────────────────────────────────────────────────

    [Header("Mana UI — drag from your Canvas")]
    [Tooltip("The fill Image on the mana bar.")]
    public Image manaBarFill;

    [Tooltip("The lighter-blue reserved-mana overlay Image. Sibling of manaBarFill, rendered behind it. Optional — leave empty to disable the preview chunk.")]
    public Image manaBarReservedFill;

    [Tooltip("The root panel that shows/hides the mana bar.")]
    public GameObject manaBarPanel;

    // ─── XP UI ────────────────────────────────────────────────────────────────

    [Header("XP UI — drag from your Canvas")]
    [Tooltip("The Slider used as the XP bar.")]
    public Slider xpBar;

    [Tooltip("Label showing the current level (e.g. 'Level 3').")]
    public TextMeshProUGUI levelText;

    [Tooltip("Label showing current XP / XP needed (e.g. '40 / 100 XP').")]
    public TextMeshProUGUI xpText;

    // ─── Spell Cast Bar UI ────────────────────────────────────────────────────

    [Header("Spell Cast Bar UI — drag from your Canvas")]
    [Tooltip("The SpellCastBarUI component on the HUD Canvas.")]
    public SpellCastBarUI spellCastBarUI;

    // ─── Nameplate UI ─────────────────────────────────────────────────────────

    [Header("Nameplate UI — drag from your Canvas")]
    [Tooltip("The draggable PlayerNameplateUI panel on the HUD canvas.")]
    public PlayerNameplateUI nameplate;

    // ─── Health UI ────────────────────────────────────────────────────────────

    [Header("Health UI — drag from your Canvas")]
    [Tooltip("The fill Image on the HP bar.")]
    public Image hpBarFill;

    [Tooltip("The root panel that shows/hides the HP bar.")]
    public GameObject hpBarPanel;

    [Tooltip("HealthBarFeedback component on the HP bar panel (ghost bar / damage delay effect).")]
    public HealthBarFeedback hpBarFeedback;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        // NOTE: PlayerUILinker must NOT be on the Canvas — put it on a dedicated
        // UIManager or GameManager GameObject that is always active.
        // If you see coroutine errors, that is why.
        StartCoroutine(WireUI());
    }

    // ─── Wiring ───────────────────────────────────────────────────────────────

    IEnumerator WireUI()
    {
        // Wait one frame so NetworkManager.IsListening is stable after scene load.
        yield return null;

        bool isNetworked = NetworkManager.Singleton != null
                        && NetworkManager.Singleton.IsListening;

        Debug.Log($"[PlayerUILinker] WireUI started — networked={isNetworked}");

        // Poll every frame until the locally owned player exists.
        PlayerController localPlayer = null;
        int waitFrames = 0;
        while (localPlayer == null)
        {
            localPlayer = FindLocalPlayer(isNetworked);
            if (localPlayer == null)
            {
                waitFrames++;
                if (waitFrames % 60 == 0)
                    Debug.Log($"[PlayerUILinker] Still waiting for local player… ({waitFrames} frames)");
                yield return null;
            }
        }

        Debug.Log($"[PlayerUILinker] Found local player: {localPlayer.name} (IsOwner={localPlayer.IsOwner})");


        // ── Wire XP Bar ───────────────────────────────────────────────────────
        // Subscribe to the event so the bar always updates regardless of timing.
        // Also push the reference as a fallback for any direct Inspector use.
        ExperienceManager xp = localPlayer.GetComponent<ExperienceManager>();
        if (xp != null)
        {
            xp.xpBar     = xpBar;
            xp.levelText = levelText;
            xp.xpText    = xpText;

            // Event-driven: whenever XP changes the bar updates automatically.
            // Guard against the slider being destroyed during scene cleanup
            // (Unity destroyed objects pass != null but throw on access).
            if (xpBar != null)
                xp.OnXPChanged += ratio => { if (xpBar != null) xpBar.value = ratio; };

            xp.RefreshXPBar();
        }

        // ── Wire Stamina ──────────────────────────────────────────────────────
        StaminaSystem stamina = localPlayer.GetComponent<StaminaSystem>();
        if (stamina != null)
        {
            stamina.staminaBarFill  = staminaBarFill;
            stamina.staminaBarPanel = staminaBarPanel;
            stamina.RefreshUI();
        }

        // ── Wire Mana ─────────────────────────────────────────────────────────
        ManaSystem mana = localPlayer.GetComponent<ManaSystem>();
        if (mana != null)
        {
            mana.manaBarFill         = manaBarFill;
            mana.manaBarReservedFill = manaBarReservedFill;
            mana.manaBarPanel        = manaBarPanel;
            mana.RefreshUI();
        }

        // ── Wire Health ───────────────────────────────────────────────────────
        HealthSystem health = localPlayer.GetComponent<HealthSystem>();
        if (health != null)
        {
            health.hpBarFill  = hpBarFill;
            health.hpBarPanel = hpBarPanel;
            health.RefreshUI();
        }

        // ── Wire Health Bar Feedback ──────────────────────────────────────────
        if (hpBarFeedback != null && health != null)
            hpBarFeedback.WireHealthSystem(health);

        // ── Wire Spell Cast Bar ───────────────────────────────────────────────
        SpellCaster spellCaster = localPlayer.GetComponent<SpellCaster>();
        if (spellCastBarUI != null && spellCaster != null)
            spellCastBarUI.SetSpellCaster(spellCaster);

        // ── Wire CharacterWindow ──────────────────────────────────────────────
        // CharacterWindow.Start() runs before the player spawns, so its
        // FindGameObjectWithTag("Player") returns null. Wire it here instead.
        CharacterWindow charWindow = FindFirstObjectByType<CharacterWindow>(FindObjectsInactive.Include);
        if (charWindow != null)
        {
            charWindow.playerHealthSystem  = health;
            charWindow.playerStaminaSystem = stamina;
            charWindow.RewirePlayer(localPlayer.gameObject);
        }

        // ── Wire InventoryUI ──────────────────────────────────────────────────
        // PlayerInventory is a singleton on the Player prefab — InventoryUI.Start()
        // runs before the player spawns so its event subscription is missed.
        // Re-subscribe here now that the instance exists.
        InventoryUI inventoryUI = FindFirstObjectByType<InventoryUI>();
        if (inventoryUI != null && PlayerInventory.Instance != null)
        {
            PlayerInventory.Instance.OnInventoryChanged -= inventoryUI.OnInventoryChanged;
            PlayerInventory.Instance.OnInventoryChanged += inventoryUI.OnInventoryChanged;
            inventoryUI.RefreshGrid();
        }

        // ── Wire Nameplate ────────────────────────────────────────────────────
        if (nameplate != null)
            nameplate.WirePlayer(localPlayer);

        // ── Wire any other scene systems that need the player ─────────────────
        // EnemySpawner no longer needs a player reference — EnemyAI finds
        // the nearest player automatically via RefreshNearestTarget().
        WaveManager wave = FindFirstObjectByType<WaveManager>();
        if (wave != null && wave.playerTransform == null)
            wave.playerTransform = localPlayer.transform;

        // ── Ensure all HUD panels are visible ─────────────────────────────────
        // Panels may start inactive in the scene (hidden during editing).
        // Force them active now that they are wired to the correct player data.
        if (hpBarPanel      != null) hpBarPanel.SetActive(true);
        if (staminaBarPanel != null) staminaBarPanel.SetActive(true);
        if (manaBarPanel    != null) manaBarPanel.SetActive(true);

        Debug.Log("[PlayerUILinker] All systems wired to local player: " + localPlayer.name);
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    static PlayerController FindLocalPlayer(bool networked)
    {
        var all = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (PlayerController pc in all)
        {
            if (networked)
            {
                if (pc.IsSpawned && pc.IsOwner) return pc;
            }
            else
            {
                return pc;
            }
        }
        return null;
    }
}
