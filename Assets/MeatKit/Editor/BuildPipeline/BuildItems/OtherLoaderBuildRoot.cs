using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Atlas;
using BepInEx;

using Mono.Cecil;
using Mono.Cecil.Cil;
using OtherLoader;
using UnityEditor;
using UnityEngine;
#if H3VR_IMPORTED
using FistVR;
using Valve.Newtonsoft.Json;
using Valve.Newtonsoft.Json.Linq;
#endif

namespace MeatKit
{
    [CreateAssetMenu(menuName = "MeatKit/Build Items/OtherLoader Mod", fileName = "New Mod")]
    public class OtherLoaderBuildRoot : BuildItem
    {
        [Tooltip("Build items that should load first, in the order they appear")]
        public List<OtherLoaderBuildItem> BuildItemsFirst = new List<OtherLoaderBuildItem>();

        [Tooltip("Build items that should in parralel, after the first items load")]
        public List<OtherLoaderBuildItem> BuildItemsAny = new List<OtherLoaderBuildItem>();

        [Tooltip("Build items that should load last, in the order they appear")]
        public List<OtherLoaderBuildItem> BuildItemsLast = new List<OtherLoaderBuildItem>();

        [Tooltip("Guids of otherloader mods that must be loaded before these assets will load. Only applies to SelfLoading mods")]
        public List<string> LoadDependancies = new List<string>();

        [Tooltip("When true, additional code will be generated that allows the mod to automatically load itself into otherloader")]
        public bool SelfLoading = true;

        public override IEnumerable<string> RequiredDependencies
        {
            get { return new[] { "devyndamonster-OtherLoader-1.1.5" }; }
        }

        public override Dictionary<string, BuildMessage> Validate()
        {
            var messages = base.Validate();

            List<OtherLoaderBuildItem> allBuildItems = BuildItemsFirst.Concat(BuildItemsAny).Concat(BuildItemsLast).ToList();

            foreach (OtherLoaderBuildItem buildItem in BuildItemsFirst)
            {
                if (buildItem == null)
                {
                    messages["BuildItemsFirst"] = BuildMessage.Error("Child build item cannot be null!");
                    continue;
                }

                if (allBuildItems.Count(o => o != null && buildItem != null && o.BundleName == buildItem.BundleName) > 1)
                {
                    messages["BuildItemsFirst"] = BuildMessage.Error("Child build items must have unique bundle names!");
                }

                var itemMessages = buildItem.Validate();
                itemMessages.ToList().ForEach(o => { messages["BuildItemsFirst"] = o.Value; });
            }

            foreach (OtherLoaderBuildItem buildItem in BuildItemsAny)
            {
                if (buildItem == null)
                {
                    messages["BuildItemsAny"] = BuildMessage.Error("Child build item cannot be null!");
                    continue;
                }

                if (allBuildItems.Count(o => o != null && buildItem != null && o.BundleName == buildItem.BundleName) > 1)
                {
                    messages["BuildItemsAny"] = BuildMessage.Error("Child build items must have unique bundle names!");
                }

                var itemMessages = buildItem.Validate();
                itemMessages.ToList().ForEach(o => { messages["BuildItemsAny"] = o.Value; });
            }

            foreach (OtherLoaderBuildItem buildItem in BuildItemsLast)
            {
                if (buildItem == null)
                {
                    messages["BuildItemsLast"] = BuildMessage.Error("Child build item cannot be null!");
                    continue;
                }

                if (allBuildItems.Count(o => o != null && buildItem != null && o.BundleName == buildItem.BundleName) > 1)
                {
                    messages["BuildItemsLast"] = BuildMessage.Error("Child build items must have unique bundle names!");
                }

                var itemMessages = buildItem.Validate();
                itemMessages.ToList().ForEach(o => { messages["BuildItemsLast"] = o.Value; });
            }


            return messages;
        }

        public override List<AssetBundleBuild> ConfigureBuild()
        {
            List<AssetBundleBuild> bundles = new List<AssetBundleBuild>();
            
            BuildItemsFirst.ForEach(o => { bundles.AddRange(o.ConfigureBuild()); });

            BuildItemsAny.ForEach(o => { bundles.AddRange(o.ConfigureBuild()); });

            BuildItemsLast.ForEach(o => { bundles.AddRange(o.ConfigureBuild()); });

            return bundles;
        }

        public override void GenerateLoadAssets(TypeDefinition plugin, ILProcessor il)
        {
#if H3VR_IMPORTED
            EnsurePluginDependsOn(plugin, "h3vr.otherloader", "1.1.5");
#endif

            //If set to self load, we add a bunch of code to load the items
            if (SelfLoading)
            {
                //Create lists of the bundles
                string[] loadFirst = BuildItemsFirst.Select(o => o.BundleName.ToLower()).ToArray();
                string[] loadAny = BuildItemsAny.Select(o => o.BundleName.ToLower()).ToArray();
                string[] loadLast = BuildItemsLast.Select(o => o.BundleName.ToLower()).ToArray();


                // Get references to the path and the method we're calling
                var publicStatic = BindingFlags.Public | BindingFlags.Static;
                FieldReference basePath = plugin.Fields.First(f => f.Name == "BasePath");
                var otherloaderRegisterLoad = typeof(OtherLoader.OtherLoader).GetMethod("RegisterDirectLoad", publicStatic);


                // Now load the path, guid, dependancies, and pass the 3 arrays of bundle names
                il.Emit(OpCodes.Ldsfld, basePath);
                il.Emit(OpCodes.Ldstr, BuildWindow.SelectedProfile.Author + "." + BuildWindow.SelectedProfile.PackageName);
                il.Emit(OpCodes.Ldstr, string.Join(",", LoadDependancies.ToArray()));
                il.Emit(OpCodes.Ldstr, string.Join(",", loadFirst));
                il.Emit(OpCodes.Ldstr, string.Join(",", loadAny));
                il.Emit(OpCodes.Ldstr, string.Join(",", loadLast));
                il.Emit(OpCodes.Call, plugin.Module.ImportReference(otherloaderRegisterLoad));
            }
        }

    }
}

