using System;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    public class BuildWindow : EditorWindow
    {
        public static BuildProfile SelectedProfile;

        private static bool _init;

        [MenuItem("MeatKit/Build Window")]
        public static void Open()
        {
            GetWindow<BuildWindow>("MeatKit Build").Show();
        }

        private void OnGUI()
        {
            if (!_init)
            {
                SelectedProfile = MeatKitCache.LastSelectedProfile;
                _init = true;
            }
            
            EditorGUILayout.LabelField("Selected Build Profile", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            SelectedProfile = EditorGUILayout.ObjectField(SelectedProfile, typeof(BuildProfile), false) as BuildProfile;
            if (EditorGUI.EndChangeCheck()) MeatKitCache.LastSelectedProfile = SelectedProfile;

            if (!SelectedProfile)
            {
                GUILayout.Label("Please select a profile");
                return;
            }

            EditorGUILayout.Space();

            BuildSettingsEditor editor = Editor.CreateEditor(SelectedProfile, typeof(BuildSettingsEditor)) as BuildSettingsEditor;
            editor.OnInspectorGUI();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Build!", GUILayout.Height(50)))
                MeatKit.DoBuild();

            if (MeatKitCache.LastBuildTime != default(DateTime))
                GUILayout.Label("Last build: " + MeatKitCache.LastBuildTime + " (" + MeatKitCache.LastBuildDuration.GetReadableTimespan() + ")");
            else GUILayout.Label("Last build: Never");
        }
    }
}