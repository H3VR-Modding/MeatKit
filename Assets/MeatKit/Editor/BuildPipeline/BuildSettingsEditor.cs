using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    [CustomEditor(typeof(BuildSettings))]
    public class BuildSettingsEditor : BuildItemEditor
    {
        private bool _folded;
        private BuildSettings _settings;

        private void OnEnable()
        {
            _settings = (BuildSettings) target;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            
            // Draw the build action stuff
            if (_settings.BuildAction == BuildAction.CopyToProfile)
            {
                // Tell the user which profile it will be output to
                string profileName = Path.GetFileName(_settings.OutputProfile);
                GUILayout.Label("Selected profile: " + (string.IsNullOrEmpty(profileName) ? "None" : profileName));
                    
                // Give a button to change the output folder
                if (GUILayout.Button("Select profile folder"))
                    _settings.OutputProfile = EditorUtility.OpenFolderPanel("Select your r2mm profile folder", @"%APPDATA%\Roaming\r2modmanPlus-local\H3VR\profiles", "");
                    
                // Draw any errors that come from the BuildAction property, as those won't get displayed otherwise.
                DrawMessageIfExists(serializedObject.FindProperty("OutputProfile"));
            }

            if (GUILayout.Button("Build!", GUILayout.Height(50)))
                MeatKit.DoBuild();
            
            if (MeatKitCache.LastBuildTime != default(DateTime))
                GUILayout.Label("Last build: " + MeatKitCache.LastBuildTime + " (" + MeatKitCache.LastBuildDuration.GetReadableTimespan() + ")");
            else GUILayout.Label("Last build: Never");
        }

        protected override void DrawProperty(SerializedProperty property)
        {
            if (property.name == "AdditionalDependencies")
            {
                _folded = EditorGUILayout.Foldout(_folded, "Dependencies");
                if (_folded)
                {
                    var requiredDeps = _settings.GetRequiredDependencies();

                    EditorGUI.indentLevel++;

                    // Draw the size field
                    var size = EditorGUILayout.DelayedIntField("Size", requiredDeps.Length + property.arraySize);
                    size = Mathf.Max(requiredDeps.Length, size);

                    // Resize the array if necessary
                    var newSize = size - requiredDeps.Length;
                    if (newSize != property.arraySize) property.arraySize = newSize;

                    // Draw the required dependencies. These are disabled.
                    EditorGUI.BeginDisabledGroup(true);
                    foreach (var dep in requiredDeps)
                        EditorGUILayout.TextField(dep);
                    EditorGUI.EndDisabledGroup();

                    // Draw the additional dependencies
                    for (var i = 0; i < property.arraySize; i++)
                    {
                        var value = property.GetArrayElementAtIndex(i);
                        value.stringValue = EditorGUILayout.TextField(value.stringValue);
                    }

                    EditorGUI.indentLevel--;
                }
            }
            else if (property.name == "BuildAction")
            {
                // Flexible space so what comes after is at the bottom
                GUILayout.FlexibleSpace();
                base.DrawProperty(property);
            }
            else base.DrawProperty(property);
        }
    }
}