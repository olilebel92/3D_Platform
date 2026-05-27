using UnityEditor;
using UnityEngine;

/// <summary>
/// Renders each ItemGenerator.StatBaseRange list entry as a single row:
/// [Stat dropdown] [Min <value>] [Max <value>] — no per-element foldout.
/// </summary>
[CustomPropertyDrawer(typeof(ItemGenerator.StatBaseRange))]
public class StatBaseRangeDrawer : PropertyDrawer
{
    private const float Spacing       = 4f;
    private const float InnerLabelWidth = 28f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => EditorGUIUtility.singleLineHeight;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        SerializedProperty stat = property.FindPropertyRelative("stat");
        SerializedProperty min  = property.FindPropertyRelative("min");
        SerializedProperty max  = property.FindPropertyRelative("max");

        int   prevIndent     = EditorGUI.indentLevel;
        float prevLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUI.indentLevel       = 0;
        EditorGUIUtility.labelWidth = InnerLabelWidth;

        float statWidth = position.width * 0.5f;
        float numWidth  = (position.width - statWidth - Spacing * 2f) * 0.5f;
        float h         = EditorGUIUtility.singleLineHeight;

        var statRect = new Rect(position.x,                          position.y, statWidth, h);
        var minRect  = new Rect(statRect.xMax + Spacing,             position.y, numWidth,  h);
        var maxRect  = new Rect(minRect.xMax  + Spacing,             position.y, numWidth,  h);

        EditorGUI.PropertyField(statRect, stat, GUIContent.none);
        EditorGUI.PropertyField(minRect,  min);
        EditorGUI.PropertyField(maxRect,  max);

        EditorGUIUtility.labelWidth = prevLabelWidth;
        EditorGUI.indentLevel       = prevIndent;
        EditorGUI.EndProperty();
    }
}
