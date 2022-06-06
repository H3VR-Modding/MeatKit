using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MeatKit
{
    public static partial class MeatKit
    {
        private static readonly string ManagedDirectory = Path.Combine(Application.dataPath, "MeatKit/Managed/");

        private static bool ShowErrorIfH3VRNotImported()
        {
#if (H3VR_IMPORTED == false)
            EditorUtility.DisplayDialog("Cannot continue.", "You don't have the H3 scripts imported. Please do that before trying to export anything.", "Ok");
            return true;
#endif
            return false;
        }


        [MenuItem("MeatKit/Scripts/Import Game", priority = 0)]
        public static void ImportAssemblies()
        {
            // If the path has never been set, or no longer exists, prompt the user to find it again
            var gameManagedLocation = MeatKitCache.GameManagedLocation;
            if (string.IsNullOrEmpty(gameManagedLocation) || !Directory.Exists(gameManagedLocation))
            {
                gameManagedLocation =
                    EditorUtility.OpenFolderPanel("Select H3VR Managed directory", string.Empty, "Managed");
                MeatKitCache.GameManagedLocation = gameManagedLocation;
            }

            // If it's _still_ empty, the user must have cancelled.
            if (string.IsNullOrEmpty(gameManagedLocation)) return;
            if (!File.Exists(Path.Combine(gameManagedLocation, "Assembly-CSharp.dll")))
            {
                EditorUtility.DisplayDialog("Error", "Looks like the path you selected is invalid. Make sure you are selecting the h3vr_Data/Managed folder in the game directory.", "Ok");
                MeatKitCache.GameManagedLocation = "";
                return;
            }
            
            ImportAssemblies(gameManagedLocation, ManagedDirectory);
        }

        [MenuItem("MeatKit/Scripts/Import Single", priority = 0)]
        public static void ImportSingleAssembly()
        {
            var assemblyLocation =
                EditorUtility.OpenFilePanel("Select assembly", null, "dll");
            if (string.IsNullOrEmpty(assemblyLocation)) return;
            MeatKitCache.LastImportedAssembly = assemblyLocation;
            ImportSingleAssembly(assemblyLocation, ManagedDirectory);
            Debug.Log("Finished importing " + assemblyLocation);
        }

        [MenuItem("MeatKit/Scripts/Re-Import Last", priority = 0)]
        public static void ReimportLast()
        {
            if (string.IsNullOrEmpty(MeatKitCache.LastImportedAssembly))
            {
                Debug.Log("Nothing to re-import.");
                return;
            }

            ImportSingleAssembly(MeatKitCache.LastImportedAssembly, ManagedDirectory);
            Debug.Log("Re-imported " + MeatKitCache.LastImportedAssembly);
        }

        [MenuItem("MeatKit/Scripts/Export", priority = 0)]
        public static void ExportEditorScripts()
        {
            // Make sure the scripts are imported and there are no errors before exporting
            if (ShowErrorIfH3VRNotImported()) return;
            if (!BuildWindow.SelectedProfile) return;

            if (!BuildWindow.SelectedProfile.EnsureValidForEditor()) return;
            ExportEditorAssembly(BuildWindow.SelectedProfile.ExportPath);
        }


        [MenuItem("MeatKit/Asset Bundle/Export", priority = 1)]
        public static void ExportBundle()
        {
            var assetBundlePath = EditorUtility.OpenFilePanel("Select asset bundle", Application.dataPath, "");
            var settings = BuildWindow.SelectedProfile;
            var replaceMap = new Dictionary<string, string>
            {
                {"Assembly-CSharp.dll", settings.PackageName + ".dll"},
                {"Assembly-CSharp-firstpass.dll", settings.PackageName + "-firstpass.dll"},
                {"H3VRCode-CSharp.dll", "Assembly-CSharp.dll"},
                {"H3VRCode-CSharp-firstpass.dll", "Assembly-CSharp-firstpass.dll"}
            };

            ProcessBundle(assetBundlePath, assetBundlePath, replaceMap, AssetBundleCompressionType.LZ4);
        }

        [MenuItem("MeatKit/Asset Bundle/Import", priority = 1)]
        public static void ImportBundle()
        {
            var assetBundlePath = EditorUtility.OpenFilePanel("Select asset bundle", Application.dataPath, "");
            if (string.IsNullOrEmpty(assetBundlePath)) return;

            var replaceMap = new Dictionary<string, string>
            {
                {"Assembly-CSharp.dll", "H3VRCode-CSharp.dll"},
                {"Assembly-CSharp-firstpass.dll", "H3VRCode-CSharp-firstpass.dll"}
            };

            ProcessBundle(assetBundlePath, assetBundlePath + "-imported", replaceMap, AssetBundleCompressionType.NONE);
        }

        [MenuItem("MeatKit/Asset Bundle/Import Folder", priority = 1)]
        public static void ImportBundlesInFolder()
        {
            // Get a folder from the user (or exit if they cancel)
            var folderPath = EditorUtility.OpenFolderPanel("Select folder", Application.dataPath, "");
            if (string.IsNullOrEmpty(folderPath)) return;

            var replaceMap = new Dictionary<string, string>
            {
                {"Assembly-CSharp.dll", "H3VRCode-CSharp.dll"},
                {"Assembly-CSharp-firstpass.dll", "H3VRCode-CSharp-firstpass.dll"}
            };

            if (!Directory.Exists(Application.streamingAssetsPath))
                Directory.CreateDirectory(Application.streamingAssetsPath);
            
            // Iterate over all the files in the selected folder and try to convert them.
            foreach (var file in Directory.GetFiles(folderPath))
            {
                // We're only interested in the files with no extension.
                if (Path.GetExtension(file) != "") continue;

                try
                {
                    string destinationFileName = Path.Combine(Application.streamingAssetsPath, Path.GetFileName(file));
                    ProcessBundle(file, destinationFileName, replaceMap, AssetBundleCompressionType.NONE);
                }
                catch (Exception e)
                {
                    Debug.Log(e);
                    // Ignored.
                }
            }
        }

        public static void ClearCache()
        {
            AssetDatabase.SaveAssets();

            if (Directory.Exists(ManagedDirectory))
                Directory.Delete(ManagedDirectory, true);
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, "");
            AssetDatabase.Refresh();
        }
    }
}