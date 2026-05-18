using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Collections;

/// <summary>
/// Draggable HUD nameplate showing the local player's name, level, HP, and Stamina.
/// Wire the UI refs in the Inspector, then assign this to PlayerUILinker.nameplate.
/// Position is persisted in PlayerPrefs so it survives Play Mode restarts.
/// </summary>
public class PlayerNameplateUI : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    // ─── Inspector — UI References ────────────────────────────────────────────

    [Header("Name & Level")]
    [Tooltip("TMP label that shows the player's name.")]
    public TextMeshProUGUI nameText;

    [Tooltip("TMP label showing the current level (e.g. 'Lv. 5').")]
    public TextMeshProUGUI levelText;

    [Header("HP")]
    [Tooltip("Fill Image for the HP bar — set Image Type to Filled, Fill Method to Horizontal.")]
    public Image hpBarFill;

    [Tooltip("TMP label showing 'currentHP / maxHP'.")]
    public TextMeshProUGUI hpText;

    [Header("Stamina")]
    [Tooltip("Fill Image for the stamina bar — set Image Type to Filled, Fill Method to Horizontal.")]
    public Image staminaBarFill;

    [Tooltip("TMP label showing stamina as a percentage (e.g. '73%').")]
    public TextMeshProUGUI staminaText;

    [Header("Mana")]
    [Tooltip("Fill Image for the mana bar — set Image Type to Filled, Fill Method to Horizontal.")]
    public Image manaBarFill;

    [Tooltip("TMP label showing 'currentMana / maxMana'.")]
    public TextMeshProUGUI manaText;

    // ─── PlayerPrefs Keys ─────────────────────────────────────────────────────

    private const string PrefKeyX = "Nameplate_PosX";
    private const string PrefKeyY = "Nameplate_PosY";

    // ─── Private State ────────────────────────────────────────────────────────

    private RectTransform     _rectTransform;
    private Canvas            _rootCanvas;
    private HealthSystem      _health;
    private StaminaSystem     _stamina;
    private ManaSystem        _mana;
    private ExperienceManager _xp;
    private LobbyPlayer       _lobbyPlayer;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();

        // Force center anchor/pivot so anchoredPosition aligns with canvas local-space
        // coords returned by RectTransformUtility. Any other anchor causes the panel
        // to teleport off-screen on the first drag frame.
        _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _rectTransform.pivot     = new Vector2(0.5f, 0.5f);

        foreach (Canvas c in GetComponentsInParent<Canvas>())
            if (c.isRootCanvas) { _rootCanvas = c; break; }

        if (PlayerPrefs.HasKey(PrefKeyX))
            _rectTransform.anchoredPosition = new Vector2(
                PlayerPrefs.GetFloat(PrefKeyX),
                PlayerPrefs.GetFloat(PrefKeyY));
    }

    // ─── Public Wiring API ────────────────────────────────────────────────────

    /// <summary>Called by PlayerUILinker once the local PlayerController is found.</summary>
    public void WirePlayer(PlayerController pc)
    {
        _health  = pc.GetComponent<HealthSystem>();
        _stamina = pc.GetComponent<StaminaSystem>();
        _mana    = pc.GetComponent<ManaSystem>();
        _xp      = pc.GetComponent<ExperienceManager>();

        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (!networked)
            SetName(PlayerPrefs.GetString("PlayerName", "Player"));
        else
        {
            SetName("Player");
            StartCoroutine(BindLobbyPlayerCoroutine(pc));
        }
    }

    // ─── Name Resolution (mirrors PlayerNameTag) ──────────────────────────────

    IEnumerator BindLobbyPlayerCoroutine(PlayerController pc)
    {
        NetworkBehaviour nb = pc.GetComponent<NetworkBehaviour>();
        if (nb == null) yield break;

        float elapsed = 0f;
        while (_lobbyPlayer == null && elapsed < 5f)
        {
            foreach (var lp in FindObjectsByType<LobbyPlayer>(FindObjectsSortMode.None))
            {
                if (lp.OwnerClientId == nb.OwnerClientId)
                {
                    _lobbyPlayer = lp;
                    SetName(lp.PlayerName.Value.ToString());
                    lp.PlayerName.OnValueChanged += OnNameChanged;
                    yield break;
                }
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    void OnDestroy()
    {
        if (_lobbyPlayer != null)
            _lobbyPlayer.PlayerName.OnValueChanged -= OnNameChanged;
    }

    void OnNameChanged(FixedString64Bytes previous, FixedString64Bytes current)
        => SetName(current.ToString());

    void SetName(string value) { if (nameText != null) nameText.text = value; }

    // ─── Update — Poll Stats ──────────────────────────────────────────────────

    void Update()
    {
        if (_xp != null && levelText != null)
            levelText.text = $"Lv. {_xp.currentLevel}";

        if (_health != null)
        {
            if (hpBarFill != null)
                hpBarFill.fillAmount = _health.currentHealth / _health.maxHealth;
            if (hpText != null)
                hpText.text = $"{_health.currentHealth:0} / {_health.maxHealth:0}";
        }

        if (_stamina != null)
        {
            float ratio = _stamina.CurrentStamina / _stamina.maxStamina;
            if (staminaBarFill != null) staminaBarFill.fillAmount = ratio;
            if (staminaText   != null)  staminaText.text = $"{ratio * 100f:0}%";
        }

        if (_mana != null)
        {
            if (manaBarFill != null)
                manaBarFill.fillAmount = _mana.maxMana > 0f ? _mana.CurrentMana / _mana.maxMana : 0f;
            if (manaText != null)
                manaText.text = $"{_mana.CurrentMana:0} / {_mana.maxMana:0}";
        }
    }

    // ─── Drag ─────────────────────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        if (_rootCanvas == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rootCanvas.transform as RectTransform,
            eventData.position,
            _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _rootCanvas.worldCamera,
            out Vector2 localPoint);
        _rectTransform.anchoredPosition = localPoint;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        ClampToScreen();
        SavePosition();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    void ClampToScreen()
    {
        if (_rootCanvas == null) return;
        Vector2 canvasSize = (_rootCanvas.transform as RectTransform).rect.size;
        Vector2 panelSize  = _rectTransform.rect.size;
        Vector2 pivot      = _rectTransform.pivot;
        float minX = -canvasSize.x * 0.5f + panelSize.x * pivot.x;
        float maxX =  canvasSize.x * 0.5f - panelSize.x * (1f - pivot.x);
        float minY = -canvasSize.y * 0.5f + panelSize.y * pivot.y;
        float maxY =  canvasSize.y * 0.5f - panelSize.y * (1f - pivot.y);
        Vector2 pos = _rectTransform.anchoredPosition;
        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.y = Mathf.Clamp(pos.y, minY, maxY);
        _rectTransform.anchoredPosition = pos;
    }

    void SavePosition()
    {
        Vector2 pos = _rectTransform.anchoredPosition;
        PlayerPrefs.SetFloat(PrefKeyX, pos.x);
        PlayerPrefs.SetFloat(PrefKeyY, pos.y);
        PlayerPrefs.Save();
    }
}
