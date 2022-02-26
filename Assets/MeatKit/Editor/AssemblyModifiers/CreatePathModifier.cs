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
            foreach(ReplacementPath replacementPath in ReplacementPaths)
            {
                TypeReference type = assembly.MainModule.GetType(replacementPath.ScriptableObjectName);
                TypeReference refType = assembly.MainModule.GetType("FistVR.MainMenuSceneDef");

                if (type == null || refType == null) continue;

                Debug.Log("Patching asset menu path for type: " + replacementPath.ScriptableObjectName);

                TypeDefinition definition = type.Resolve();
                TypeDefinition refDefinition = refType.Resolve();

                CustomAttribute oldAttribute = GetCreateAssetMenuAttribute(definition);
                CustomAttribute referenceAttribute = GetCreateAssetMenuAttribute(refDefinition);

                if (oldAttribute == null)
                {
                    Debug.LogWarning("Could not get CreateAssetMenuAttribute for scriptable object: " + replacementPath.ScriptableObjectName);
                    continue;
                }

                definition.CustomAttributes.Remove(oldAttribute);

                CustomAttribute newAttribute = new CustomAttribute(referenceAttribute.Constructor)
                {
                    Properties =
                    {
                        new CustomAttributeNamedArgument(
                            referenceAttribute.Properties[0].Name,
                            new CustomAttributeArgument(referenceAttribute.Properties[0].Argument.Type, replacementPath.NewDefaultName)
                        ),
                        new CustomAttributeNamedArgument(
                            referenceAttribute.Properties[1].Name,
                            new CustomAttributeArgument(referenceAttribute.Properties[1].Argument.Type, replacementPath.NewPath)
                        ),
                        new CustomAttributeNamedArgument(
                            referenceAttribute.Properties[2].Name,
                            new CustomAttributeArgument(referenceAttribute.Properties[2].Argument.Type, replacementPath.NewOrder)
                        )
                    }
                };

                definition.CustomAttributes.Add(newAttribute);
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
