using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Ionic.Zip;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MeatKit
{
    public partial class MeatKit
    {
        public static void DoBuild()
        {
            try
            {
                DoBuildInternal();
            }
            catch (MeatKitBuildException e)
            {
                string message = e.Message;
                if (e.InnerException != null) message += "\n\n" + e.InnerException.Message;
                EditorUtility.DisplayDialog("Build failed", message, "Ok.");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Build failed with unknown error",
                    "Error message: " + e.Message + "\n\nCheck console for full exception text.", "Ok.");
                Debug.LogException(e);
            }
        }

        private static void DoBuildInternal()
        {
            // Make sure the scripts are imported.
            if (ShowErrorIfH3VRNotImported()) return;

            // Get our profile and make sure it isn't null
            BuildProfile profile = BuildWindow.SelectedProfile;
            if (!profile) return;

            //BundleOutputPath = BundleOutputPathBase;
            BundleOutputPath = Path.Combine(Path.Combine(BundleOutputPathBase, profile.PackageName),profile.Version) + "/";


            // Start a stopwatch to time the build
            Stopwatch sw = Stopwatch.StartNew();

            // If there's anything invalid in the settings don't continue
            if (!profile.EnsureValidForEditor()) return;

            // Clean the output folder
            CleanBuild();

            // Make a copy of the editor assembly because when we build an asset bundle, Unity will delete it
            string editorAssembly = EditorAssemblyPath + AssemblyName + ".dll";
            string tempAssemblyFile = Path.GetTempFileName();
            File.Copy(editorAssembly, tempAssemblyFile, true);
            
            // Then get their asset bundle configurations
            var bundles = profile.BuildItems.SelectMany(x => x.ConfigureBuild()).ToArray();
            //Debug.Log("BundleOutputPath: " + BundleOutputPath);
            BuildPipeline.BuildAssetBundles(BundleOutputPath, bundles, BuildAssetBundleOptions.None,
                BuildTarget.StandaloneWindows64);
            //Debug.Log("Bloop!");
            // Cleanup the unused files created with building the bundles
            foreach (var file in Directory.GetFiles(BundleOutputPath, "*.manifest"))
                File.Delete(file);
            //File.Delete(Path.Combine(BundleOutputPath, "AssetBundles"));
            File.Delete(Path.Combine(BundleOutputPath, profile.Version));

            // With the bundles done building we can process them
            var replaceMap = new Dictionary<string, string>
            {
                {"Assembly-CSharp.dll", profile.PackageName + ".dll"},
                {"Assembly-CSharp-firstpass.dll", profile.PackageName + "-firstpass.dll"},
                {"H3VRCode-CSharp.dll", "Assembly-CSharp.dll"},
                {"H3VRCode-CSharp-firstpass.dll", "Assembly-CSharp-firstpass.dll"}
            };

            Dictionary<string, List<string>> requiredScripts = new Dictionary<string, List<string>>();
            foreach (var bundle in bundles)
            {
                var path = Path.Combine(BundleOutputPath, bundle.assetBundleName);
                ProcessBundle(path, path, replaceMap, profile.BundleCompressionType, requiredScripts);
            }

            // And export the assembly to the folder
            ExportEditorAssembly(BundleOutputPath, tempAssemblyFile, requiredScripts);
            
            // Now we can write the Thunderstore stuff to the folder
            profile.WriteThunderstoreManifest(BundleOutputPath + "manifest.json");

            // Check if the icon is already 256x256
            Texture2D icon = profile.Icon;

            // Make sure our icon is marked as readable
            var importSettings = (TextureImporter) AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(profile.Icon));
            if (!importSettings.isReadable ||
                importSettings.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importSettings.isReadable = true;
                importSettings.textureCompression = TextureImporterCompression.Uncompressed;
                importSettings.SaveAndReimport();
            }

            if (profile.Icon.width != 256 || profile.Icon.height != 256)
            {
                // Resize it for the build
                icon = icon.ScaleTexture(256, 256);
            }

            // Write the texture to file
            File.WriteAllBytes(BundleOutputPath + "icon.png", icon.EncodeToPNG());

            // Copy the readme
            File.Copy(AssetDatabase.GetAssetPath(profile.ReadMe), BundleOutputPath + "README.md");

            string packageName = profile.Author + "-" + profile.PackageName;
            if (profile.BuildAction == BuildAction.CopyToProfile)
            {
                string pluginFolder = Path.Combine(profile.OutputProfile, "BepInEx/plugins/" + packageName);
                if (Directory.Exists(pluginFolder)) Directory.Delete(pluginFolder, true);
                Directory.CreateDirectory(pluginFolder);
                Extensions.CopyFilesRecursively(BundleOutputPath, pluginFolder);
            }
            else if (profile.BuildAction == BuildAction.CreateThunderstorePackage)
            {
                using (var zip = new ZipFile())
                {
                    zip.AddDirectory(BundleOutputPath, "");
                    zip.Save(Path.Combine(BundleOutputPath, packageName + "-" + profile.Version + ".zip"));
                }
            }

            // End the stopwatch and save the time
            MeatKitCache.LastBuildDuration = sw.Elapsed;
            MeatKitCache.LastBuildTime = DateTime.Now;
        }

        public static void CleanBuild()
        {
            if (Directory.Exists(BundleOutputPath)) Directory.Delete(BundleOutputPath, true);
            Directory.CreateDirectory(BundleOutputPath);
        }
    }
}