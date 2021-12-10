using System;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    public class BuildWindow : EditorWindow
    {
        public static BuildProfile SelectedProfile;
        
        [MenuItem("MeatKit/Build Window")]
        public static void Open()
        {
            GetWindow<BuildWindow>("MeatKit Build").Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Selected Build Profile", EditorStyles.boldLabel);
            SelectedProfile = EditorGUILayout.ObjectField(SelectedProfile, typeof(BuildProfile), false) as BuildProfile;

            if (!SelectedProfile)
            {
                GUILayout.Label("Please select a profile");
                return;
            }

            EditorGUILayout.Space();
            
            Editor editor = Editor.CreateEditor(SelectedProfile);
            editor.DrawDefaultInspector();
            
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Build!", GUILayout.Height(50)))
                MeatKit.DoBuild();
            
            if (MeatKitCache.LastBuildTime != default(DateTime))
                GUILayout.Label("Last build: " + MeatKitCache.LastBuildTime + " (" + MeatKitCache.LastBuildDuration.GetReadableTimespan() + ")");
            else GUILayout.Label("Last build: Never");
        }
    }
}