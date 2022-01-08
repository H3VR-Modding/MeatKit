using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    [CustomEditor(typeof(BuildProfile))]
    public class BuildProfileEditor : BuildItemEditor
    {
        private bool _folded;
        private BuildProfile _profile;

        private void OnEnable()
        {
            _profile = (BuildProfile) target;
        }

        protected override void DrawProperty(SerializedProperty property)
        {
            if (property.name == "AdditionalDependencies")
            {
                _folded = EditorGUILayout.Foldout(_folded, "Dependencies");
                if (_folded)
                {
                    var requiredDeps = _profile.GetRequiredDependencies();

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
                base.DrawProperty(property);

                // Draw the build action stuff
                if (_profile.BuildAction == BuildAction.CopyToProfile)
                {
                    // Tell the user which profile it will be output to
                    string profileName = Path.GetFileName(_profile.OutputProfile);
                    GUILayout.Label("Selected profile: " + (string.IsNullOrEmpty(profileName) ? "None" : profileName));

                    // Give a button to change the output folder
                    if (GUILayout.Button("Select profile folder"))
                        _profile.OutputProfile = EditorUtility.OpenFolderPanel("Select your r2mm profile folder", @"%APPDATA%\Roaming\r2modmanPlus-local\H3VR\profiles", "");

                    // Draw any errors that come from the BuildAction property, as those won't get displayed otherwise.
                    DrawMessageIfExists("OutputProfile");
                }
            }
            else base.DrawProperty(property);
        }
    }
}