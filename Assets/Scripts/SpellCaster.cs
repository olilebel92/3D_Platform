using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Single component on the Player that handles casting any spell from the spell bar.
/// All per-spell behaviour (cast time, channeling, projectile count) is defined on SpellData.
/// No new player scripts are needed when adding new spells.
/// </summary>
public class SpellCaster : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Setup")]
    [Tooltip("Spawn point for projectiles. Defaults to this transform if unassigned.")]
    public Transform firePoint;

    // ─── Cast State ───────────────────────────────────────────────────────────

    private enum CastState { Idle, Pending, Channeling }
    private CastState _state  = CastState.Idle;
    private float     _timer  = 0f;
    private SpellData _active = null;

    // ─── Input ────────────────────────────────────────────────────────────────

    private PlayerInputActions _inputActions;
    private bool _ownsInputActions = false;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        PlayerController pc = GetComponent<PlayerController>();
        if (pc != null)
        {
            _inputActions = pc.InputActions;
        }
        else
        {
            Debug.LogWarning("[SpellCaster] PlayerController not found — creating standalone input.");
            _inputActions = new PlayerInputActions();
            _inputActions.Player.Enable();
            _ownsInputActions = true;
        }
    }

    void OnDestroy()
    {
        if (_ownsInputActions && _inputActions != null)
            _inputActions.Player.Disable();
    }

    void Update()
    {
        // ── Hotkeys 1–0 select a slot and immediately begin casting ───────────
        if (Keyboard.current != null)
        {
            for (int i = 0; i < 10; i++)
            {
                Key key = i == 9 ? Key.Digit0 : (Key)((int)Key.Digit1 + i);
                if (Keyboard.current[key].wasPressedThisFrame)
                {
                    SpellBarManager.Instance?.SelectSlot(i);
                    BeginCast(SpellBarManager.Instance?.GetSpellAt(i));
                    return;
                }
            }
        }

        // ── Gamepad R1 → cast currently selected spell ────────────────────────
        if (Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame)
            BeginCast(SpellBarManager.Instance?.GetSelectedSpell());

        // ── Tick active cast state ────────────────────────────────────────────
        TickCastState();
    }

    // ─── State Machine ────────────────────────────────────────────────────────

    void TickCastState()
    {
        switch (_state)
        {
            case CastState.Pending:
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    FireSpell();
                    _state = _active != null && _active.isChannelable
                        ? CastState.Channeling : CastState.Idle;
                    _timer = _active != null ? _active.channelTickRate : 0f;
                }
                break;

            case CastState.Channeling:
                if (!IsCastHeld())
                {
                    _state = CastState.Idle;
                    Debug.Log("[SpellCaster] Channel released.");
                    break;
                }
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    FireSpell();
                    _timer = _active != null ? _active.channelTickRate : 0.5f;
                }
                break;
        }
    }

    void BeginCast(SpellData spell)
    {
        if (spell == null || spell.prefab == null)
        {
            Debug.LogWarning("[SpellCaster] No spell or prefab to cast.");
            return;
        }

        _active = spell;

        if (spell.castTime <= 0f)
        {
            FireSpell();
            _state = spell.isChannelable ? CastState.Channeling : CastState.Idle;
            _timer = spell.channelTickRate;
        }
        else
        {
            _state = CastState.Pending;
            _timer = spell.castTime;
            Debug.Log($"[SpellCaster] Casting {spell.spellName}... ({spell.castTime}s)");
        }
    }

    void FireSpell()
    {
        if (_active == null || _active.prefab == null) return;

        Transform origin = firePoint != null ? firePoint : transform;
        int count        = Mathf.Max(1, _active.projectileCount);

        if (count == 1)
        {
            Instantiate(_active.prefab, origin.position, origin.rotation);
        }
        else
        {
            float halfSpread = _active.spreadAngle * 0.5f;
            float step       = _active.spreadAngle / (count - 1);

            for (int i = 0; i < count; i++)
            {
                float yaw      = -halfSpread + step * i;
                Quaternion rot = origin.rotation * Quaternion.Euler(0f, yaw, 0f);
                Instantiate(_active.prefab, origin.position, rot);
            }
        }

        Debug.Log($"[SpellCaster] Fired: {_active.spellName} x{count}");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    bool IsCastHeld()
    {
        if (Keyboard.current != null)
            for (int i = 0; i < 10; i++)
            {
                Key key = i == 9 ? Key.Digit0 : (Key)((int)Key.Digit1 + i);
                if (Keyboard.current[key].isPressed) return true;
            }

        if (Gamepad.current != null && Gamepad.current.rightShoulder.isPressed) return true;

        return false;
    }
}
