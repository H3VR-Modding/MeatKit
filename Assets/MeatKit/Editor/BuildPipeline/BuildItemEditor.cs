using UnityEditor;

namespace MeatKit
{
    [CustomEditor(typeof(BuildItem), true)]
    public class BuildItemEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // Apply any changes and validate them
            serializedObject.ApplyModifiedProperties();
            var messages = ((IValidatable) target).Validate();

            // Draw the property fields and their message boxes, if any.
            var property = serializedObject.GetIterator();
            if (!property.NextVisible(true)) return;
            do
            {
                DrawProperty(property);

                BuildMessage message;
                if (messages.TryGetValue(property.name, out message))
                    EditorGUILayout.HelpBox(message.Message, message.Type);
            } while (property.NextVisible(false));
        }

        protected virtual void DrawProperty(SerializedProperty property)
        {
            EditorGUILayout.PropertyField(property, true);
        }
    }
}
