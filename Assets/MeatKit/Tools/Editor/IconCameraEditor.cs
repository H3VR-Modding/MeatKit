using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(IconCamera))]
[CanEditMultipleObjects]
public class IconCameraEditor : Editor {


    Texture2D previewTexture = null;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var property = serializedObject.GetIterator();
        if (!property.NextVisible(true)) return;
        do EditorGUILayout.PropertyField(property, true);
        while (property.NextVisible(false));

        serializedObject.ApplyModifiedProperties();

        IconCamera iconCamera = serializedObject.targetObject as IconCamera;
        iconCamera.thisCamera.depthTextureMode = iconCamera.CameraDepthMode;

        if (previewTexture != null)
        {
            GUILayout.BeginVertical("Box");

            GUIStyle style = new GUIStyle();
            style.alignment = TextAnchor.UpperCenter;
            style.fixedWidth = Screen.width - 50;
            style.fixedHeight = Screen.width - 50;
            GUILayout.Label(previewTexture, style);

            GUILayout.EndVertical();
        }
        


        if (GUILayout.Button("Update Preview"))
        {
            if (iconCamera.renderTexture != null)
            {
                RenderTexture temp = RenderTexture.active;
                RenderTexture.active = iconCamera.renderTexture;
                Texture2D texture = new Texture2D(iconCamera.renderTexture.width, iconCamera.renderTexture.height);
                texture.ReadPixels(new Rect(0, 0, iconCamera.renderTexture.width, iconCamera.renderTexture.height), 0, 0);
                texture.Apply();
                RenderTexture.active = temp;

                texture = iconCamera.FlipTexture(texture);

                if (iconCamera.background != null)
                {
                    texture = iconCamera.AddBackground(texture, iconCamera.background);
                }

                previewTexture = texture;
            }
        }


        if (GUILayout.Button("Take Picture"))
        {
            Selection.activeGameObject.GetComponent<IconCamera>().Capture();
        }
    }
}
