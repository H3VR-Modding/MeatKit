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
    [CreateAssetMenu(menuName = "MeatKit/Build Items/OtherLoader Item", fileName = "New Item")]
    public class OtherLoaderBuildItem : BuildItem
    {
        [Tooltip("The name of this bundle pair")]
        public string BundleName;

        [Tooltip("Drag your item prefabs here")]
        public List<GameObject> Prefabs;

        [Tooltip("Drag your SpawnerIDs here")]
        public List<OtherLoader.ItemSpawnerEntry> SpawnerEntries;

        [Tooltip("Drag your FVRObjects here")]
        public List<FVRObject> FVRObjects;

        [Tooltip("When true, contents of item will be broken into two bundles: the data and the assets. This improves load times")]
        public bool OnDemand = true;

        public override IEnumerable<string> RequiredDependencies
        {
            get { return new[] { "devyndamonster-OtherLoader-1.1.5" }; }
        }

        public override Dictionary<string, BuildMessage> Validate()
        {
            var messages = base.Validate();

            return messages;
        }

        public override List<AssetBundleBuild> ConfigureBuild()
        {
            List<AssetBundleBuild> bundles = new List<AssetBundleBuild>();

            List<string> dataNames = new List<string>();
            dataNames.AddRange(SpawnerEntries.Select(o => AssetDatabase.GetAssetPath(o)));
            dataNames.AddRange(FVRObjects.Select(o => AssetDatabase.GetAssetPath(o)));

            List<string> prefabNames = new List<string>();
            prefabNames.AddRange(Prefabs.Select(o => AssetDatabase.GetAssetPath(o)));


            //If the build item is on demand, we split it into two bundles
            if (OnDemand)
            {
                bundles.Add(new AssetBundleBuild
                {
                    assetBundleName = BundleName.ToLower(),
                    assetNames = dataNames.ToArray()
                });

                
                bundles.Add(new AssetBundleBuild
                {
                    assetBundleName = "late_" + BundleName.ToLower(),
                    assetNames = prefabNames.ToArray()
                });
            }

            //If the build item is not on demand, it is all in once bundle
            else
            {
                bundles.Add(new AssetBundleBuild
                {
                    assetBundleName = BundleName.ToLower(),
                    assetNames = dataNames.Concat(prefabNames).ToArray()
                });
            }


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

