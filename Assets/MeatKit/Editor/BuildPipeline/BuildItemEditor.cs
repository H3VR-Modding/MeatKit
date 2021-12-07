using System.Collections.Generic;
using UnityEditor;

namespace MeatKit
{
    [CustomEditor(typeof(BuildItem), true)]
    public class BuildItemEditor : Editor
    {
        protected Dictionary<string, BuildMessage> ValidationMessages;

        public override void OnInspectorGUI()
        {
            // Apply any changes and validate them
            serializedObject.ApplyModifiedProperties();
            ValidationMessages = ((IValidatable) target).Validate();

            // Draw the property fields and their message boxes, if any.
            var property = serializedObject.GetIterator();
            if (!property.NextVisible(true)) return;
            do DrawProperty(property);
            while (property.NextVisible(false));
        }

        protected virtual void DrawProperty(SerializedProperty property)
        {
            EditorGUILayout.PropertyField(property, true);
            DrawMessageIfExists(property.name);
        }

        protected void DrawMessageIfExists(string propertyName)
        {
            BuildMessage message;
            if (ValidationMessages.TryGetValue(propertyName, out message))
                EditorGUILayout.HelpBox(message.Message, message.Type);
        }
    }
}