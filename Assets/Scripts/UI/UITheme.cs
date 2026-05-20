using UnityEngine;

[CreateAssetMenu(fileName = "UITheme", menuName = "HackNSLASH/UI Theme")]
public class UITheme : ScriptableObject
{
    [Header("Panels")]
    public Color panelBackground  = new Color(0.212f, 0.294f, 0.420f); // #364B6B
    public Color panelDark        = new Color(0.255f, 0.224f, 0.169f); // #41392B
    public Color panelHeader      = new Color(0.165f, 0.227f, 0.333f); // #2A3A55

    [Header("Borders")]
    public Color borderAccent     = new Color(0.922f, 0.612f, 0.000f); // #EB9C00
    public Color borderDim        = new Color(0.588f, 0.455f, 0.196f); // #967432

    [Header("Text")]
    public Color textPrimary      = new Color(0.941f, 0.902f, 0.800f); // #F0E6CC
    public Color textMuted        = new Color(0.604f, 0.667f, 0.733f); // #9AAABB
    public Color textAccent       = new Color(0.922f, 0.612f, 0.000f); // #EB9C00
    public Color textDanger       = new Color(0.800f, 0.200f, 0.200f); // #CC3333
    public Color textSuccess      = new Color(0.227f, 0.604f, 0.227f); // #3A9A3A

    [Header("Buttons — Default (blue)")]
    public Color buttonNormal     = new Color(0.169f, 0.369f, 0.671f); // #2B5EAB
    public Color buttonHover      = new Color(0.227f, 0.435f, 0.800f); // #3A6FCC
    public Color buttonPressed    = new Color(0.102f, 0.290f, 0.541f); // #1A4A8A
    public Color buttonDisabled   = new Color(0.165f, 0.165f, 0.227f); // #2A2A3A

    [Header("Buttons — Primary (gold)")]
    public Color buttonPrimaryNormal  = new Color(0.922f, 0.612f, 0.000f); // #EB9C00
    public Color buttonPrimaryHover   = new Color(1.000f, 0.753f, 0.000f); // #FFC000
    public Color buttonPrimaryPressed = new Color(0.800f, 0.533f, 0.000f); // #CC8800
    public Color buttonPrimaryText    = new Color(0.102f, 0.102f, 0.165f); // #1A1A2A

    [Header("Buttons — Danger (red)")]
    public Color buttonDangerNormal   = new Color(0.478f, 0.102f, 0.102f); // #7A1A1A
    public Color buttonDangerHover    = new Color(0.667f, 0.133f, 0.133f); // #AA2222

    [Header("Progress Bars")]
    public Color barHealth   = new Color(0.800f, 0.200f, 0.200f); // #CC3333
    public Color barXP       = new Color(0.169f, 0.369f, 0.671f); // #2B5EAB
    public Color barMana     = new Color(0.467f, 0.267f, 0.800f); // #7744CC
    public Color barStamina  = new Color(0.227f, 0.604f, 0.227f); // #3A9A3A
    public Color barTrack    = new Color(0.102f, 0.165f, 0.227f); // #1A2A3A

    [Header("Rarity")]
    public Color rarityNormal    = new Color(0.800f, 0.800f, 0.800f);
    public Color rarityUncommon  = new Color(0.118f, 1.000f, 0.000f);
    public Color rarityRare      = new Color(0.000f, 0.439f, 0.867f);
    public Color rarityEpic      = new Color(0.639f, 0.208f, 0.929f);
    public Color rarityLegendary = new Color(1.000f, 0.500f, 0.000f);
    public Color rarityGodly     = new Color(1.000f, 0.100f, 0.100f);

    /// <summary>
    /// Returns the theme's preferred colour for a rarity. Prefers RarityData.color directly;
    /// the per-tier fields below remain as fallback palette references for legacy UI.
    /// </summary>
    public Color GetRarityColor(RarityData rarity)
        => rarity != null ? rarity.color : rarityNormal;
}
