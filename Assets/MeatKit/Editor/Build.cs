﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Ionic.Zip;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    public partial class MeatKit
    {
        public static void DoBuild()
        {
            // Make sure the scripts are imported.
            if (ShowErrorIfH3VRNotImported()) return;

            // Get our profile and make sure it isn't null
            BuildProfile profile = BuildWindow.SelectedProfile;
            if (!profile) return;
            
            // Start a stopwatch to time the build
            Stopwatch sw = Stopwatch.StartNew();

            // If there's anything invalid in the settings don't continue
            if (!profile.EnsureValidForEditor()) return;

            // Clean the output folder
            CleanBuild();

            // And export the assembly to the folder
            ExportEditorAssembly(BundleOutputPath);

            // Then get their asset bundle configurations
            var bundles = profile.BuildItems
                .Select(x => x.ConfigureBuild())
                .Where(x => x != null)
                .Select(x => x.Value).ToArray();

            BuildPipeline.BuildAssetBundles(BundleOutputPath, bundles, BuildAssetBundleOptions.None,
                BuildTarget.StandaloneWindows64);

            // Cleanup the unused files created with building the bundles
            foreach (var file in Directory.GetFiles(BundleOutputPath, "*.manifest"))
                File.Delete(file);
            File.Delete(Path.Combine(BundleOutputPath, "AssetBundles"));

            // With the bundles done building we can process them
            var replaceMap = new Dictionary<string, string>
            {
                {"Assembly-CSharp.dll", profile.PackageName + ".dll"},
                {"Assembly-CSharp-firstpass.dll", profile.PackageName + "-firstpass.dll"},
                {"H3VRCode-CSharp.dll", "Assembly-CSharp.dll"},
                {"H3VRCode-CSharp-firstpass.dll", "Assembly-CSharp-firstpass.dll"}
            };

            foreach (var bundle in bundles)
            {
                var path = Path.Combine(BundleOutputPath, bundle.assetBundleName);
                ProcessBundle(path, path, replaceMap, profile.BundleCompressionType);
            }

            // Now we can write the Thunderstore stuff to the folder
            profile.WriteThunderstoreManifest(BundleOutputPath + "manifest.json");

            // Check if the icon is already 256x256
            Texture2D icon = profile.Icon;
            if (profile.Icon.width != 256 || profile.Icon.height != 256)
            {
                // If not, make sure the texture is readable and not compressed
                var importSettings = (TextureImporter) AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(profile.Icon));
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
                    zip.Save(Path.Combine(BundleOutputPath, packageName + ".zip"));
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