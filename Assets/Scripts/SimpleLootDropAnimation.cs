using System.Collections;
using UnityEngine;

/// <summary>
/// Lightweight drop-burst animation for plain prefabs (HP orbs, XP orbs, etc.)
/// that are not NetworkObjects. Mirrors LootDropAnimation's arc but runs
/// entirely client/host-side — no NGO required.
/// Call PreInit(targetPos) immediately after Instantiate, before the frame ends.
/// </summary>
public class SimpleLootDropAnimation : MonoBehaviour
{
    [Header("Animation")]
    [Tooltip("Time in seconds to reach the target position.")]
    [SerializeField] private float duration = 0.45f;

    [Tooltip("Peak height above the start point during the arc.")]
    [SerializeField] private float arcHeight = 1.1f;

    [Tooltip("Ease curve applied to horizontal movement.")]
    [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Vector3  _target;
    private Collider _col;

    void Awake() => _col = GetComponent<Collider>();

    void Start()
    {
        if (_target != Vector3.zero)
            StartCoroutine(Burst(transform.position, _target));
    }

    /// <summary>Set scatter target. Must be called before the first frame.</summary>
    public void PreInit(Vector3 target) => _target = target;

    private IEnumerator Burst(Vector3 from, Vector3 to)
    {
        if (_col != null) _col.enabled = false;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / duration);
            float curve = moveCurve.Evaluate(t);

            Vector3 pos = Vector3.Lerp(from, to, curve);
            pos.y += arcHeight * Mathf.Sin(t * Mathf.PI);
            transform.position = pos;

            yield return null;
        }

        transform.position = to;
        if (_col != null) _col.enabled = true;
    }
}
