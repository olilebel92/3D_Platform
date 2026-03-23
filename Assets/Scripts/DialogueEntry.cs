using UnityEngine;

/// <summary>
/// One line of dialogue or tutorial text shown in the popup.
/// Pass an array of these to PopupManager.Instance.Show().
/// </summary>
[System.Serializable]
public class DialogueEntry
{
    [Tooltip("The message to display in the popup.")]
    [TextArea(2, 4)]
    public string message = "";

    [Tooltip("Override the auto-advance timer for this entry. Set to 0 to use the manager default.")]
    public float customDuration = 0f;
}