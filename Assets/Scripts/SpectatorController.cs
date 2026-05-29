using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Drives a dead player's spectate camera in multiplayer co-op. While a player is down
/// but teammates are alive, this follows a living player and lets the dead player cycle
/// between survivors — on-screen Prev/Next buttons and the Move axis (keyboard/gamepad
/// left-right). Enabled by DeathScreenManager.EnterSpectatorMode(); disabled on respawn.
///
/// Lives on the DeathScreenManager GameObject. The overlay panel + name label are read
/// from DeathScreenManager.Instance so there is a single Inspector wiring point.
/// </summary>
[DisallowMultipleComponent]
public class SpectatorController : MonoBehaviour
{
    [Header("Overlay Buttons (optional — drag them in; onClick is auto-wired)")]
    [Tooltip("Cycles to the previous living teammate.")]
    [SerializeField] private Button prevButton;

    [Tooltip("Cycles to the next living teammate.")]
    [SerializeField] private Button nextButton;

    [Header("Tuning")]
    [Tooltip("Move-axis magnitude needed to register a spectator cycle (avoids stick drift).")]
    [SerializeField] private float cycleThreshold = 0.6f;

    // ─── State ──────────────────────────────────────────────────────────────────
    private readonly List<GameObject> _living = new();
    private int  _index;
    private bool _active;
    private bool _axisArmed = true; // edge-detect so a held stick doesn't spin the cycle

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────────
    void Awake()
    {
        // Auto-wire the overlay buttons so the user only has to drag them into the
        // Inspector slots — no manual OnClick configuration needed.
        if (prevButton != null) prevButton.onClick.AddListener(Prev);
        if (nextButton != null) nextButton.onClick.AddListener(Next);

        enabled = false; // dormant until Begin()
    }

    // ─── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Start spectating: build the living list, focus the first survivor, show the overlay.</summary>
    public void Begin()
    {
        RebuildLiving();
        _index  = 0;
        _active = _living.Count > 0;

        if (OverlayPanel != null) OverlayPanel.SetActive(true);

        FocusCurrent();
        enabled = true;
    }

    /// <summary>Stop spectating: hide the overlay and go dormant.</summary>
    public void End()
    {
        _active = false;
        enabled = false;
        _living.Clear();

        if (OverlayPanel != null) OverlayPanel.SetActive(false);
    }

    /// <summary>Next survivor. Also wired to the overlay's Next button OnClick.</summary>
    public void Next() => Cycle(+1);

    /// <summary>Previous survivor. Also wired to the overlay's Prev button OnClick.</summary>
    public void Prev() => Cycle(-1);

    // ─── Update ─────────────────────────────────────────────────────────────────
    void Update()
    {
        if (!_active) return;

        // If the player we're watching died (or left), retarget to another survivor.
        if (!IsAlive(CurrentTarget))
        {
            RebuildLiving();
            if (_living.Count == 0) return; // everyone is down — server shows game-over
            _index = Mathf.Clamp(_index, 0, _living.Count - 1);
            FocusCurrent();
        }

        // Keyboard / gamepad left-right cycles, edge-detected so holding doesn't spin.
        float x = InputManager.Player.Move.ReadValue<Vector2>().x;
        if (_axisArmed && Mathf.Abs(x) >= cycleThreshold)
        {
            if (x > 0f) Next(); else Prev();
            _axisArmed = false;
        }
        else if (Mathf.Abs(x) < cycleThreshold * 0.5f)
        {
            _axisArmed = true;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    private void Cycle(int dir)
    {
        RebuildLiving();
        if (_living.Count == 0) return;
        _index = ((_index + dir) % _living.Count + _living.Count) % _living.Count;
        FocusCurrent();
    }

    private GameObject CurrentTarget =>
        (_index >= 0 && _index < _living.Count) ? _living[_index] : null;

    private void FocusCurrent()
    {
        GameObject t = CurrentTarget;
        if (t == null) return;
        RetargetCamera(t.transform);
        UpdateLabel(t);
    }

    /// <summary>
    /// Points the gameplay camera at a transform. Uses CameraModeSwitcher when present,
    /// otherwise sets the target directly on whichever camera controller is on Camera.main
    /// (projects that don't use CameraModeSwitcher still work). Also used to hand the
    /// camera back to the local player on respawn.
    /// </summary>
    public static void RetargetCamera(Transform t)
    {
        if (t == null) return;

        CameraModeSwitcher switcher = FindFirstObjectByType<CameraModeSwitcher>();
        if (switcher != null) { switcher.SetTarget(t); return; }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[SpectatorController] Camera.main is null — cannot retarget.");
            return;
        }

        var iso = cam.GetComponent<CameraControllerIsometric>();
        if (iso != null) iso.target = t;
        var tp = cam.GetComponent<CameraControllerThirdPerson>();
        if (tp != null) tp.target = t;

        if (iso == null && tp == null)
            Debug.LogWarning("[SpectatorController] No camera controller found on Camera.main to retarget.");
    }

    private void RebuildLiving()
    {
        GameObject prev = CurrentTarget;
        _living.Clear();
        foreach (GameObject p in PlayerController.All)
        {
            HealthSystem h = p.GetComponent<HealthSystem>();
            if (h != null && h.currentHealth > 0f) _living.Add(p);
        }
        // Keep watching the same player across a rebuild when still alive.
        int keep = prev != null ? _living.IndexOf(prev) : -1;
        if (keep >= 0) _index = keep;
    }

    private static bool IsAlive(GameObject go)
    {
        if (go == null) return false;
        HealthSystem h = go.GetComponent<HealthSystem>();
        return h != null && h.currentHealth > 0f;
    }

    private void UpdateLabel(GameObject target)
    {
        if (NameLabel != null)
            NameLabel.text = "Spectating: " + ResolveName(target);
    }

    /// <summary>Resolve the lobby display name by OwnerClientId (same source as PlayerNameTag).</summary>
    private static string ResolveName(GameObject player)
    {
        NetworkObject net = player.GetComponent<NetworkObject>();
        if (net == null) return player.name;

        foreach (LobbyPlayer lp in FindObjectsByType<LobbyPlayer>(FindObjectsSortMode.None))
        {
            if (lp.OwnerClientId == net.OwnerClientId)
            {
                string n = lp.PlayerName.Value.ToString();
                if (!string.IsNullOrEmpty(n)) return n;
            }
        }
        return "Player " + net.OwnerClientId;
    }

    private static GameObject OverlayPanel =>
        DeathScreenManager.Instance != null ? DeathScreenManager.Instance.spectatorOverlayPanel : null;

    private static TMP_Text NameLabel =>
        DeathScreenManager.Instance != null ? DeathScreenManager.Instance.spectatingNameLabel : null;
}
