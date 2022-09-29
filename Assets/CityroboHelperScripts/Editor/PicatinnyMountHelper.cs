using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using FistVR;
using OpenScripts2;

public class PicatinnyMountHelper : EditorWindow 
{
    public AudioEvent Event;
    public FVRFireArm[] FVRFireArms;

    [MenuItem("Window/PicatinnyMountHelper")]
	public static void ShowWindow()
	{
		//Show existing window instance. If one doesn't exist, make one.
		EditorWindow.GetWindow(typeof(PicatinnyMountHelper));
	}


	public void OnGUI()
    {
        GUILayout.Label("PicatinnyMountHelper", EditorStyles.boldLabel);
        EditorGUIUtility.labelWidth = 300f;

        ScriptableObject target = this;
        SerializedObject so = new SerializedObject(target);
        SerializedProperty stringsProperty = so.FindProperty("FVRFireArms");

        SerializedProperty audioEvent = so.FindProperty("Event");
        BetterPropertyField.DrawSerializedProperty(audioEvent);

        EditorGUILayout.PropertyField(stringsProperty, true); // True means show children

        if (GUILayout.Button("Apply"))
        {
            ApplyChanges();
        }

        so.ApplyModifiedProperties(); // Remember to apply modified properties


    }

    void ApplyChanges()
    {
        foreach (var firearm in FVRFireArms)
        {
            foreach (var mount in firearm.AttachmentMounts)
            {
                AttachmentMountPicatinnyRail mountPicatinnyRail = mount.GetComponent<AttachmentMountPicatinnyRail>();

                if (mountPicatinnyRail != null) mountPicatinnyRail.SlotSound = Event;
            }
        }
    }


    public static class BetterPropertyField
    {


        /// <summary>
        /// Draws a serialized property (including children) fully, even if it's an instance of a custom serializable class.
        /// Supersedes EditorGUILayout.PropertyField(serializedProperty, true);
        /// </summary>
        /// <param name="_serializedProperty">Serialized property.</param>
        /// source: https://gist.github.com/tomkail/ba8d49e1cee021b0b89d47fca68b53a2
        public static void DrawSerializedProperty(SerializedProperty _serializedProperty)
        {
            if (_serializedProperty == null)
            {
                EditorGUILayout.HelpBox("SerializedProperty was null!", MessageType.Error);
                return;
            }
            var serializedProperty = _serializedProperty.Copy();
            int startingDepth = serializedProperty.depth;
            EditorGUI.indentLevel = serializedProperty.depth;
            DrawPropertyField(serializedProperty);
            while (serializedProperty.NextVisible(serializedProperty.isExpanded && !PropertyTypeHasDefaultCustomDrawer(serializedProperty.propertyType)) && serializedProperty.depth > startingDepth)
            {
                EditorGUI.indentLevel = serializedProperty.depth;
                DrawPropertyField(serializedProperty);
            }
            EditorGUI.indentLevel = startingDepth;
        }
        public static void DrawSerializedProperty(SerializedProperty _serializedProperty, GUIContent content)
        {
            if (_serializedProperty == null)
            {
                EditorGUILayout.HelpBox("SerializedProperty was null!", MessageType.Error);
                return;
            }
            var serializedProperty = _serializedProperty.Copy();
            int startingDepth = serializedProperty.depth;
            EditorGUI.indentLevel = serializedProperty.depth;
            DrawPropertyField(serializedProperty, content);
            while (serializedProperty.NextVisible(serializedProperty.isExpanded && !PropertyTypeHasDefaultCustomDrawer(serializedProperty.propertyType)) && serializedProperty.depth > startingDepth)
            {
                EditorGUI.indentLevel = serializedProperty.depth;
                DrawPropertyField(serializedProperty);
            }
            EditorGUI.indentLevel = startingDepth;
        }

        static void DrawPropertyField(SerializedProperty serializedProperty)
        {
            if (serializedProperty.propertyType == SerializedPropertyType.Generic)
            {
                serializedProperty.isExpanded = EditorGUILayout.Foldout(serializedProperty.isExpanded, serializedProperty.displayName, true);
            }
            else
            {
                EditorGUILayout.PropertyField(serializedProperty);
            }
        }
        static void DrawPropertyField(SerializedProperty serializedProperty, GUIContent content)
        {
            if (serializedProperty.propertyType == SerializedPropertyType.Generic)
            {
                serializedProperty.isExpanded = EditorGUILayout.Foldout(serializedProperty.isExpanded, serializedProperty.displayName, true);
            }
            else
            {
                EditorGUILayout.PropertyField(serializedProperty, content);
            }
        }

        static bool PropertyTypeHasDefaultCustomDrawer(SerializedPropertyType type)
        {
            return
            type == SerializedPropertyType.AnimationCurve ||
            type == SerializedPropertyType.Bounds ||
            type == SerializedPropertyType.Color ||
            type == SerializedPropertyType.Gradient ||
            type == SerializedPropertyType.LayerMask ||
            type == SerializedPropertyType.ObjectReference ||
            type == SerializedPropertyType.Rect ||
            type == SerializedPropertyType.Vector2 ||
            type == SerializedPropertyType.Vector3;
        }
    }
}