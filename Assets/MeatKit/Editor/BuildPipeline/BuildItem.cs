using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    public abstract class BuildItem : ScriptableObject, IValidatable
    {
        public abstract IEnumerable<string> RequiredDependencies { get; }

        public virtual Dictionary<string, BuildMessage> Validate()
        {
            return new Dictionary<string, BuildMessage>();
        }

        public virtual AssetBundleBuild? ConfigureBuild()
        {
            return null;
        }

        public virtual void GenerateLoadAssets(TypeDefinition plugin, ILProcessor il)
        {
        }
    }
}
