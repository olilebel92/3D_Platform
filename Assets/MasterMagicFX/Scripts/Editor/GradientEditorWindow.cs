using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace MasterFX
{
    public class GradientEditorWindow : EditorWindow
    {
        string gradientPath = "Assets/Res/MasterMagicFX/Commons/Textures/";
        Gradient gradient = new Gradient();
        Texture2D GradientTexture;
        [MenuItem("Tools/GradientWindow")]
        private static void ShowWindow()
        {
            var window = GetWindow<GradientEditorWindow>();
            window.titleContent = new GUIContent("GradientWindow");
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("Gradient Editor", EditorStyles.boldLabel);
            gradientPath = EditorGUILayout.TextField("Gradient Path", gradientPath);
            EditorGUI.BeginChangeCheck();
            gradient = EditorGUILayout.GradientField("Gradient", gradient);
            if (EditorGUI.EndChangeCheck())
            {
                if (GradientTexture != null)
                    UpdateGradient();
                else
                {
                    AddGradient();
                }
            }
            GradientTexture = EditorGUILayout.ObjectField("Gradient Texture", GradientTexture, typeof(Texture2D), false) as Texture2D;
            //draw a button to save gradient;

            if (GUILayout.Button("Save Gradient"))
            {
                if (GradientTexture != null)
                {
                    UpdateGradient();
                }
            }

            if (GUILayout.Button("Add New Gradient"))
            {
                AddGradient();
            }

        }
        //set the gradient to a texture of 1x256, and save it to the gradient path;
        public void AddGradient()
        {
            Texture2D texture = new Texture2D(256, 1);
            for (int i = 0; i < 256; i++)
            {
                Color color = gradient.Evaluate(i / 255f);
                texture.SetPixel(i + 1, 0, new Color(color.r, color.g, color.b, color.a)); // 确保使用拥有透明度的颜色
            }
            texture.Apply();
            var path = gradientPath + "Tex_Gradient.png";

            path = AssetDatabase.GenerateUniqueAssetPath(path);
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.Refresh();
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.isReadable = true;
                AssetDatabase.WriteImportSettingsIfDirty(path);
                AssetDatabase.ImportAsset(path);
            }

            Debug.Log("AddNewGradient");
            texture = null;
            GradientTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public void UpdateGradient()
        {
            for (int i = 0; i < 256; i++)
            {
                Color color = gradient.Evaluate(i / 255f);
                GradientTexture.SetPixel(i + 1, 0, new Color(color.r, color.g, color.b, color.a));
            }

            GradientTexture.Apply();
            //get the path of the gradient texture;
            var path = AssetDatabase.GetAssetPath(GradientTexture);
            System.IO.File.WriteAllBytes(path, GradientTexture.EncodeToPNG());
            AssetDatabase.Refresh();
            // AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(GradientTexture));
        }
    }
}