using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Assigns a tint to all Renderers on this enemy prefab.
/// Random mode picks a new color every spawn. Fixed mode uses a chosen color.
/// Color is synced to all clients via NetworkVariable.
/// Attach to the enemy prefab root (alongside EnemyAI).
/// </summary>
public class EnemyColorRandomizer : NetworkBehaviour
{
    public enum ColorMode { Random, Fixed }

    [Header("Mode")]
    [Tooltip("Random: new tint each spawn. Fixed: always use the color below.")]
    [SerializeField] private ColorMode _colorMode = ColorMode.Random;

    [Header("Random Mode")]
    [Tooltip("Minimum value for each RGB channel (0–1).")]
    [SerializeField] private float minChannelValue = 0.2f;
    [Tooltip("Maximum value for each RGB channel (0–1).")]
    [SerializeField] private float maxChannelValue = 1f;

    [Header("Fixed Mode")]
    [Tooltip("Base color used when mode is Fixed.")]
    [SerializeField] private Color _fixedColor = Color.red;

    [Header("Tint (both modes)")]
    [Tooltip("How strongly the tint is applied. 0 = pure white (no tint), 1 = full color.")]
    [Range(0f, 1f)]
    [SerializeField] private float tintStrength = 0.5f;

    private readonly NetworkVariable<Color> _syncedColor = new(
        Color.white,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Renderer[] _renderers;

    // ─── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
    }

    private void Start()
    {
        // Single player: NetworkObject is never Spawn()ed so OnNetworkSpawn never fires.
        // Apply a local random color here as a fallback.
        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (!networked)
            ApplyColor(PickColor());
    }

    public override void OnNetworkSpawn()
    {
        _syncedColor.OnValueChanged += OnColorChanged;

        if (IsServer)
        {
            _syncedColor.Value = PickColor();
        }

        ApplyColor(_syncedColor.Value);
    }

    public override void OnNetworkDespawn()
    {
        _syncedColor.OnValueChanged -= OnColorChanged;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private void OnColorChanged(Color previous, Color current) => ApplyColor(current);

    private void ApplyColor(Color color)
    {
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            // renderer.material creates a per-instance copy — guaranteed to work
            // with Standard shader regardless of GPU instancing settings.
            r.material.color = color;
        }
    }

    private Color PickColor()
    {
        Color raw = _colorMode == ColorMode.Fixed
            ? _fixedColor
            : new Color(
                Random.Range(minChannelValue, maxChannelValue),
                Random.Range(minChannelValue, maxChannelValue),
                Random.Range(minChannelValue, maxChannelValue));
        return Color.Lerp(Color.white, raw, tintStrength);
    }
}
