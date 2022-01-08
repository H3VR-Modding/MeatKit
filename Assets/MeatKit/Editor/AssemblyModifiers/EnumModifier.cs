using System;
using Mono.Cecil;
using UnityEngine;

namespace MeatKit
{
    [CreateAssetMenu(menuName = "MeatKit/Assembly Editors/Enum", fileName = "New Enum Editor")]
    public class EnumModifier : AssemblyModifier
    {
        private const FieldAttributes Attributes =
            FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.Public | FieldAttributes.HasDefault;

        [Tooltip("Specify the FULL NAME of the enum you want to change. e.g. Sub.Namespace.Type")]
        public string EnumName = "FistVR.FireArmRoundType";

        [Tooltip("The new values you want to add to this enum")]
        public EnumValue[] AddedValues = new EnumValue[0];

        public override void ApplyModification(AssemblyDefinition assembly)
        {
            // Try to get this type from the assembly. If it doesn't exist, we can just skip.
            TypeReference type = assembly.MainModule.GetType(EnumName);
            if (type == null) return;

            var definition = type.Resolve();
            if (!definition.IsEnum)
            {
                Debug.LogError(EnumName + " is not an enum type!", this);
                Applied = true;
                return;
            }

            // Add the new enum value to the type
            foreach (var value in AddedValues)
                definition.Fields.Add(new FieldDefinition(value.Name, Attributes, definition) {Constant = value.Value});

            Applied = true;
        }

        [Serializable]
        public struct EnumValue
        {
            [Tooltip("The name of the new enum value")]
            public string Name;

            [Tooltip("The new enum value")]
            public int Value;
        }
    }
}
