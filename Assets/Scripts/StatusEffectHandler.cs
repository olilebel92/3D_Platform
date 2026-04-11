using UnityEngine;

/// <summary>
/// Tracks and applies status effects on an entity.
/// Currently supports: Stun (always interrupts casts; freezes all input).
///
/// Place this component on the same GameObject as SpellCaster / PlayerController.
/// To apply a stun from a spell hit: targetObject.GetComponent&lt;StatusEffectHandler&gt;()?.ApplyStun(duration)
/// </summary>
public class StatusEffectHandler : MonoBehaviour
{
    // ─── State ────────────────────────────────────────────────────────────────

    private float _stunRemaining = 0f;

    /// <summary>True while a stun effect is active on this entity.</summary>
    public bool IsStunned => _stunRemaining > 0f;

    // ─── Dependencies ─────────────────────────────────────────────────────────

    private SpellCaster _spellCaster;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        _spellCaster = GetComponent<SpellCaster>();
    }

    void Update()
    {
        if (_stunRemaining <= 0f) return;

        _stunRemaining -= Time.deltaTime;
        if (_stunRemaining <= 0f)
        {
            _stunRemaining = 0f;
            Debug.Log("[StatusEffectHandler] Stun expired.");
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a stun of the given duration. If already stunned, extends to the
    /// longer of the two durations (no stacking — highest value wins).
    /// Immediately cancels any active cast; PlayerController blocks all movement
    /// while IsStunned is true.
    /// </summary>
    public void ApplyStun(float duration)
    {
        if (duration <= 0f) return;

        bool wasStunned = IsStunned;
        _stunRemaining  = Mathf.Max(_stunRemaining, duration);

        if (!wasStunned)
            Debug.Log($"[StatusEffectHandler] Stunned for {duration:F2}s.");

        // Stun always interrupts a cast regardless of any grace period.
        _spellCaster?.CancelByStun();
    }

    /// <summary>Removes the stun immediately (e.g. from a cleanse effect).</summary>
    public void ClearStun()
    {
        if (_stunRemaining <= 0f) return;
        _stunRemaining = 0f;
        Debug.Log("[StatusEffectHandler] Stun cleared.");
    }
}
