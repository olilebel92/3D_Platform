using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

/// <summary>
/// Handles casting any spell from the spell bar.
/// All per-spell behaviour (spell type, cast timing, interrupt rules, channeling)
/// lives on SpellData.
///
/// Multiplayer: the owning client computes damage from local stats, then sends a
/// ServerRpc with the prefab name — the server resolves it from NetworkManager's
/// registry and spawns it, so no ordered array or enum mapping is needed.
/// </summary>
public class SpellCaster : NetworkBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Setup")]
    [Tooltip("Spawn point for projectiles. Defaults to this transform if unassigned.")]
    public Transform firePoint;

    [Tooltip("Telegraph projector on this player. Assign the TelegraphProjector component here.")]
    [SerializeField] private TelegraphProjector _telegraphProjector;


    // ─── Cast State ───────────────────────────────────────────────────────────

    private enum CastState { Idle, AwaitingTarget, PreCast, Pending, Channeling }
    private CastState _state             = CastState.Idle;
    private float     _timer             = 0f;
    private SpellData _active            = null;
    private float     _castStartTime     = 0f;  // Time.time when cast was initiated (used for grace checks)
    private bool      _throwEventFired   = false;

    // ─── Channel Object Tracking ─────────────────────────────────────────────
    // Channel spells spawn their VFX prefab once and keep it alive for the full
    // duration. On each tick, ChannelTick() is called on the persistent instance
    // instead of spawning a new one — so the VFX plays continuously.

    /// <summary>True after the persistent channel VFX has been spawned (owner client).</summary>
    private bool         _channelObjSpawned   = false;
    /// <summary>Server-side reference to the active channel NetworkObject.</summary>
    private NetworkObject _activeChannelNetObj = null;
    /// <summary>Solo-mode reference to the active channel HealingWave instance.</summary>
    private HealingWave  _activeSoloChannelHW  = null;

    // ─── Cooldowns ────────────────────────────────────────────────────────────
    // Cooldown starts when BeginCast is called, not when the spell fires.
    // This means castTime = cooldown → zero downtime between casts.
    // Cancelled casts do NOT trigger a cooldown.

    private readonly Dictionary<SpellData, float> _cooldownEndTimes = new();

    /// <summary>Seconds remaining on this spell's cooldown. 0 if ready.</summary>
    public float GetCooldownRemaining(SpellData spell)
    {
        if (spell == null || !_cooldownEndTimes.TryGetValue(spell, out float endTime)) return 0f;
        return Mathf.Max(0f, endTime - Time.time);
    }

    /// <summary>True if this spell cannot be cast yet due to cooldown.</summary>
    public bool IsOnCooldown(SpellData spell) => GetCooldownRemaining(spell) > 0f;

    // ─── Dependencies ─────────────────────────────────────────────────────────

    private StatusEffectHandler _statusEffects;
    private PlayerController    _playerController;
    private AudioSource         _audioSource;
    private TargetSelector      _targetSelector;

    // ─── Public Cast State API (for UI) ──────────────────────────────────────

    /// <summary>Fired when a spell with cast time begins charging (after castStartDelay). Args: spell, total cast duration.</summary>
    public event System.Action<SpellData, float> OnCastBegin;
    /// <summary>Fired when the cast bar completes (spell fires / channeling begins).</summary>
    public event System.Action                   OnCastComplete;
    /// <summary>Fired throwAnimLeadTime seconds before the spell fires — cue the throw animation.</summary>
    public event System.Action                   OnCastThrowStart;
    /// <summary>Fired when channeling begins (either after cast time or from instant-cast Channel spell).</summary>
    public event System.Action<SpellData>        OnChannelBegin;
    /// <summary>Fired each time a channel tick fires a projectile.</summary>
    public event System.Action                   OnChannelTick;
    /// <summary>Fired when the player releases the channel button.</summary>
    public event System.Action                   OnChannelEnd;
    /// <summary>Fired when the cast or channel is interrupted (by movement, damage, or stun).</summary>
    public event System.Action                   OnCastCancelled;

    /// <summary>0→1 progress during cast time (Pending state only).</summary>
    public float CastProgress =>
        _state == CastState.Pending && _active != null && _active.castTime > 0f
            ? 1f - (_timer / _active.castTime)
            : 0f;

    /// <summary>0→1 progress toward the next channel tick.</summary>
    public float ChannelTickProgress =>
        _state == CastState.Channeling && _active != null && _active.channelTickRate > 0f
            ? 1f - (_timer / _active.channelTickRate)
            : 0f;

    /// <summary>True while the cast bar is filling (after castStartDelay).</summary>
    public bool IsCasting    => _state == CastState.Pending;
    /// <summary>True while a Channel spell is being held.</summary>
    public bool IsChanneling => _state == CastState.Channeling;
    /// <summary>True whenever any cast state is active (pre-cast, casting, or channeling).</summary>
    public bool IsActive     => _state != CastState.Idle;
    /// <summary>
    /// True when player movement should be fully suppressed.
    /// Covers two cases:
    ///   1. In PreCast or Pending state with lockMovementDuringCast enabled on the spell.
    ///   2. Actively channeling a spell that has lockMovementDuringChannel enabled.
    /// </summary>
    public bool IsMovementLocked =>
        ((_state == CastState.PreCast || _state == CastState.Pending) && _active != null && _active.lockMovementDuringCast)
        || (_state == CastState.Channeling && _active != null && _active.lockMovementDuringChannel);

    /// <summary>Seconds elapsed since the cast was initiated (includes castStartDelay window).</summary>
    private float TimeSinceCastStart => Time.time - _castStartTime;

    // ─── Cancellation API ────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to cancel the cast due to player movement.
    /// Returns false (no cancel) when:
    ///   - Within the spell's movementInterruptGrace window.
    ///   - The spell's lockMovementDuringCast is false — movement is freely allowed and never interrupts.
    ///   - Actively channeling with lockMovementDuringChannel enabled.
    /// </summary>
    public bool TryCancelByMovement()
    {
        if (_state == CastState.Idle || _state == CastState.AwaitingTarget) return false;
        if (_active != null && TimeSinceCastStart < _active.movementInterruptGrace) return false;
        if ((_state == CastState.PreCast || _state == CastState.Pending) && _active != null && !_active.lockMovementDuringCast) return false;
        if (_state == CastState.Channeling && _active != null && _active.lockMovementDuringChannel) return false;
        CancelCast();
        return true;
    }

    /// <summary>
    /// Attempts to cancel the cast due to the caster taking damage.
    /// Respects the active spell's damageInterruptGrace.
    /// </summary>
    public bool TryCancelByDamage()
    {
        if (_state == CastState.Idle) return false;
        if (_active != null && TimeSinceCastStart < _active.damageInterruptGrace) return false;
        CancelCast();
        return true;
    }

    /// <summary>
    /// Cancels the cast immediately. Stun always bypasses all grace periods.
    /// </summary>
    public void CancelByStun()
    {
        CancelCast();
    }

    /// <summary>
    /// Interrupts any active cast or channel immediately.
    /// Fires OnCastCancelled so the UI can react.
    /// </summary>
    public void CancelCast()
    {
        if (_state == CastState.Idle) return;
        _audioSource?.Stop();
        if (_state == CastState.AwaitingTarget) _targetSelector?.CancelTargeting();
        StopChannel();
        _telegraphProjector?.Hide();

        // Cancelled cast — remove the cooldown so the player isn't penalised.
        if (_active != null) _cooldownEndTimes.Remove(_active);

        _state  = CastState.Idle;
        _timer  = 0f;
        _active = null;
        OnCastCancelled?.Invoke();
        Debug.Log("[SpellCaster] Cast cancelled.");
    }

    /// <summary>
    /// Tears down the persistent channel VFX object (if any).
    /// Safe to call even when no channel is active — guards internally.
    /// </summary>
    private void StopChannel()
    {
        if (!_channelObjSpawned && _activeSoloChannelHW == null) return;
        _channelObjSpawned = false;

        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networkActive)
        {
            StopChannelServerRpc();
        }
        else
        {
            if (_activeSoloChannelHW != null)
            {
                Destroy(_activeSoloChannelHW.gameObject);
                _activeSoloChannelHW = null;
            }
        }
    }

    // ─── Input ────────────────────────────────────────────────────────────────

    private PlayerInputActions _inputActions;
    private bool _ownsInputActions = false;

    // ─── Aim ──────────────────────────────────────────────────────────────────

    private Transform _cameraTransform;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        _statusEffects    = GetComponent<StatusEffectHandler>();
        _targetSelector   = GetComponent<TargetSelector>();
        _cameraTransform  = Camera.main?.transform;

        if (_targetSelector != null)
        {
            _targetSelector.OnTargetSelected    += OnTargetConfirmed;
            _targetSelector.OnTargetingCancelled += CancelCast;
        }
        _audioSource      = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource           = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
        }

        PlayerController pc = GetComponent<PlayerController>();
        if (pc != null)
        {
            _playerController = pc;
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
        if (_targetSelector != null)
        {
            _targetSelector.OnTargetSelected     -= OnTargetConfirmed;
            _targetSelector.OnTargetingCancelled -= CancelCast;
        }
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

        // Gamepad R1: cast slot 1
        if (Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame)
            BeginCast(SpellBarManager.Instance?.GetSpellAt(0));

        // Gamepad L1: cast slot 2
        if (Gamepad.current != null && Gamepad.current.leftShoulder.wasPressedThisFrame)
            BeginCast(SpellBarManager.Instance?.GetSpellAt(1));

        TickCastState();
    }

    // ─── Cast State Machine ───────────────────────────────────────────────────

    void TickCastState()
    {
        switch (_state)
        {
            case CastState.AwaitingTarget:
                // TargetSelector handles click detection; OnTargetConfirmed() advances the state.
                break;

            case CastState.PreCast:
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                    TransitionFromPreCast();
                break;

            case CastState.Pending:
                _timer -= Time.deltaTime;
                if (!_throwEventFired && _active != null && _timer <= _active.throwAnimLeadTime)
                {
                    _throwEventFired = true;
                    Debug.Log($"[SpellCaster] CastThrowStart fired — leadTime={_active.throwAnimLeadTime}, timer={_timer}");
                    OnCastThrowStart?.Invoke();
                }
                if (_timer <= 0f)
                {
                    _audioSource?.Stop();
                    OnCastComplete?.Invoke();
                    bool isChannel = _active != null && _active.spellType == SpellType.Channel;

                    // Channel spells only fire immediately on cast complete if fireOnChannelStart
                    // is explicitly enabled. Otherwise the first effect fires on the first tick.
                    if (!isChannel || _active.fireOnChannelStart)
                        FireSpell();

                    if (isChannel)
                    {
                        _state = CastState.Channeling;
                        _timer = _active.channelTickRate;
                        OnChannelBegin?.Invoke(_active);
                    }
                    else
                    {
                        _telegraphProjector?.Hide();
                        _state  = CastState.Idle;
                        _active = null;
                        _timer  = 0f;
                    }
                }
                break;

            case CastState.Channeling:
                if (!IsCastHeld())
                {
                    StopChannel();
                    _telegraphProjector?.Hide();
                    _state  = CastState.Idle;
                    _active = null;
                    OnChannelEnd?.Invoke();
                    DebugLogger.Log(DebugLogger.Category.SpellCast, "Channel released.");
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

    /// <summary>Called when castStartDelay expires; transitions to cast bar or instant-fires.</summary>
    void TransitionFromPreCast()
    {
        if (_active == null)
        {
            _state = CastState.Idle;
            return;
        }

        if (_active.castTime > 0f)
        {
            _state = CastState.Pending;
            _timer = _active.castTime;
            if (_active.castSound != null && _audioSource != null)
            {
                _audioSource.clip = _active.castSound;
                _audioSource.Play();
            }
            OnCastBegin?.Invoke(_active, _active.castTime);
            DebugLogger.Log(DebugLogger.Category.SpellCast,
                $"Casting {_active.spellName}... ({_active.castTime}s)");
        }
        else
        {
            // castStartDelay only — instant-fire after windup
            bool isChannel = _active != null && _active.spellType == SpellType.Channel;
            if (!isChannel || _active.fireOnChannelStart)
                FireSpell();
            if (isChannel)
            {
                _state = CastState.Channeling;
                _timer = _active.channelTickRate;
                OnChannelBegin?.Invoke(_active);
            }
            else
            {
                _telegraphProjector?.Hide();
                _state  = CastState.Idle;
                _active = null;
                _timer  = 0f;
            }
        }
    }

    void BeginCast(SpellData spell)
    {
        if (spell == null || spell.prefab == null)
        {
            Debug.LogWarning("[SpellCaster] No spell or prefab to cast.");
            return;
        }

        if (_statusEffects != null && _statusEffects.IsStunned)
        {
            Debug.Log("[SpellCaster] Cannot cast while stunned.");
            return;
        }

        if (IsOnCooldown(spell))
        {
            Debug.Log($"[SpellCaster] {spell.spellName} on cooldown ({GetCooldownRemaining(spell):0.0}s left).");
            return;
        }

        // Target-locked spells enter targeting mode — cooldown starts only after target is confirmed.
        if (spell.spellType == SpellType.TargetLocked)
        {
            if (_targetSelector == null)
            {
                Debug.LogWarning("[SpellCaster] No TargetSelector component found — cannot cast target-locked spells.");
                return;
            }
            if (_state == CastState.AwaitingTarget && _active == spell) return; // already targeting this spell
            if (_state == CastState.AwaitingTarget) _targetSelector.CancelTargeting(); // switch spell

            _active          = spell;
            _castStartTime   = Time.time;
            _throwEventFired = false;
            _state           = CastState.AwaitingTarget;
            _targetSelector.BeginTargeting();
            Debug.Log($"[SpellCaster] {spell.spellName} — left-click an enemy to cast.");
            return;
        }

        // Cooldown starts now — runs concurrently with the cast.
        // If castTime == cooldown the spell is ready again the moment it fires.
        if (spell.cooldown > 0f)
            _cooldownEndTimes[spell] = Time.time + spell.cooldown;

        // Sprinting → cancel sprint and proceed with the cast.
        if (_playerController != null && _playerController.IsSprinting)
            _playerController.CancelSprint();

        // Already casting this exact spell — keep going, don't restart.
        if (_state != CastState.Idle && _active == spell) return;

        _active            = spell;
        _castStartTime     = Time.time;
        _throwEventFired   = false;

        // Show telegraph as soon as the cast begins so the player sees the preview
        // during the full windup + cast-bar window.
        _telegraphProjector?.Show(spell, transform);

        if (spell.castStartDelay > 0f)
        {
            _state = CastState.PreCast;
            _timer = spell.castStartDelay;
            Debug.Log($"[SpellCaster] Pre-cast {spell.spellName} ({spell.castStartDelay}s windup)");
            return;
        }

        if (spell.castTime > 0f)
        {
            _state = CastState.Pending;
            _timer = spell.castTime;
            if (spell.castSound != null && _audioSource != null)
                _audioSource.PlayOneShot(spell.castSound);
            OnCastBegin?.Invoke(spell, spell.castTime);
            DebugLogger.Log(DebugLogger.Category.SpellCast,
                $"Casting {spell.spellName}... ({spell.castTime}s)");
            return;
        }

        // Instant cast (no delay, no cast time)
        bool instantIsChannel = spell.spellType == SpellType.Channel;
        if (!instantIsChannel || spell.fireOnChannelStart)
            FireSpell();
        if (instantIsChannel)
        {
            _state = CastState.Channeling;
            _timer = spell.channelTickRate;
            OnChannelBegin?.Invoke(spell);
        }
        else
        {
            _telegraphProjector?.Hide();
            _state  = CastState.Idle;
            _active = null;
            _timer  = 0f;
        }
    }

    void FireSpell()
    {
        if (_active == null || _active.prefab == null) return;

        Transform origin = firePoint != null ? firePoint : transform;
        int       count  = Mathf.Max(1, _active.projectileCount);

        // Lazy-init camera (may not be ready at Start in multiplayer)
        if (_cameraTransform == null) _cameraTransform = Camera.main?.transform;

        // Isometric aim: point toward the cursor's world position (flat, no pitch).
        // Falls back to camera forward (flattened) if the cursor ray missed, then to
        // the fire-point rotation for dedicated-server contexts with no camera.
        // Mirror the same fallback logic as TelegraphProjector.GetAimDirection():
        // HasHit → aim toward cursor/stick world point.
        // No hit  → use the player's facing (movement direction), not camera forward,
        //           so the spell fires exactly where the telegraph is pointing.
        Quaternion aimRot;
        if (IsoAim.HasHit)
        {
            Vector3 aimDir = IsoAim.AimDirectionFrom(origin.position);
            aimRot = Quaternion.LookRotation(aimDir);
        }
        else
        {
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            aimRot = fwd.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(fwd.normalized)
                : origin.rotation;
        }

        // For target-locked spells, aim toward the locked target instead of the cursor.
        if (_active.spellType == SpellType.TargetLocked && _targetSelector?.SelectedTarget != null)
        {
            Vector3 toTarget = _targetSelector.SelectedTarget.position - origin.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.001f)
                aimRot = Quaternion.LookRotation(toTarget.normalized);
        }

        Vector3 firePos = _active.spawnOrigin == SpellSpawnOrigin.Caster
            ? transform.position : origin.position;

        Quaternion fireRot = _active.spawnRotation switch
        {
            SpellSpawnRotation.WorldUp       => Quaternion.Euler(-90f, 0f, 0f),
            SpellSpawnRotation.CasterForward => Quaternion.LookRotation(transform.forward),
            SpellSpawnRotation.None          => Quaternion.identity,
            _                                => aimRot, // CameraAim (default)
        };

        bool networkActive = NetworkManager.Singleton != null
                          && NetworkManager.Singleton.IsListening;

        float rawDamage = ComputeRawDamage(_active, SkillTreeManager.Instance?.GetSpellRank(_active) ?? 0);

        // ── Channel spells: spawn once, tick on existing instance ─────────────
        if (_active.spellType == SpellType.Channel)
        {
            if (networkActive)
            {
                if (!_channelObjSpawned)
                {
                    _channelObjSpawned = true;
                    SpawnChannelObjectServerRpc(firePos, fireRot, rawDamage, _active.prefab.name);
                }
                else
                {
                    TickChannelServerRpc(rawDamage);
                }
            }
            else
            {
                if (_activeSoloChannelHW == null)
                {
                    GameObject go = Instantiate(_active.prefab, firePos, fireRot);
                    _activeSoloChannelHW = go.GetComponent<HealingWave>();
                }
                _activeSoloChannelHW?.ChannelTick(rawDamage);
            }
            DebugLogger.Log(DebugLogger.Category.SpellCast, $"Channel tick — {_active.spellName}");
            return;
        }

        // ── Target-locked spells (e.g. Chain Lightning) ───────────────────────
        if (_active.spellType == SpellType.TargetLocked)
        {
            FireTargetLockedSpell(firePos, fireRot, rawDamage);
            Debug.Log($"[SpellCaster] Fired target-locked {_active.spellName}");
            return;
        }

        // ── One-shot spells (Cast, Buff, etc.) ────────────────────────────────
        if (networkActive)
        {
            if (!_active.prefab.TryGetComponent<NetworkObject>(out NetworkObject _))
            {
                Debug.LogWarning($"[SpellCaster] '{_active.spellName}' prefab has no NetworkObject " +
                                 "component — register it in NetworkManager's prefab list.");
                return;
            }

            string hitEffectName = _active.hitEffect != null ? _active.hitEffect.name : "";
            foreach (Quaternion rot in GetShotRotations(fireRot, count))
                SpawnProjectileServerRpc(firePos, rot, rawDamage, _active.prefab.name, hitEffectName);
        }
        else
        {
            foreach (Quaternion rot in GetShotRotations(fireRot, count))
            {
                GameObject go = Instantiate(_active.prefab, firePos, rot);
                if (go.TryGetComponent<Fireball>(out Fireball fb))
                {
                    fb.precomputedDamage = rawDamage;
                    fb.hitEffect = _active.hitEffect;
                }
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

        if (instance.TryGetComponent<Fireball>(out Fireball fb))
        {
            fb.precomputedDamage = rawDamage;
            if (!string.IsNullOrEmpty(hitEffectName))
                fb.hitEffect = FindRegisteredGameObject(hitEffectName);
        }
        else if (instance.TryGetComponent<HealingWave>(out HealingWave hw))
        {
            hw.precomputedHeal = rawDamage;
        }

        instance.Spawn(true);
    }

    /// <summary>
    /// Spawns the persistent channel VFX once and fires the first heal tick.
    /// Stores the instance so subsequent ticks can call ChannelTick() on it.
    /// </summary>
    [Rpc(SendTo.Server)]
    private void SpawnChannelObjectServerRpc(Vector3 pos, Quaternion rot, float rawDamage, string prefabName)
    {
        NetworkObject prefab = FindRegisteredPrefab(prefabName);
        if (prefab == null)
        {
            Debug.LogWarning($"[SpellCaster] No registered NetworkObject prefab named '{prefabName}'.");
            return;
        }

        NetworkObject instance = Instantiate(prefab, pos, rot);
        instance.Spawn(true);

        if (instance.TryGetComponent<HealingWave>(out HealingWave hw))
        {
            _activeChannelNetObj = instance;
            hw.casterNetworkObjectId = NetworkObjectId;
            hw.ChannelTick(rawDamage);
        }
    }

    /// <summary>Forwards a heal tick to the persistent channel VFX instance.</summary>
    [Rpc(SendTo.Server)]
    private void TickChannelServerRpc(float rawDamage)
    {
        if (_activeChannelNetObj == null) return;
        if (_activeChannelNetObj.TryGetComponent<HealingWave>(out HealingWave hw))
            hw.ChannelTick(rawDamage);
    }

    /// <summary>Despawns the persistent channel VFX when the player releases or is interrupted.</summary>
    [Rpc(SendTo.Server)]
    private void StopChannelServerRpc()
    {
        if (_activeChannelNetObj == null) return;
        _activeChannelNetObj.Despawn(true);
        _activeChannelNetObj = null;
    }

    // ─── Target Confirmation ──────────────────────────────────────────────────

    /// <summary>Called by TargetSelector.OnTargetSelected when the player clicks a valid enemy.</summary>
    void OnTargetConfirmed(Transform target)
    {
        if (_state != CastState.AwaitingTarget || _active == null) return;

        // Cooldown starts now that a target has been confirmed.
        if (_active.cooldown > 0f)
            _cooldownEndTimes[_active] = Time.time + _active.cooldown;

        _telegraphProjector?.Show(_active, transform);

        if (_active.castStartDelay > 0f)
        {
            _state = CastState.PreCast;
            _timer = _active.castStartDelay;
            Debug.Log($"[SpellCaster] Pre-cast {_active.spellName} ({_active.castStartDelay}s windup)");
            return;
        }

        if (_active.castTime > 0f)
        {
            _state = CastState.Pending;
            _timer = _active.castTime;
            if (_active.castSound != null && _audioSource != null)
                _audioSource.PlayOneShot(_active.castSound);
            OnCastBegin?.Invoke(_active, _active.castTime);
            DebugLogger.Log(DebugLogger.Category.SpellCast,
                $"Casting {_active.spellName}... ({_active.castTime}s)");
            return;
        }

        // Instant cast — fire immediately.
        FireSpell();
        _telegraphProjector?.Hide();
        _state  = CastState.Idle;
        _active = null;
        _timer  = 0f;
    }

    // ─── Target-Locked Fire ───────────────────────────────────────────────────

    void FireTargetLockedSpell(Vector3 firePos, Quaternion fireRot, float rawDamage)
    {
        if (_active == null || _active.prefab == null) return;

        string hitEffectName = _active.hitEffect != null ? _active.hitEffect.name : "";
        bool networkActive   = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        if (networkActive)
        {
            if (!_active.prefab.TryGetComponent<NetworkObject>(out NetworkObject _))
            {
                Debug.LogWarning($"[SpellCaster] '{_active.spellName}' prefab has no NetworkObject — register it in NetworkManager's prefab list.");
                return;
            }

            ulong targetId = _targetSelector?.SelectedNetworkObject != null
                ? _targetSelector.SelectedNetworkObject.NetworkObjectId
                : ulong.MaxValue;

            SpawnTargetLockedServerRpc(firePos, fireRot, rawDamage, _active.prefab.name,
                hitEffectName, targetId, _active.chainCount, _active.chainRadius,
                _active.chainDamageFalloff, _active.chainTravelTime, _active.chainJumpDelay);
        }
        else
        {
            GameObject go = Instantiate(_active.prefab, firePos, fireRot);
            if (go.TryGetComponent<ChainLightning>(out ChainLightning cl))
            {
                cl.precomputedDamage  = rawDamage;
                cl.hitEffect          = _active.hitEffect;
                cl.soloTarget         = _targetSelector?.SelectedTarget;
                cl.chainCount         = _active.chainCount;
                cl.chainRadius        = _active.chainRadius;
                cl.chainDamageFalloff = _active.chainDamageFalloff;
                cl.travelTime         = _active.chainTravelTime;
                cl.jumpDelay          = _active.chainJumpDelay;
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void SpawnTargetLockedServerRpc(Vector3 pos, Quaternion rot, float rawDamage,
        string prefabName, string hitEffectName, ulong targetNetObjId,
        int chainCount, float chainRadius, float chainDamageFalloff, float chainTravelTime, float chainJumpDelay)
    {
        NetworkObject prefab = FindRegisteredPrefab(prefabName);
        if (prefab == null)
        {
            Debug.LogWarning($"[SpellCaster] No registered NetworkObject prefab named '{prefabName}'.");
            return;
        }

        NetworkObject instance = Instantiate(prefab, pos, rot);
        if (instance.TryGetComponent<ChainLightning>(out ChainLightning cl))
        {
            cl.precomputedDamage     = rawDamage;
            cl.targetNetworkObjectId = targetNetObjId;
            cl.chainCount            = chainCount;
            cl.chainRadius           = chainRadius;
            cl.chainDamageFalloff    = chainDamageFalloff;
            cl.travelTime            = chainTravelTime;
            cl.jumpDelay             = chainJumpDelay;
            if (!string.IsNullOrEmpty(hitEffectName))
                cl.hitEffect = FindRegisteredGameObject(hitEffectName);
        }

        instance.Spawn(true);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

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

    Quaternion[] GetShotRotations(Quaternion baseRot, int count)
    {
        if (count == 1) return new[] { baseRot };

        var   rots       = new Quaternion[count];
        float halfSpread = _active.spreadAngle * 0.5f;
        float step       = _active.spreadAngle / (count - 1);
        for (int i = 0; i < count; i++)
        {
            float yaw = -halfSpread + step * i;
            rots[i]   = baseRot * Quaternion.Euler(0f, yaw, 0f);
        }
        return rots;
    }

    float ComputeRawDamage(SpellData spell, int spellRank = 0)
    {
        float baseDmg = spell.BaseDamage + Mathf.Max(0, spellRank - 1) * spell.damagePerSkillRank;

        bool isFire      = spell.school == SpellSchool.Fire;
        bool isHeal      = spell.school == SpellSchool.Healing;
        bool isLightning = spell.school == SpellSchool.Lightning;

        float spellBonus       = SkillTreeManager.Instance?.TotalSpellDamageBonus      ?? 0f;
        float fireBonus        = isFire      ? (SkillTreeManager.Instance?.TotalFireDamageBonus         ?? 0f) : 0f;
        float firePctBonus     = isFire      ? (SkillTreeManager.Instance?.TotalFireDamagePctBonus      ?? 0f) : 0f;
        float healBonus        = isHeal      ? (SkillTreeManager.Instance?.TotalHealBonus               ?? 0f) : 0f;
        float healPctBonus     = isHeal      ? (SkillTreeManager.Instance?.TotalHealPctBonus            ?? 0f) : 0f;
        float lightningBonus   = isLightning ? (SkillTreeManager.Instance?.TotalLightningDamageBonus    ?? 0f) : 0f;
        float lightningPct     = isLightning ? (SkillTreeManager.Instance?.TotalLightningDamagePctBonus ?? 0f) : 0f;
        float intMult          = ExperienceManager.Instance?.SpellDamageMultiplier ?? 1f;

        // Equipment bonuses — only valid on the owner's client (singleplayer or pre-server-RPC path)
        float equipSpell = PlayerInventory.Instance?.TotalBonusSpellPower  ?? 0f;
        float equipFire  = isFire ? (PlayerInventory.Instance?.TotalBonusFireDamage ?? 0f) : 0f;

        // Formula: (Base + flat bonuses) × INT multiplier × (1 + school%)
        return (baseDmg + spellBonus + equipSpell + fireBonus + equipFire + healBonus + lightningBonus)
               * intMult * (1f + firePctBonus + healPctBonus + lightningPct);
    }

    bool IsCastHeld()
    {
        if (Keyboard.current != null)
            for (int i = 0; i < 10; i++)
            {
                Key key = i == 9 ? Key.Digit0 : (Key)((int)Key.Digit1 + i);
                if (Keyboard.current[key].isPressed) return true;
            }

        if (Gamepad.current != null && Gamepad.current.rightShoulder.isPressed) return true;
        if (Gamepad.current != null && Gamepad.current.leftShoulder.isPressed)  return true;
        return false;
    }
}
