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
    SerializedProperty _itemPool;
    SerializedProperty _randomItemWaveOverride;
    SerializedProperty _playerTag;
    SerializedProperty _lifetime;
    SerializedProperty _pickupParticles;
    SerializedProperty _pickupSound;
    SerializedProperty _interactionLabelHeight;
    SerializedProperty _interactionLabelFontSize;
    SerializedProperty _interactionLabelColor;

    static readonly string[] TypeLabels = { "XP Reward", "HP Potion", "Mana Potion", "Material / Item", "Items (Random from Pool)", "Random Item (Procedural)" };
    static readonly Color[]  TypeColors =
    {
        new Color(1f,   0.85f, 0.2f),    // XP
        new Color(0.4f, 0.9f,  0.4f),    // HP
        new Color(0.3f, 0.6f,  1f),      // Mana
        new Color(0.8f, 0.55f, 0.25f),   // Material
        new Color(0.7f, 0.4f,  0.9f),    // Items pool
        new Color(0.95f, 0.75f, 0.3f),   // RandomItem — gold
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
        _itemPool           = serializedObject.FindProperty("itemPool");
        _randomItemWaveOverride = serializedObject.FindProperty("randomItemWaveOverride");
        _playerTag      = serializedObject.FindProperty("playerTag");
        _lifetime       = serializedObject.FindProperty("lifetime");
        _pickupParticles = serializedObject.FindProperty("pickupParticles");
        _pickupSound    = serializedObject.FindProperty("pickupSound");
        _interactionLabelHeight   = serializedObject.FindProperty("interactionLabelHeight");
        _interactionLabelFontSize = serializedObject.FindProperty("interactionLabelFontSize");
        _interactionLabelColor    = serializedObject.FindProperty("interactionLabelColor");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Loot Type", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_lootType, new GUIContent("Type"));

        int typeIndex = _lootType.enumValueIndex;

        EditorGUILayout.Space(4);
        Color prev = GUI.backgroundColor;
        GUI.backgroundColor = TypeColors[typeIndex];
        EditorGUILayout.HelpBox(TypeLabels[typeIndex], MessageType.None);
        GUI.backgroundColor = prev;

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
                break;

            case LootType.Material:
                EditorGUILayout.PropertyField(_itemReward, new GUIContent("Item Data"));
                if (_itemReward.objectReferenceValue == null)
                    EditorGUILayout.HelpBox("Assign an ItemData ScriptableObject.", MessageType.Warning);
                break;

            case LootType.Items:
                EditorGUILayout.PropertyField(_itemPool, new GUIContent("Item Pool"), true);
                DrawRarityQuickFillButtons(_itemPool);
                if (PoolIsEmptyOrAllNull(_itemPool))
                    EditorGUILayout.HelpBox("Add at least one non-null ItemData to the pool. " +
                                            "On spawn, one entry is chosen at random (server-authoritative).", MessageType.Warning);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Interaction Prompt", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Items loot is interactable: the player must press F (or gamepad South) " +
                                        "while in range to pick it up. The label below floats above the item.", MessageType.Info);
                EditorGUILayout.PropertyField(_interactionLabelHeight,   new GUIContent("Label Height"));
                EditorGUILayout.PropertyField(_interactionLabelFontSize, new GUIContent("Label Font Size"));
                EditorGUILayout.PropertyField(_interactionLabelColor,    new GUIContent("Label Color"));
                break;

            case LootType.RandomItem:
                EditorGUILayout.PropertyField(_randomItemWaveOverride, new GUIContent("Wave Override"));
                EditorGUILayout.HelpBox(
                    "On spawn, the server rolls a procedural item via ItemGenerator using the current wave " +
                    "(or the override above). All clients see the same SubType model + Rarity glow before pickup. " +
                    "The collecting client generates the final item locally with rolled stat lines.",
                    MessageType.Info);
                break;
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_playerTag,  new GUIContent("Player Tag"));
        EditorGUILayout.PropertyField(_lifetime,   new GUIContent("Lifetime (s)"));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Effects (optional)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_pickupParticles, new GUIContent("Pickup Particles"));
        EditorGUILayout.PropertyField(_pickupSound,     new GUIContent("Pickup Sound"));

        serializedObject.ApplyModifiedProperties();
    }

    private static bool PoolIsEmptyOrAllNull(SerializedProperty list)
    {
        if (list == null || !list.isArray || list.arraySize == 0) return true;
        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue != null) return false;
        }
        return true;
    }

    // ─── Rarity Quick-Fill ────────────────────────────────────────────────────
    // Iterates all RarityData assets in the project. For each rarity asset, adds
    // a button that bulk-appends every ItemData whose rarity matches.
    private static void DrawRarityQuickFillButtons(SerializedProperty list)
    {
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Quick Fill", EditorStyles.miniBoldLabel);

        Color prevBg = GUI.backgroundColor;

        EditorGUILayout.BeginHorizontal();

        // Find all RarityData assets sorted by sortOrder.
        string[] rarityGuids = AssetDatabase.FindAssets("t:RarityData");
        var rarities = new System.Collections.Generic.List<RarityData>();
        foreach (string g in rarityGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            RarityData r = AssetDatabase.LoadAssetAtPath<RarityData>(path);
            if (r != null) rarities.Add(r);
        }
        rarities.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));

        foreach (RarityData rarity in rarities)
        {
            GUI.backgroundColor = rarity.color;
            if (GUILayout.Button("+ " + rarity.displayName, EditorStyles.miniButton))
                AddAllOfRarity(list, rarity);
        }
        GUI.backgroundColor = prevBg;

        if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(60)))
        {
            list.ClearArray();
        }
        EditorGUILayout.EndHorizontal();
    }

    private static void AddAllOfRarity(SerializedProperty list, RarityData rarity)
    {
        var existing = new System.Collections.Generic.HashSet<Object>();
        for (int i = 0; i < list.arraySize; i++)
        {
            Object obj = list.GetArrayElementAtIndex(i).objectReferenceValue;
            if (obj != null) existing.Add(obj);
        }

        string[] guids = AssetDatabase.FindAssets("t:ItemData");
        int added = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ItemData data = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (data == null) continue;
            if (data.rarity != rarity) continue;
            if (existing.Contains(data)) continue;

            int newIndex = list.arraySize;
            list.InsertArrayElementAtIndex(newIndex);
            list.GetArrayElementAtIndex(newIndex).objectReferenceValue = data;
            existing.Add(data);
            added++;
        }

        Debug.Log($"[LootPickupEditor] Added {added} {rarity.displayName} ItemData asset(s) to pool.");
    }
}
