using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Atlas;
using BepInEx;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEditor;
using UnityEngine;
#if H3VR_IMPORTED
using Valve.Newtonsoft.Json;
using Valve.Newtonsoft.Json.Linq;
#endif

namespace MeatKit
{
    [CreateAssetMenu(menuName = "MeatKit/Build Items/Atlas Scene", fileName = "New scene")]
    public class AtlasSceneBuildItem : BuildItem
    {
        private static readonly string[] WellKnownModes = {"sandbox"};

        [Tooltip("Drag and drop your scene asset file here.")]
        public SceneAsset SceneFile;

        [Tooltip("Give a name to your scene")]
        public string SceneName;

        [Tooltip("Where the user will be able to load your scene")]
        public string Mode;

        [Tooltip("Your scene thumbnail / preview")]
        public Texture2D Thumbnail;

        [Tooltip("Your name")]
        public string Author;

        [Tooltip("The scene's description text")]
        [TextArea]
        public string Description;

        public override IEnumerable<string> RequiredDependencies
        {
            get { return new[] {"nrgill28-Atlas-0.1.0"}; }
        }

        public override Dictionary<string, BuildMessage> Validate()
        {
            var messages = base.Validate();

            if (string.IsNullOrEmpty(SceneName))
                messages["SceneName"] = BuildMessage.Error("Scene name cannot be empty");
            if (!Thumbnail) messages["Thumbnail"] = BuildMessage.Warning("Scene is missing a thumbnail.");
            if (!WellKnownModes.Contains(Mode))
                messages["Mode"] = BuildMessage.Warning("'" + Mode + "' is not in the list of well-known modes.");

            return messages;
        }

        public override List<AssetBundleBuild> ConfigureBuild()
        {
#if H3VR_IMPORTED
            // We need to export the thumbnail and scene metadata
            var sceneFileName = MeatKit.BundleOutputPath + SceneFile.name.ToLower();
            File.Copy(AssetDatabase.GetAssetPath(Thumbnail), sceneFileName + ".png");
            var obj = new JObject();
            obj["DisplayName"] = SceneName;
            obj["Identifier"] = SceneFile.name;
            obj["Mode"] = Mode;
            obj["Author"] = Author;
            obj["Description"] = Description;
            File.WriteAllText(sceneFileName + ".json", JsonConvert.SerializeObject(obj));
#endif
            // Return the configuration to build the scene bundle file
            List<AssetBundleBuild> bundles = new List<AssetBundleBuild>();

            bundles.Add(new AssetBundleBuild
            {
                assetBundleName = SceneFile.name,
                assetNames = new[] { AssetDatabase.GetAssetPath(SceneFile) }
            });

            return bundles;
        }

        public override void GenerateLoadAssets(TypeDefinition plugin, ILProcessor il)
        {
#if H3VR_IMPORTED
            EnsurePluginDependsOn(plugin, AtlasConstants.Guid, AtlasConstants.Version);
            
            /*
             * We need to add this line: AtlasPlugin.RegisterScene(Path.Combine(BasePath, "scene name"))
             * Which translates to this IL:
             *  ldsfld  string MeatKitPlugin::BasePath
             *  ldstr   "scene name"
             *  call    string [mscorlib]System.IO.Path::Combine(string, string)
             *  call    string [Atlas]Atlas.AtlasPlugin::RegisterScene(string)
             */

            // Get some references
            var publicStatic = BindingFlags.Public | BindingFlags.Static;
            FieldReference basePath = plugin.Fields.First(f => f.Name == "BasePath");
            var pathCombine = typeof(Path).GetMethod("Combine", publicStatic);
            var atlasRegisterScene = typeof(AtlasPlugin).GetMethod("RegisterScene", publicStatic);

            // Emit our opcodes
            il.Emit(OpCodes.Ldsfld, basePath);
            il.Emit(OpCodes.Ldstr, SceneFile.name.ToLower());
            il.Emit(OpCodes.Call, plugin.Module.ImportReference(pathCombine));
            il.Emit(OpCodes.Call, plugin.Module.ImportReference(atlasRegisterScene));
#endif
        }
    }
}
