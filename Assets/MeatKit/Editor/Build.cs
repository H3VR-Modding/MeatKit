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
        private const string BaseOutputPath = "AssetBundles/";

        private static string GetPackageOutputPath(BuildProfile profile)
        {
            return Path.Combine(BaseOutputPath, Path.Combine(profile.PackageName, profile.Version)) + Path.DirectorySeparatorChar;
        }
        
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

            string bundleOutputPath = profile.ExportPath;

            // Start a stopwatch to time the build
            Stopwatch sw = Stopwatch.StartNew();

            // If there's anything invalid in the settings don't continue
            if (!profile.EnsureValidForEditor()) return;

            // Clean the output folder
            CleanBuild(profile);

            // Make a copy of the editor assembly because when we build an asset bundle, Unity will delete it
            string editorAssembly = EditorAssemblyPath + AssemblyName + ".dll";
            string tempAssemblyFile = Path.GetTempFileName();
            File.Copy(editorAssembly, tempAssemblyFile, true);
            
            // Make sure we have the virtual reality supported checkbox enabled
            // If this is not set to true when we build our asset bundles, the shaders will not compile correctly
            bool wasVirtualRealitySupported = PlayerSettings.virtualRealitySupported;
            PlayerSettings.virtualRealitySupported = true;
            
            // Create a map of assembly names to what we want to rename them to, then enable bundle processing
            var replaceMap = new Dictionary<string, string>
            {
                {AssemblyName + ".dll", profile.PackageName + ".dll"},
                {AssemblyFirstpassName + ".dll", profile.PackageName + "-firstpass.dll"},
                {AssemblyRename + ".dll", AssemblyName + ".dll"},
                {AssemblyFirstpassRename + ".dll", AssemblyFirstpassName + ".dll"}
            };
            AssetBundleIO.EnableProcessing(replaceMap);

            // Get the list of asset bundle configurations and build them
            var bundles = profile.BuildItems.SelectMany(x => x.ConfigureBuild()).ToArray();
            BuildPipeline.BuildAssetBundles(bundleOutputPath, bundles, BuildAssetBundleOptions.None,
                BuildTarget.StandaloneWindows64);
            
            // Disable bundle processing now that we're done with it.
            var requiredScripts = AssetBundleIO.DisableProcessing();
            
            // Cleanup the unused files created with building the bundles
            foreach (var file in Directory.GetFiles(bundleOutputPath, "*.manifest"))
                File.Delete(file);
            File.Delete(Path.Combine(bundleOutputPath, profile.Version));
            
            // Reset the virtual reality supported checkbox, so if the user had it disabled it will stay disabled
            PlayerSettings.virtualRealitySupported = wasVirtualRealitySupported;
            
            // And export the assembly to the folder
            ExportEditorAssembly(bundleOutputPath, tempAssemblyFile, requiredScripts);
            
            // Now we can write the Thunderstore stuff to the folder
            profile.WriteThunderstoreManifest(bundleOutputPath + "manifest.json");

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
            File.WriteAllBytes(bundleOutputPath + "icon.png", icon.EncodeToPNG());

            // Copy the readme
            File.Copy(AssetDatabase.GetAssetPath(profile.ReadMe), bundleOutputPath + "README.md");

            string packageName = profile.Author + "-" + profile.PackageName;
            if (profile.BuildAction == BuildAction.CopyToProfile)
            {
                string pluginFolder = Path.Combine(profile.OutputProfile, "BepInEx/plugins/" + packageName);
                if (Directory.Exists(pluginFolder)) Directory.Delete(pluginFolder, true);
                Directory.CreateDirectory(pluginFolder);
                Extensions.CopyFilesRecursively(bundleOutputPath, pluginFolder);
            }
            else if (profile.BuildAction == BuildAction.CreateThunderstorePackage)
            {
                using (var zip = new ZipFile())
                {
                    zip.AddDirectory(bundleOutputPath, "");
                    zip.Save(Path.Combine(bundleOutputPath, packageName + "-" + profile.Version + ".zip"));
                }
            }

            // End the stopwatch and save the time
            MeatKitCache.LastBuildDuration = sw.Elapsed;
            MeatKitCache.LastBuildTime = DateTime.Now;
        }

        public static void CleanBuild(BuildProfile profile)
        {
            string outputPath = profile.ExportPath;
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
            Directory.CreateDirectory(outputPath);
        }
    }
}
