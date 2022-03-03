using System;
using System.Linq;
using Mono.Cecil;
using UnityEngine;

namespace MeatKit
{
    [CreateAssetMenu(menuName = "MeatKit/Assembly Editors/Path Replacer", fileName = "New Path Replacer")]
    public class CreatePathModifier : AssemblyModifier
    {
        [Tooltip("The new path replacements")]
        public ReplacementPath[] ReplacementPaths = new ReplacementPath[0];

        public override void ApplyModification(AssemblyDefinition assembly)
        {
            TypeReference stringRef = assembly.MainModule.TypeSystem.String;
            TypeReference intRef = assembly.MainModule.TypeSystem.Int32;
            if (stringRef == null || intRef == null) return;

            foreach (ReplacementPath replacementPath in ReplacementPaths)
            {
                TypeReference type = assembly.MainModule.GetType(replacementPath.ScriptableObjectName);
                if (type == null) continue;

                TypeDefinition definition = type.Resolve();
                CustomAttribute createMenuAttribute = GetCreateAssetMenuAttribute(definition);

                if (createMenuAttribute == null)
                {
                    Debug.LogWarning("Could not get CreateAssetMenuAttribute for scriptable object: " + replacementPath.ScriptableObjectName);
                    continue;
                }

                CustomAttributeArgument pathArgumentValue = new CustomAttributeArgument(stringRef, replacementPath.NewPath);
                CustomAttributeArgument fileNameArgumentValue = new CustomAttributeArgument(stringRef, replacementPath.NewDefaultName);
                CustomAttributeArgument orderArgumentValue = new CustomAttributeArgument(intRef, replacementPath.NewOrder);

                CustomAttributeNamedArgument pathArgument = new CustomAttributeNamedArgument("menuName", pathArgumentValue);
                CustomAttributeNamedArgument fileNameArgument = new CustomAttributeNamedArgument("fileName", fileNameArgumentValue);
                CustomAttributeNamedArgument orderArgument = new CustomAttributeNamedArgument("order", orderArgumentValue);

                createMenuAttribute.Properties.Clear();
                createMenuAttribute.Properties.Add(pathArgument);
                createMenuAttribute.Properties.Add(fileNameArgument);
                createMenuAttribute.Properties.Add(orderArgument);
            }

            Applied = true;
        }

        private CustomAttribute GetCreateAssetMenuAttribute(TypeDefinition definition)
        {
            CustomAttribute[] foundAttributes = definition.CustomAttributes.Where(ca => ca.AttributeType.FullName.Contains("CreateAssetMenuAttribute")).ToArray();

            if(foundAttributes.Length > 0)
            {
                return foundAttributes[0];
            }

            return null;
        }


        [Serializable]
        public struct ReplacementPath
        {
            [Tooltip("Specify the FULL NAME of the scriptable object you wish to modify. e.g. Sub.Namespace.Type")]
            public string ScriptableObjectName;

            [Tooltip("The new create asset path of the scriptable object")]
            public string NewPath;

            [Tooltip("The new default name of the scriptable object when created")]
            public string NewDefaultName;

            [Tooltip("The new ordering of the scriptable object in menu")]
            public int NewOrder;
        }
    }
}
