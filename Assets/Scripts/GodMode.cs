using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Debug-only cheat tool. Attach to the Player GameObject.
/// Press 0 in Play Mode to toggle, or right-click → "Enable God Mode".
/// Assign all SpellData assets to the Spells list in the Inspector.
/// </summary>
public class GodMode : MonoBehaviour
{
    [Header("God Mode Settings")]
    [SerializeField] private float _godHP = 50000f;

    [Header("Spells to Equip")]
    [Tooltip("Drag all SpellData assets here (up to 10 slots).")]
    [SerializeField] private SpellData[] _spells;

    // ─── Cached refs ───

    private HealthSystem _health;
    private NetworkObject _netObj;
    private bool _isActive;

    private void Start()
    {
        _health = GetComponent<HealthSystem>();
        _netObj = GetComponent<NetworkObject>();
    }

    private void Update()
    {
        // In MP, only respond to input on the locally-owned player.
        // In solo (no NetworkObject or unspawned), no gate.
        if (_netObj != null && _netObj.IsSpawned && !_netObj.IsOwner) return;

        if (Keyboard.current[Key.Digit0].wasPressedThisFrame)
            Toggle();
    }

    // ─── Context Menu (play mode only) ───

    [ContextMenu("Enable God Mode")]
    public void EnableGodMode()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[GodMode] Only usable in Play Mode."); return; }
        ApplyHP();
        ApplySkillPoints();
        ApplySpells();
        _isActive = true;
        Debug.Log($"[GodMode] ENABLED — HP {_godHP}, all skills maxed, {_spells?.Length ?? 0} spells equipped.");
    }

    [ContextMenu("Disable God Mode")]
    public void DisableGodMode()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[GodMode] Only usable in Play Mode."); return; }
        _isActive = false;
        Debug.Log("[GodMode] Disabled.");
    }

    // ─── Private Helpers ───

    private void Toggle()
    {
        if (_isActive)
            DisableGodMode();
        else
            EnableGodMode();
    }

    private void ApplyHP()
    {
        if (_health == null)
        {
            _health = GetComponent<HealthSystem>();
            if (_health == null) { Debug.LogWarning("[GodMode] HealthSystem not found on this GameObject."); return; }
        }

        // Patch the private backing field so ApplyEquipmentHP won't reset us
        FieldInfo permanentField = typeof(HealthSystem).GetField(
            "_permanentMaxHealth",
            BindingFlags.NonPublic | BindingFlags.Instance);
        permanentField?.SetValue(_health, _godHP);

        // HealthSystem auto-routes to the server in MP. The NetworkVariable broadcast
        // updates this owner's HUD via OnHealthChanged. RefreshUI keeps in-world HP bars in sync.
        _health.InitializeServerHP(_godHP, _godHP);
        _health.RefreshUI();
    }

    private void ApplySkillPoints()
    {
        if (SkillTreeManager.Instance == null)
        {
            Debug.LogWarning("[GodMode] SkillTreeManager.Instance is null — skills not maxed.");
            return;
        }

        SkillTreeManager.Instance.MaxAllSkills();
    }

    private void ApplySpells()
    {
        if (SpellBarManager.Instance == null)
        {
            Debug.LogWarning("[GodMode] SpellBarManager.Instance is null — spells not applied.");
            return;
        }

        if (_spells == null || _spells.Length == 0)
        {
            Debug.LogWarning("[GodMode] No spells assigned. Drag SpellData assets into the Spells list.");
            return;
        }

        int count = Mathf.Min(_spells.Length, 10);
        for (int i = 0; i < count; i++)
        {
            if (_spells[i] != null)
                SpellBarManager.Instance.SetSpell(i, _spells[i]);
        }
    }
}
