using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Atlas;
using BepInEx;
using FistVR;
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
    [CreateAssetMenu(menuName = "MeatKit/Build Items/OtherLoader Mod", fileName = "New Mod")]
    public class OtherLoaderBuildRoot : BuildItem
    {
        [Tooltip("All build items for this otherloader mod")]
        public List<OtherLoaderBuildItem> BuildItems;


        public override IEnumerable<string> RequiredDependencies
        {
            get { return new[] { "devyndamonster-OtherLoader-1.1.5" }; }
        }

        public override Dictionary<string, BuildMessage> Validate()
        {
            var messages = base.Validate();

            //Go through all the child build items and validate them
            foreach(OtherLoaderBuildItem buildItem in BuildItems)
            {
                var itemMessages = buildItem.Validate();
                itemMessages.ToList().ForEach(o => { messages[o.Key] = o.Value; });
            }

            return messages;
        }

        public override List<AssetBundleBuild> ConfigureBuild()
        {
            List<AssetBundleBuild> bundles = new List<AssetBundleBuild>();
            
            BuildItems.ForEach(o => { bundles.AddRange(o.ConfigureBuild()); });

            return bundles;
        }

        public override void GenerateLoadAssets(TypeDefinition plugin, ILProcessor il)
        {
#if H3VR_IMPORTED
            EnsurePluginDependsOn(plugin, "h3vr.otherloader", "1.1.5");
#endif
        }

        public override void PostProcessBuild()
        {
            
        }
    }
}

