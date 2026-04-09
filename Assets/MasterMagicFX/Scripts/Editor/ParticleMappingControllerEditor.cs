using UnityEditor;
using UnityEngine;

namespace MasterFX
{
    [CustomEditor(typeof(ParticleMappingController))]
    public class ParticleMappingControllerEditor : Editor
    {
        private ParticleMappingController controller;
        private Texture2D gradientTexture;

        private void OnEnable()
        {
            controller = (ParticleMappingController)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            controller.MappingGradient = EditorGUILayout.GradientField("Mapping Gradient", controller.MappingGradient);
            if (EditorGUI.EndChangeCheck())
            {
                if (controller.MappingTexture)
                {
                    DestroyImmediate(controller.MappingTexture);
                    gradientTexture = null;
                    controller.MappingTexture = null;
                }
                controller.MappingTexture = GenerateGradientTexture();

                //set the particles ramp texture to the new gradient texture

                controller.SetRampTexture();
            }


            if (GUILayout.Button("Generate Mapping Texture"))
            {
                controller.MappingTexture = GenerateGradientTexture();
                // SaveTextureAsAsset();
                controller.SetRampTexture();
            }
            //draw the property of mapping texture and test texture;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("MappingTexture"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("TestTexture"));


            serializedObject.ApplyModifiedProperties();
        }

        private Texture2D GenerateGradientTexture()
        {
            int textureWidth = 256;
            gradientTexture = new Texture2D(textureWidth, 1);

            for (int i = 0; i < textureWidth; i++)
            {
                float t = i / (float)(textureWidth - 1);
                Color color = controller.MappingGradient.Evaluate(t);
                gradientTexture.SetPixel(i, 0, color);
            }
            //change gradient texture to readable;
            gradientTexture.wrapMode = TextureWrapMode.Clamp;
            gradientTexture.filterMode = FilterMode.Bilinear;
            gradientTexture.Apply();
            SaveTextureAsAsset();
            return gradientTexture;
        }

        private void SaveTextureAsAsset()
        {
            if (gradientTexture == null) return;

            string assetPath = "Assets/GradientTexture.png";
            AssetDatabase.DeleteAsset(assetPath);
            // AssetDatabase.CreateAsset(gradientTexture, assetPath);
            //import this;

            Debug.Log(assetPath);
            System.IO.File.WriteAllBytes(assetPath, gradientTexture.EncodeToPNG());
            AssetDatabase.Refresh();

            AssetDatabase.SaveAssets();
        }
    }
}