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
    public class OtherLoaderBuildItem : BuildItem
    {
        [Tooltip("The name of this bundle pair")]
        public string BundleName;

        [Tooltip("Drag your item prefabs here")]
        public List<GameObject> Prefabs;

        [Tooltip("Drag your SpawnerIDs here")]
        public List<ItemSpawnerID> SpawnerIDs;

        [Tooltip("Drag your FVRObjects here")]
        public List<FVRObject> FVRObjects;


        public override IEnumerable<string> RequiredDependencies
        {
            get { return new[] { "devyndamonster-OtherLoader-1.1.5" }; }
        }

        public override Dictionary<string, BuildMessage> Validate()
        {
            var messages = base.Validate();

            return messages;
        }

        public override List<AssetBundleBuild?> ConfigureBuild()
        {
            List<AssetBundleBuild?> bundles = new List<AssetBundleBuild?>();

            // The first asset bundle contains just item data
            List<string> dataNames = new List<string>();
            dataNames.AddRange(SpawnerIDs.Select(o => AssetDatabase.GetAssetPath(o)));
            dataNames.AddRange(FVRObjects.Select(o => AssetDatabase.GetAssetPath(o)));

            bundles.Add(new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = dataNames.ToArray()
            });



            //The second asset bundle contains the prefabs themselves, and everything they reference
            bundles.Add(new AssetBundleBuild
            {
                assetBundleName = "late_" + BundleName,
                assetNames = Prefabs.Select(o => AssetDatabase.GetAssetPath(PrefabUtility.GetPrefabParent(o))).ToArray()
            });

            return bundles;
        }

        public override void GenerateLoadAssets(TypeDefinition plugin, ILProcessor il)
        {
#if H3VR_IMPORTED
            EnsurePluginDependsOn(plugin, "h3vr.otherloader", "1.1.3");
#endif
        }
    }
}

