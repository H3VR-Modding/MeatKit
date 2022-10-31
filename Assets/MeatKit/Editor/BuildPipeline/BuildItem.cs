using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
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

        public virtual List<AssetBundleBuild> ConfigureBuild()
        {
            return null;
        }

        public virtual void GenerateLoadAssets(TypeDefinition plugin, ILProcessor il)
        {
        }

        protected void EnsurePluginDependsOn(TypeDefinition plugin, string pluginGuid, string pluginVersion)
        {
            // Check if the plugin already has this dependency
            var alreadyDependsOn = plugin.CustomAttributes
                .Where(a => a.AttributeType.Name == "BepInDependency")
                .Any(attribute => (string) attribute.ConstructorArguments[0].Value == pluginGuid);

            // If it doesn't we need to add it.
            if (!alreadyDependsOn)
            {
                MethodBase constructor =
                    typeof(BepInDependency).GetConstructor(new[] {typeof(string), typeof(string)});
                var attribute = new CustomAttribute(plugin.Module.ImportReference(constructor));
                plugin.CustomAttributes.Add(attribute);

                var str = plugin.Module.TypeSystem.String;
                attribute.ConstructorArguments.Add(new CustomAttributeArgument(str, pluginGuid));
                attribute.ConstructorArguments.Add(new CustomAttributeArgument(str, pluginVersion));
            }
        }

        protected void EnsurePluginIsIncompatibleWith(TypeDefinition plugin, string pluginGuid)
        {
            // Check if the plugin already has this dependency
            var alreadyIncompatibleWith = plugin.CustomAttributes
                .Where(a => a.AttributeType.Name == "BepInIncompatibility")
                .Any(attribute => (string)attribute.ConstructorArguments[0].Value == pluginGuid);

            // If it doesn't we need to add it.
            if (!alreadyIncompatibleWith)
            {
                MethodBase constructor = typeof(BepInIncompatibility).GetConstructor(new[] {typeof(string)});
                var attribute = new CustomAttribute(plugin.Module.ImportReference(constructor));
                plugin.CustomAttributes.Add(attribute);

                var str = plugin.Module.TypeSystem.String;
                attribute.ConstructorArguments.Add(new CustomAttributeArgument(str, pluginGuid));
            }
        }
    }
}
