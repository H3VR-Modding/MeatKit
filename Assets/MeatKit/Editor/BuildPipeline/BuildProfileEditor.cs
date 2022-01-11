using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    [CustomEditor(typeof(BuildProfile))]
    public class BuildProfileEditor : BuildItemEditor
    {
        private bool _folded1, _folded2;
        private BuildProfile _profile;

        private void OnEnable()
        {
            _profile = (BuildProfile) target;
        }

        protected override void DrawProperty(SerializedProperty property)
        {
            if (property.name == "AdditionalDependencies")
            {
                _folded1 = EditorGUILayout.Foldout(_folded1, "Dependencies");
                if (_folded1) DrawListWithRequiredElements(_profile.GetRequiredDependencies(), property);
            }
            else if (property.name == "AdditionalNamespaces")
            {
                if (!_profile.StripNamespaces) return;
                
                _folded2 = EditorGUILayout.Foldout(_folded2, "Allowed Namespaces");
                if (_folded2)
                {
                    DrawListWithRequiredElements(_profile.GetRequiredNamespaces(), property);
                    EditorGUI.indentLevel++;
                    EditorGUILayout.HelpBox("Namespaces relate to custom scripts in your project. Any code in a namespace not included here will not be included in your final build. Note this only applies to .cs files directly in your project. Scripts coming from .dll files are not affected by this setting.", MessageType.Info);
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