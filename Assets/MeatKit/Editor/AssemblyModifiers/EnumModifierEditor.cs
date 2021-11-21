using System;
using System.Linq;
using UnityEditor;

namespace MeatKit
{
    [CustomEditor(typeof(EnumModifier))]
    public class EnumModifierEditor : Editor
    {
        private static readonly string[] CommonTypes =
        {
            "FistVR.FireArmRoundType",
            "FistVR.FireArmRoundClass",
            "FistVR.FireArmMagazineType",
            "FistVR.ItemSpawnerObjectDefinition.ItemSpawnerCategory",
            "FistVR.SosigEnemyID"
        };

        private SerializedProperty _addedValues;
        private EnumModifier _enumModifier;
        private bool _isCustomType;
        private int _selectedType;

        // Called when an object of this type is selected
        private void OnEnable()
        {
            // Get our properties and check if this is a common type
            _addedValues = serializedObject.FindProperty("AddedValues");
            _enumModifier = (EnumModifier) target;
            _selectedType = Array.IndexOf(CommonTypes, _enumModifier.EnumName);
            _isCustomType = _selectedType == -1 || string.IsNullOrEmpty(_enumModifier.EnumName);
        }

        // Called to draw the inspector GUI
        public override void OnInspectorGUI()
        {
            // I'll be real I have no idea what this does but the Unity docs had it so I'm not gonna mess with it
            serializedObject.Update();

            // Use a toggle (checkbox) to determine if we're using a custom type or a commonly used one from the array
            _isCustomType = EditorGUILayout.Toggle("Custom type", _isCustomType);
            if (_isCustomType)
                _enumModifier.EnumName = EditorGUILayout.TextField("Enum name", _enumModifier.EnumName);
            else
            {
                if (_selectedType < 0 || _selectedType >= CommonTypes.Length) _selectedType = 0;
                _selectedType = EditorGUILayout.Popup("Type", _selectedType, CommonTypes);
                _enumModifier.EnumName = CommonTypes[_selectedType];
            }

            // Draw the values field and then save the object
            EditorGUILayout.PropertyField(_addedValues, true);
            serializedObject.ApplyModifiedProperties();
            
            // Suggest to the user that all added enums should be negative
            if (_enumModifier.AddedValues.Any(x => x.Value >= 0))
                EditorGUILayout.HelpBox(
                    "Your added enum values should be negative to avoid conflicts with vanilla items.",
                    MessageType.Warning);
        }
    }
}