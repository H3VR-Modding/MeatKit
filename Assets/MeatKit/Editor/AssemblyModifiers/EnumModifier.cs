using System;
using System.Linq;
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
            // Try to get the enum type. If it contains a '+' in the name, it's nested and we have to do a bit of extra stuff
            TypeDefinition type;
            if (EnumName.Contains("+"))
            {
                
                string[] parts = EnumName.Split('+');
                type = assembly.MainModule.GetType(parts[0]);
                if (type != null)
                {
                    for (int i = 1; i < parts.Length; i++)
                    {
                        Debug.Log(type);
                        type = type.NestedTypes.FirstOrDefault(x => x.Name == parts[i]);
                        if (type == null) break;
                    }
                }
            }
            
            // Otherwise we can just grab it directly if it isn't nested
            else type = assembly.MainModule.GetType(EnumName);
            
            // If we can't find the type, skip.
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
