using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Assigns a random RGB tint to all Renderers on this enemy prefab.
/// Color is synced to all clients via NetworkVariable.
/// Attach to the enemy prefab root (alongside EnemyAI).
/// </summary>
public class EnemyColorRandomizer : NetworkBehaviour
{
    [Header("Color Range")]
    [Tooltip("Minimum value for each RGB channel (0–1).")]
    [SerializeField] private float minChannelValue = 0.2f;
    [Tooltip("Maximum value for each RGB channel (0–1).")]
    [SerializeField] private float maxChannelValue = 1f;

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
            ApplyColor(RandomColor());
    }

    public override void OnNetworkSpawn()
    {
        _syncedColor.OnValueChanged += OnColorChanged;

        if (IsServer)
        {
            _syncedColor.Value = RandomColor();
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

    private Color RandomColor()
    {
        return new Color(
            Random.Range(minChannelValue, maxChannelValue),
            Random.Range(minChannelValue, maxChannelValue),
            Random.Range(minChannelValue, maxChannelValue)
        );
    }
}
