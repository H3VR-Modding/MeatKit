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
                gameManagedLocation = EditorUtility.OpenFolderPanel("Select H3VR Managed directory", string.Empty, "Managed");
                MeatKitCache.GameManagedLocation = gameManagedLocation;
            }

            // If it's _still_ empty, the user must have cancelled.
            if (string.IsNullOrEmpty(gameManagedLocation)) return;
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
            if (!BuildWindow.SelectedProfile.EnsureValidForEditor()) return;
            ExportEditorAssembly(BundleOutputPath);
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
            var replaceMap = new Dictionary<string, string>
            {
                {"Assembly-CSharp.dll", "H3VRCode-CSharp.dll"},
                {"Assembly-CSharp-firstpass.dll", "H3VRCode-CSharp-firstpass.dll"}
            };

            ProcessBundle(assetBundlePath, assetBundlePath + "-imported", replaceMap, AssetBundleCompressionType.NONE);
        }

        [MenuItem("MeatKit/Build Window", priority = 2)]
        public static void ConfigureBuild()
        {
            Selection.activeObject = BuildSettings.Instance;
        }

        public static void CleanBuild()
        {
            if (Directory.Exists(BundleOutputPath)) Directory.Delete(BundleOutputPath, true);
            Directory.CreateDirectory(BundleOutputPath);
        }

        public static void DoBuild()
        {
            // Make sure the scripts are imported.
            if (ShowErrorIfH3VRNotImported()) return;

            // Start a stopwatch to time the build
            Stopwatch sw = Stopwatch.StartNew();

            // If there's anything invalid in the settings don't continue
            var settings = BuildSettings.Instance;
            if (!settings.EnsureValidForEditor()) return;

            // Clean the output folder
            CleanBuild();

            // And export the assembly to the folder
            ExportEditorAssembly(BundleOutputPath);

            // Then get their asset bundle configurations
            var bundles = settings.BuildItems.SelectMany(x => x.ConfigureBuild()).ToArray();

            BuildPipeline.BuildAssetBundles(BundleOutputPath, bundles, BuildAssetBundleOptions.None,
                BuildTarget.StandaloneWindows64);

            // Cleanup the unused files created with building the bundles
            foreach (var file in Directory.GetFiles(BundleOutputPath, "*.manifest"))
                File.Delete(file);
            File.Delete(Path.Combine(BundleOutputPath, "AssetBundles"));

            // With the bundles done building we can process them
            var replaceMap = new Dictionary<string, string>
            {
                {"Assembly-CSharp.dll", settings.PackageName + ".dll"},
                {"Assembly-CSharp-firstpass.dll", settings.PackageName + "-firstpass.dll"},
                {"H3VRCode-CSharp.dll", "Assembly-CSharp.dll"},
                {"H3VRCode-CSharp-firstpass.dll", "Assembly-CSharp-firstpass.dll"}
            };

            foreach (var bundle in bundles)
            {
                var path = Path.Combine(BundleOutputPath, bundle.assetBundleName);
                ProcessBundle(path, path, replaceMap, settings.BundleCompressionType);
            }

            // Now we can write the Thunderstore stuff to the folder
            settings.WriteThunderstoreManifest(BundleOutputPath + "manifest.json");

            // Check if the icon is already 256x256
            Texture2D icon = settings.Icon;
            if (settings.Icon.width != 256 || settings.Icon.height != 256)
            {
                // If not, make sure the texture is readable and not compressed
                var importSettings = (TextureImporter) AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(settings.Icon));
                if (!importSettings.isReadable || importSettings.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    importSettings.isReadable = true;
                    importSettings.textureCompression = TextureImporterCompression.Uncompressed;
                    importSettings.SaveAndReimport();
                }

                // Then resize it for the build
                icon = icon.ScaleTexture(256, 256);
            }

            // Write the texture to file
            File.WriteAllBytes(BundleOutputPath + "icon.png", icon.EncodeToPNG());

            // Copy the readme
            File.Copy(AssetDatabase.GetAssetPath(settings.ReadMe), BundleOutputPath + "README.md");


            //Now that all files are in place, you can perform post processing on them
            foreach(BuildItem buildItem in settings.BuildItems)
            {
                buildItem.PostProcessBuild(BundleOutputPath);
            }

            string packageName = settings.Author + "-" + settings.PackageName;
            if (settings.BuildAction == BuildAction.CopyToProfile)
            {
                string pluginFolder = Path.Combine(settings.OutputProfile, "BepInEx/plugins/" + packageName);
                if (Directory.Exists(pluginFolder)) Directory.Delete(pluginFolder, true);
                Directory.CreateDirectory(pluginFolder);
                Extensions.CopyFilesRecursively(BundleOutputPath, pluginFolder);
            }
            else if (settings.BuildAction == BuildAction.CreateThunderstorePackage)
            {
                using (var zip = new ZipFile())
                {
                    zip.AddDirectory(BundleOutputPath, "");
                    zip.Save(Path.Combine(BundleOutputPath, packageName + ".zip"));
                }
            }

            // End the stopwatch and save the time
            MeatKitCache.LastBuildDuration = sw.Elapsed;
            MeatKitCache.LastBuildTime = DateTime.Now;

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