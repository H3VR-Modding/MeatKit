using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MeatKit
{
    [CustomEditor(typeof(AdvancedIconCamera))]
    [CanEditMultipleObjects]
    public class AdvancedIconCameraEditor : Editor
    {
        private Texture2D _previewTexture = null;
        private AdvancedIconCamera _iconCamera;

        int pictureIterator = 0;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var property = serializedObject.GetIterator();
            if (!property.NextVisible(true)) return;
            do EditorGUILayout.PropertyField(property, true);
            while (property.NextVisible(false));

            serializedObject.ApplyModifiedProperties();

            _iconCamera = serializedObject.targetObject as AdvancedIconCamera;
            _iconCamera.thisCamera.depthTextureMode = _iconCamera.CameraDepthMode;

            if (_previewTexture != null)
            {
                GUILayout.BeginVertical("Box");

                GUIStyle style = new GUIStyle();
                style.alignment = TextAnchor.UpperCenter;
                style.fixedWidth = Screen.width - 50;
                style.fixedHeight = Screen.width - 50;
                GUILayout.Label(_previewTexture, style);

                GUILayout.EndVertical();
            }
            /*
            string path = "Assets" + "/" + iconCamera.path + "/" + iconCamera.iconName + ".png";
            Texture savedTexture = (Texture)AssetDatabase.LoadAssetAtPath(path, typeof(Texture));
            if (savedTexture != null)
            {
                GUILayout.BeginVertical("Box");

                GUIStyle style = new GUIStyle();
                style.alignment = TextAnchor.UpperCenter;
                style.fixedWidth = Screen.width - 50;
                style.fixedHeight = Screen.width - 50;
                GUILayout.Label(savedTexture, style);

                GUILayout.EndVertical();
            }
            */
            if (GUILayout.Button("Move to Object"))
            {
                _iconCamera.GoToObject();
            }

            if (GUILayout.Button("Update Preview"))
            {
                UpdatePreview();
            }


            if (GUILayout.Button("Create single Icon"))
            {
                _iconCamera.TakeSinglePicture();
            }

            if (GUILayout.Button("Create multiple Icons"))
            {
                pictureIterator = 0;
                //EditorApplication.update += TakeMultiplePictures;

                _iconCamera.TakeMultiplePictures();
            }
        }

        void UpdatePreview()
        {
            if (_iconCamera.renderTexture != null)
            {
                RenderTexture temp = RenderTexture.active;
                RenderTexture.active = _iconCamera.renderTexture;
                Texture2D texture = new Texture2D(_iconCamera.renderTexture.width, _iconCamera.renderTexture.height);
                texture.ReadPixels(new Rect(0, 0, _iconCamera.renderTexture.width, _iconCamera.renderTexture.height), 0, 0);
                texture.Apply();
                texture = _iconCamera.FlipTexture(texture);

                if (_iconCamera.background != null)
                {
                    texture = _iconCamera.AddBackground(texture, _iconCamera.background);
                }

                // Gamma adjustment
                RenderTexture gammaCorrection = RenderTexture.GetTemporary(_iconCamera.renderTexture.width, _iconCamera.renderTexture.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Material gammaCorrectionMaterial = new Material(Shader.Find("Hidden/GammaCorrectionShader"));
                Graphics.Blit(texture, gammaCorrection, gammaCorrectionMaterial);
                RenderTexture.active = gammaCorrection;
                texture.ReadPixels(new Rect(0, 0, _iconCamera.renderTexture.width, _iconCamera.renderTexture.height), 0, 0);
                texture.Apply();

                RenderTexture.ReleaseTemporary(gammaCorrection);
                RenderTexture.active = temp;
                _previewTexture = texture;
            }

            _iconCamera.UnhideOtherSceneObjects();
        }
    }
}
