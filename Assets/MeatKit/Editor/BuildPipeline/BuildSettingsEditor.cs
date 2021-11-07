using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    [CustomEditor(typeof(BuildSettings))]
    public class BuildSettingsEditor : BuildItemEditor
    {
        private bool _folded;

        protected override void DrawProperty(SerializedProperty property)
        {
            if (property.name != "AdditionalDependencies") base.DrawProperty(property);
            else
            {
                _folded = EditorGUILayout.Foldout(_folded, "Dependencies");
                if (_folded)
                {
                    var settings = (BuildSettings) target;
                    var requiredDeps = settings.GetRequiredDependencies();

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
        }
    }
}
