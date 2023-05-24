using System.IO;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    public static class CreateUpdatePackage
    {
        private static readonly string[] ExportAssets =
        {
            "Assets/MeatKit",
            "Assets/Managed/0Harmony.dll",
            "Assets/Managed/BepInEx.dll",
            "Assets/Managed/DotNetZip.dll",
            "Assets/Managed/Mono.Cecil.dll",
            "Assets/Managed/MonoMod.RuntimeDetour.dll",
            "Assets/Managed/MonoMod.Utils.dll",
            "Assets/Managed/Sodalite.dll",
            "Assets/Managed/Valve.Newtonsoft.Json.dll",
        };

        [MenuItem("MeatKit/Developer/Create update package")]
        public static void Create()
        {
            AssetDatabase.ExportPackage(ExportAssets, Updater.UpdatePackageName, ExportPackageOptions.Recurse);
            Debug.Log("Exported an update package to " + Path.Combine(Path.GetDirectoryName(Application.dataPath) , Updater.UpdatePackageName));
        }
    }
}