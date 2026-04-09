using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

/// <summary>
/// Handles casting any spell from the spell bar.
/// All per-spell behaviour (cast time, channeling, projectile count) lives on SpellData.
///
/// Multiplayer: the owning client computes damage from local stats, then sends a
/// ServerRpc with the prefab's GlobalObjectIdHash — a stable uint baked into every
/// registered NetworkObject prefab by NGO. The server resolves the prefab from
/// NetworkManager's registry and spawns it, so no ordered array or enum mapping
/// is needed. Adding a new spell requires only registering its prefab in NetworkManager.
/// </summary>
public class SpellCaster : NetworkBehaviour
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

    // ─── Public Cast State API (for UI) ──────────────────────────────────────

    /// <summary>Fired when a spell with cast time begins charging. Args: spell, total cast duration.</summary>
    public event System.Action<SpellData, float> OnCastBegin;
    /// <summary>Fired when the cast bar completes (spell fires / channeling begins).</summary>
    public event System.Action                   OnCastComplete;
    /// <summary>Fired when channeling begins (either after cast time or from instant-cast channelable).</summary>
    public event System.Action<SpellData>        OnChannelBegin;
    /// <summary>Fired each time a channel tick fires a projectile.</summary>
    public event System.Action                   OnChannelTick;
    /// <summary>Fired when the player releases the channel button.</summary>
    public event System.Action                   OnChannelEnd;
    /// <summary>Fired when the cast or channel is interrupted externally (e.g. player moved).</summary>
    public event System.Action                   OnCastCancelled;

    /// <summary>0→1 progress during cast time (Pending state).</summary>
    public float CastProgress =>
        _state == CastState.Pending && _active != null && _active.castTime > 0f
            ? 1f - (_timer / _active.castTime)
            : 0f;

    /// <summary>0→1 progress toward the next channel tick.</summary>
    public float ChannelTickProgress =>
        _state == CastState.Channeling && _active != null && _active.channelTickRate > 0f
            ? 1f - (_timer / _active.channelTickRate)
            : 0f;

    public bool IsCasting    => _state == CastState.Pending;
    public bool IsChanneling => _state == CastState.Channeling;

    /// <summary>True whenever input and movement should be locked (casting or channeling).</summary>
    public bool IsLocked => _state != CastState.Idle;

    /// <summary>
    /// Interrupts any active cast or channel immediately (e.g. player moved).
    /// Fires OnCastCancelled so the UI can react.
    /// </summary>
    public void CancelCast()
    {
        if (_state == CastState.Idle) return;
        _state  = CastState.Idle;
        _timer  = 0f;
        _active = null;
        OnCastCancelled?.Invoke();
        Debug.Log("[SpellCaster] Cast cancelled.");
    }

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

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (_ownsInputActions && _inputActions != null)
            _inputActions.Player.Disable();
    }

    // ─── NGO Lifecycle ────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    void Update()
    {
        // Hotkeys 1–0: select slot and begin cast
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

        // Gamepad R1: cast currently selected spell
        if (Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame)
            BeginCast(SpellBarManager.Instance?.GetSelectedSpell());

        TickCastState();
    }

    // ─── Cast State Machine ───────────────────────────────────────────────────

    void TickCastState()
    {
        switch (_state)
        {
            case CastState.Pending:
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    OnCastComplete?.Invoke();
                    FireSpell();
                    if (_active != null && _active.isChannelable)
                    {
                        _state = CastState.Channeling;
                        _timer = _active.channelTickRate;
                        OnChannelBegin?.Invoke(_active);
                    }
                    else
                    {
                        _state = CastState.Idle;
                        _timer = 0f;
                    }
                }
                break;

            case CastState.Channeling:
                if (!IsCastHeld())
                {
                    _state = CastState.Idle;
                    OnChannelEnd?.Invoke();
                    Debug.Log("[SpellCaster] Channel released.");
                    break;
                }
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    FireSpell();
                    OnChannelTick?.Invoke();
                    _timer = _active?.channelTickRate ?? 0.5f;
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
            if (spell.isChannelable)
            {
                _state = CastState.Channeling;
                _timer = spell.channelTickRate;
                OnChannelBegin?.Invoke(spell);
            }
            else
            {
                _state = CastState.Idle;
                _timer = 0f;
            }
        }
        else
        {
            _state = CastState.Pending;
            _timer = spell.castTime;
            OnCastBegin?.Invoke(spell, spell.castTime);
            Debug.Log($"[SpellCaster] Casting {spell.spellName}... ({spell.castTime}s)");
        }
    }

    void FireSpell()
    {
        if (_active == null || _active.prefab == null) return;

        Transform origin = firePoint != null ? firePoint : transform;
        int       count  = Mathf.Max(1, _active.projectileCount);

        bool networkActive = NetworkManager.Singleton != null
                          && NetworkManager.Singleton.IsListening;

        if (networkActive)
        {
            // Validate that the prefab has a NetworkObject component (i.e. is registered in NGO).
            if (!_active.prefab.TryGetComponent<NetworkObject>(out NetworkObject _))
            {
                Debug.LogWarning($"[SpellCaster] '{_active.spellName}' prefab has no NetworkObject " +
                                 "component — register it in NetworkManager's prefab list.");
                return;
            }

            // Pass the prefab's asset name as the identifier. The server resolves it
            // by name from NetworkManager's own registered prefab list — the same list
            // on both client and server, so they always agree. Prefab names must be
            // unique within the NetworkManager registry (standard Unity convention).
            string prefabName    = _active.prefab.name;
            float  rawDamage     = ComputeRawDamage(_active);
            string hitEffectName = _active.hitEffect != null ? _active.hitEffect.name : "";

            foreach (Quaternion rot in GetShotRotations(origin, count))
                SpawnProjectileServerRpc(origin.position, rot, rawDamage, prefabName, hitEffectName);
        }
        else
        {
            foreach (Quaternion rot in GetShotRotations(origin, count))
            {
                GameObject go = Instantiate(_active.prefab, origin.position, rot);
                if (go.TryGetComponent<Fireball>(out Fireball fb))
                    fb.hitEffect = _active.hitEffect;
            }
        }

        Debug.Log($"[SpellCaster] Fired {_active.spellName} x{count}");
    }

    // ─── Network RPC ──────────────────────────────────────────────────────────

    /// <summary>
    /// Runs on the server. Resolves the prefab from NetworkManager's registry by name,
    /// injects the client-computed damage, and spawns the projectile for all clients.
    /// </summary>
    [Rpc(SendTo.Server)]
    private void SpawnProjectileServerRpc(Vector3 pos, Quaternion rot, float rawDamage, string prefabName, string hitEffectName)
    {
        NetworkObject prefab = FindRegisteredPrefab(prefabName);
        if (prefab == null)
        {
            Debug.LogWarning($"[SpellCaster] No registered NetworkObject prefab named '{prefabName}' — " +
                             "check NetworkManager's prefab list.");
            return;
        }

        NetworkObject instance = Instantiate(prefab, pos, rot);

        // Inject damage and hit effect before Spawn() so they're available when OnNetworkSpawn fires.
        if (instance.TryGetComponent<Fireball>(out Fireball fb))
        {
            fb.precomputedDamage = rawDamage;
            if (!string.IsNullOrEmpty(hitEffectName))
                fb.hitEffect = FindRegisteredGameObject(hitEffectName);
        }

        instance.Spawn(true);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches NetworkManager's registered prefab list for an entry by name.
    /// Returns the raw GameObject; caller casts to NetworkObject when needed.
    /// Falls back to Resources.Load when not found in the registry (local VFX only).
    /// </summary>
    static GameObject FindRegisteredGameObject(string prefabName)
    {
        foreach (var entry in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
        {
            if (entry.Prefab != null && entry.Prefab.name == prefabName)
                return entry.Prefab;
        }
        return Resources.Load<GameObject>(prefabName);
    }

    static NetworkObject FindRegisteredPrefab(string prefabName)
    {
        GameObject go = FindRegisteredGameObject(prefabName);
        if (go != null && go.TryGetComponent<NetworkObject>(out NetworkObject no))
            return no;
        return null;
    }

    /// <summary>Returns one rotation per projectile, spread evenly around the origin.</summary>
    Quaternion[] GetShotRotations(Transform origin, int count)
    {
        if (count == 1) return new[] { origin.rotation };

        var   rots       = new Quaternion[count];
        float halfSpread = _active.spreadAngle * 0.5f;
        float step       = _active.spreadAngle / (count - 1);
        for (int i = 0; i < count; i++)
        {
            float yaw = -halfSpread + step * i;
            rots[i]   = origin.rotation * Quaternion.Euler(0f, yaw, 0f);
        }
        return rots;
    }

    /// <summary>
    /// Computes raw spell damage from the local player's stats.
    /// Called client-side on the owner before the ServerRpc is sent,
    /// so the server never needs to read per-player singletons.
    /// </summary>
    float ComputeRawDamage(SpellData spell)
    {
        float baseDmg = 0f;
        if (spell.prefab.TryGetComponent<Fireball>(out Fireball fb))
            baseDmg = fb.baseDamage;

        float spellBonus = SkillTreeManager.Instance?.TotalSpellDamageBonus ?? 0f;
        float fireBonus  = SkillTreeManager.Instance?.TotalFireDamageBonus  ?? 0f;
        float intMult    = ExperienceManager.Instance?.SpellDamageMultiplier ?? 1f;

        return (baseDmg + spellBonus + fireBonus) * intMult;
    }

    bool IsCastHeld()
    {
        if (Keyboard.current != null)
            for (int i = 0; i < 10; i++)
            {
                Key key = i == 9 ? Key.Digit0 : (Key)((int)Key.Digit1 + i);
                if (Keyboard.current[key].isPressed) return true;
            }

        return Gamepad.current != null && Gamepad.current.rightShoulder.isPressed;
    }
}
