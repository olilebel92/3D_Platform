using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Add this component to any UI GameObject and pick its role.
// Right-click > "Apply Theme" to preview in the Editor without entering Play mode.
[ExecuteAlways]
public class UIThemeApplier : MonoBehaviour
{
    public enum Role
    {
        // ── Panels ──────────────────────
        PanelBackground,
        PanelDark,
        PanelHeader,
        BorderAccent,
        BorderDim,

        // ── Text ────────────────────────
        TextPrimary,
        TextMuted,
        TextAccent,
        TextDanger,
        TextSuccess,

        // ── Buttons ─────────────────────
        ButtonDefault,
        ButtonPrimary,
        ButtonDanger,

        // ── Progress Bars ────────────────
        BarHealth,
        BarXP,
        BarMana,
        BarStamina,
        BarTrack,
    }

    [SerializeField] UITheme theme;
    [SerializeField] Role    role;

    void Start()      => Apply();
    void OnValidate() => Apply();

    [ContextMenu("Apply Theme")]
    public void Apply()
    {
        if (theme == null) return;

        var img    = GetComponent<Image>();
        var tmp    = GetComponent<TMP_Text>();
        var btn    = GetComponent<Button>();
        var slider = GetComponent<Slider>();

        switch (role)
        {
            // ── Panels ──────────────────────────────────────────
            case Role.PanelBackground: SetImage(img, theme.panelBackground); break;
            case Role.PanelDark:       SetImage(img, theme.panelDark);       break;
            case Role.PanelHeader:     SetImage(img, theme.panelHeader);     break;
            case Role.BorderAccent:    SetImage(img, theme.borderAccent);    break;
            case Role.BorderDim:       SetImage(img, theme.borderDim);       break;

            // ── Text ────────────────────────────────────────────
            case Role.TextPrimary: SetText(tmp, theme.textPrimary); break;
            case Role.TextMuted:   SetText(tmp, theme.textMuted);   break;
            case Role.TextAccent:  SetText(tmp, theme.textAccent);  break;
            case Role.TextDanger:  SetText(tmp, theme.textDanger);  break;
            case Role.TextSuccess: SetText(tmp, theme.textSuccess); break;

            // ── Buttons ─────────────────────────────────────────
            case Role.ButtonDefault: SetButton(btn, img,
                theme.buttonNormal, theme.buttonHover,
                theme.buttonPressed, theme.buttonDisabled,
                theme.textPrimary); break;

            case Role.ButtonPrimary: SetButton(btn, img,
                theme.buttonPrimaryNormal, theme.buttonPrimaryHover,
                theme.buttonPrimaryPressed, theme.buttonDisabled,
                theme.buttonPrimaryText); break;

            case Role.ButtonDanger: SetButton(btn, img,
                theme.buttonDangerNormal, theme.buttonDangerHover,
                theme.buttonDangerHover, theme.buttonDisabled,
                theme.textPrimary); break;

            // ── Progress Bars ────────────────────────────────────
            case Role.BarHealth:  SetSliderFill(slider, theme.barHealth);  break;
            case Role.BarXP:      SetSliderFill(slider, theme.barXP);      break;
            case Role.BarMana:    SetSliderFill(slider, theme.barMana);    break;
            case Role.BarStamina: SetSliderFill(slider, theme.barStamina); break;
            case Role.BarTrack:   SetImage(img, theme.barTrack);           break;
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    static void SetImage(Image img, Color color)
    {
        if (img == null) return;
        img.color = color;
    }

    static void SetText(TMP_Text tmp, Color color)
    {
        if (tmp == null) return;
        tmp.color = color;
    }

    static void SetButton(Button btn, Image img,
        Color normal, Color hover, Color pressed, Color disabled, Color textColor)
    {
        if (btn != null)
        {
            btn.colors = new ColorBlock
            {
                normalColor      = normal,
                highlightedColor = hover,
                pressedColor     = pressed,
                selectedColor    = hover,
                disabledColor    = disabled,
                colorMultiplier  = 1f,
                fadeDuration     = 0.1f
            };
        }

        if (img != null) img.color = normal;

        var label = btn != null
            ? btn.GetComponentInChildren<TMP_Text>()
            : null;

        if (label != null) label.color = textColor;
    }

    static void SetSliderFill(Slider slider, Color color)
    {
        if (slider == null || slider.fillRect == null) return;
        var fill = slider.fillRect.GetComponent<Image>();
        if (fill != null) fill.color = color;
    }
}
