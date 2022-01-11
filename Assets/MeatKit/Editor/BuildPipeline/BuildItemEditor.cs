using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    [CustomEditor(typeof(BuildItem), true)]
    public class BuildItemEditor : Editor
    {
        protected Dictionary<string, BuildMessage> ValidationMessages;

        public override void OnInspectorGUI()
        {
            // Apply any changes and validate them
            ValidationMessages = ((IValidatable) target).Validate();

            // Draw the property fields and their message boxes, if any.
            var property = serializedObject.GetIterator();
            if (!property.NextVisible(true)) return;
            do DrawProperty(property);
            while (property.NextVisible(false));
            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void DrawProperty(SerializedProperty property)
        {
            // Don't draw the script name window.
            if (property.name == "m_Script") return;
            
            EditorGUILayout.PropertyField(property, true);
            DrawMessageIfExists(property.name);
        }

        protected void DrawMessageIfExists(string propertyName)
        {
            BuildMessage message;
            if (ValidationMessages.TryGetValue(propertyName, out message))
                EditorGUILayout.HelpBox(message.Message, message.Type);
        }
        
        protected void DrawListWithRequiredElements(string[] required, SerializedProperty additional)
        {
            EditorGUI.indentLevel++;

            // Draw the size field
            var size = EditorGUILayout.DelayedIntField("Size", required.Length + additional.arraySize);
            size = Mathf.Max(required.Length, size);

            // Resize the array if necessary
            var newSize = size - required.Length;

            if (newSize != additional.arraySize) additional.arraySize = newSize;

            // Draw the required dependencies. These are disabled.
            EditorGUI.BeginDisabledGroup(true);
            foreach (var dep in required)
                EditorGUILayout.TextField(dep);
            EditorGUI.EndDisabledGroup();

            // Draw the additional dependencies
            for (var i = 0; i < additional.arraySize; i++)
            {
                var value = additional.GetArrayElementAtIndex(i);
                value.stringValue = EditorGUILayout.TextField(value.stringValue);
            }

            EditorGUI.indentLevel--;
        }
    }
}