using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.Reflection;
using Atlas;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Sodalite.Api;

namespace MeatKit
{
    [CreateAssetMenu(menuName = "MeatKit/Build Items/Preload Assets", fileName = "New build item")]
    public class PreloadAssetsBuildItem : BuildItem
    {
        private const string BundleName = "preload";
        public Object[] Items;

        public override IEnumerable<string> RequiredDependencies
        {
            get { return new string[0]; }
        }

        public override AssetBundleBuild? ConfigureBuild()
        {
            return new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = Items.Select(AssetDatabase.GetAssetPath).ToArray()
            };
        }

        public override void GenerateLoadAssets(TypeDefinition plugin, ILProcessor il)
        {
#if H3VR_IMPORTED
            // Get some references
            const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
            FieldReference basePath = plugin.Fields.First(f => f.Name == "BasePath");
            MethodInfo pathCombine = typeof(Path).GetMethod("Combine", publicStatic);
            MethodInfo sodalitePreloadAllAssets = typeof(GameAPI).GetMethod("PreloadAllAssets", publicStatic);

            // Emit our opcodes
            il.Emit(OpCodes.Ldsfld, basePath);
            il.Emit(OpCodes.Ldstr, BundleName);
            il.Emit(OpCodes.Call, plugin.Module.ImportReference(pathCombine));
            il.Emit(OpCodes.Call, plugin.Module.ImportReference(sodalitePreloadAllAssets));
#endif
        }
    }
}