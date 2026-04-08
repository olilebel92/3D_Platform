using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector for LootPickup.
/// Shows only the reward fields that apply to the selected LootType.
/// </summary>
[CustomEditor(typeof(LootPickup))]
public class LootPickupEditor : Editor
{
    // ─── Serialized Properties ────────────────────────────────────────────────
    SerializedProperty _lootType;
    SerializedProperty _xpReward;
    SerializedProperty _hpRestoreMode;
    SerializedProperty _hpReward;
    SerializedProperty _hpRestorePercent;
    SerializedProperty _manaRestoreMode;
    SerializedProperty _manaReward;
    SerializedProperty _manaRestorePercent;
    SerializedProperty _itemReward;
    SerializedProperty _playerTag;
    SerializedProperty _lifetime;
    SerializedProperty _pickupParticles;
    SerializedProperty _pickupSound;

    // ─── Type Labels & Colors ────────────────────────────────────────────────
    static readonly string[] TypeLabels = { "XP Reward", "HP Potion", "Mana Potion (WIP)", "Material / Item" };
    static readonly Color[]  TypeColors =
    {
        new Color(1f,   0.85f, 0.2f),   // XP      — gold
        new Color(0.4f, 0.9f,  0.4f),   // HP      — green
        new Color(0.3f, 0.6f,  1f),     // Mana    — blue
        new Color(0.8f, 0.55f, 0.25f),  // Material — orange
    };

    void OnEnable()
    {
        _lootType           = serializedObject.FindProperty("lootType");
        _xpReward           = serializedObject.FindProperty("xpReward");
        _hpRestoreMode      = serializedObject.FindProperty("hpRestoreMode");
        _hpReward           = serializedObject.FindProperty("hpReward");
        _hpRestorePercent   = serializedObject.FindProperty("hpRestorePercent");
        _manaRestoreMode    = serializedObject.FindProperty("manaRestoreMode");
        _manaReward         = serializedObject.FindProperty("manaReward");
        _manaRestorePercent = serializedObject.FindProperty("manaRestorePercent");
        _itemReward         = serializedObject.FindProperty("itemReward");
        _playerTag      = serializedObject.FindProperty("playerTag");
        _lifetime       = serializedObject.FindProperty("lifetime");
        _pickupParticles = serializedObject.FindProperty("pickupParticles");
        _pickupSound    = serializedObject.FindProperty("pickupSound");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ── Type selector ─────────────────────────────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Loot Type", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_lootType, new GUIContent("Type"));

        int typeIndex = _lootType.enumValueIndex;

        // ── Colored banner showing current type ───────────────────────────────
        EditorGUILayout.Space(4);
        Color prev = GUI.backgroundColor;
        GUI.backgroundColor = TypeColors[typeIndex];
        EditorGUILayout.HelpBox(TypeLabels[typeIndex], MessageType.None);
        GUI.backgroundColor = prev;

        // ── Reward fields — only the relevant one ─────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Reward", EditorStyles.boldLabel);

        switch ((LootType)typeIndex)
        {
            case LootType.XPReward:
                EditorGUILayout.PropertyField(_xpReward, new GUIContent("XP Amount"));
                break;

            case LootType.HPPotion:
                EditorGUILayout.PropertyField(_hpRestoreMode, new GUIContent("Restore Mode"));
                var hpMode = (RestoreMode)_hpRestoreMode.enumValueIndex;
                if (hpMode == RestoreMode.Flat || hpMode == RestoreMode.Both)
                    EditorGUILayout.PropertyField(_hpReward, new GUIContent("HP Amount"));
                if (hpMode == RestoreMode.Percent || hpMode == RestoreMode.Both)
                    EditorGUILayout.PropertyField(_hpRestorePercent, new GUIContent("Restore %"));
                break;

            case LootType.ManaPotion:
                EditorGUILayout.PropertyField(_manaRestoreMode, new GUIContent("Restore Mode"));
                var manaMode = (RestoreMode)_manaRestoreMode.enumValueIndex;
                if (manaMode == RestoreMode.Flat || manaMode == RestoreMode.Both)
                    EditorGUILayout.PropertyField(_manaReward, new GUIContent("Mana Amount"));
                if (manaMode == RestoreMode.Percent || manaMode == RestoreMode.Both)
                    EditorGUILayout.PropertyField(_manaRestorePercent, new GUIContent("Restore %"));
                EditorGUILayout.HelpBox("ManaSystem is not yet implemented. Values are saved and ready to wire up.", MessageType.Info);
                break;

            case LootType.Material:
                EditorGUILayout.PropertyField(_itemReward, new GUIContent("Item Data"));
                if (_itemReward.objectReferenceValue == null)
                    EditorGUILayout.HelpBox("Assign an ItemData ScriptableObject.", MessageType.Warning);
                break;
        }

        // ── Common settings ───────────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_playerTag,  new GUIContent("Player Tag"));
        EditorGUILayout.PropertyField(_lifetime,   new GUIContent("Lifetime (s)"));

        // ── Effects ───────────────────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Effects (optional)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_pickupParticles, new GUIContent("Pickup Particles"));
        EditorGUILayout.PropertyField(_pickupSound,     new GUIContent("Pickup Sound"));

        serializedObject.ApplyModifiedProperties();
    }
}
