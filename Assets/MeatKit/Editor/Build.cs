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
        // Guards against re-entrant builds.
        internal static bool _buildRunning = false;

        public static void DoBuild()
        {
            if (_buildRunning)
            {
                return;
            }

            // Refuse to start while compiling or refreshing.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Cannot build",
                    "Scripts are currently being compiled or assets are refreshing. Please wait for compilation to finish and then try again.",
                    "Ok");
                return;
            }

            _buildRunning = true;
            EditorApplication.LockReloadAssemblies();

            BuildLog.StartNew();
            try
            {
                DoBuildInternal();
            }
            catch (System.Threading.ThreadAbortException)
            {
                System.Threading.Thread.ResetAbort();
                NativeHookManager.InsideEATI = false;
                NativeHookManager.InsideBundleEATI = false;
                BuildLog.SetCompletionStatus(true, "Build interrupted by domain reload.", null);
            }
            catch (MeatKitBuildException e)
            {
                NativeHookManager.InsideEATI = false;
                NativeHookManager.InsideBundleEATI = false;
                string message = e.Message;
                if (e.InnerException != null) message += "\n\n" + e.InnerException.Message;
                EditorUtility.DisplayDialog("Build failed", message, "Ok.");
                Debug.LogError("Build failed: " + message);
                BuildLog.SetCompletionStatus(true, "MeatKit Build Exception", e);
            }
            catch (Exception e)
            {
                NativeHookManager.InsideEATI = false;
                NativeHookManager.InsideBundleEATI = false;
                EditorUtility.DisplayDialog("Build failed with unknown error",
                    "Error message: " + e.Message + "\n\nCheck console for full exception text.", "Ok.");
                Debug.LogException(e);
                BuildLog.SetCompletionStatus(true, "Unexpected exception during build", e);
            }
            finally
            {
                _buildRunning = false;

                // Covers the throws between EnableProcessing() and DoBuildInternal's try block,
                // which that inner finally cannot see.
                try { AssetBundleIO.DisableProcessing(); }
                catch (Exception) { }

                try { BuildLog.Finish(); }
                catch (System.Threading.ThreadAbortException) { System.Threading.Thread.ResetAbort(); }
                catch (Exception) { }
                EditorApplication.delayCall += UnlockReloadDelayed;
            }
        }

        private static void UnlockReloadDelayed()
        {
            EditorApplication.UnlockReloadAssemblies();
        }

        // Build output is always x86_64. A mismatched editor target makes Unity run
        // "Determining assets that need to be reimported for target platform" on
        // every build, so match the target up front.
        [InitializeOnLoadMethod]
        private static void EnsureWindows64TargetOnLoad()
        {
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64) return;
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            }
            catch (Exception)
            {
            }
        }

        private static void DoBuildInternal()
        {
            if (ShowErrorIfH3VRNotImported()) return;

            // One-shot: repair H3VRCode MonoScript metadata that was corrupted by
            // FixRuntimeScriptReference during prior domain-reload cycles. Re-importing
            // the DLLs forces the MonoImporter to regenerate correct assembly-name refs.
            // Runs at most once per session, and before any guard, since the forced
            // reimport queues an AssetDatabase refresh of its own.
            if (!SessionState.GetBool("MeatKit.RepairedH3VRCodeMS2", false))
            {
                string managed = Path.Combine(Application.dataPath, "Managed");
                foreach (var dll in Directory.GetFiles(managed, "H3VRCode*.dll"))
                    AssetDatabase.ImportAsset("Assets/Managed/" + Path.GetFileName(dll),
                        ImportAssetOptions.ForceUpdate | ImportAssetOptions.DontDownloadFromCacheServer);
                SessionState.SetBool("MeatKit.RepairedH3VRCodeMS2", true);
            }

            BuildProfile profile = BuildWindow.SelectedProfile;
            if (!profile) return;

            string bundleOutputPath = profile.ExportPath;
            Stopwatch sw = Stopwatch.StartNew();

            if (!profile.EnsureValidForEditor()) return;

            BuildLog.WriteLine("Cleaning build folder");
            CleanBuild(profile);

            // Back up editor assemblies — BuildAssetBundles deletes them
            string editorAssembly  = EditorAssemblyPath + AssemblyName + ".dll";
            string editorFirstpass = EditorAssemblyPath + AssemblyFirstpassName + ".dll";
            string tempAssemblyFile  = Path.GetTempFileName();
            string tempFirstpassFile = Path.GetTempFileName();
            DateTime editorAsmMtime = File.GetLastWriteTimeUtc(editorAssembly);
            DateTime editorFpMtime  = File.Exists(editorFirstpass) ? File.GetLastWriteTimeUtc(editorFirstpass) : DateTime.UtcNow;
            BuildLog.WriteLine("Copying editor assembly: " + editorAssembly + " -> " + tempAssemblyFile);
            File.Copy(editorAssembly, tempAssemblyFile, true);
            if (File.Exists(editorFirstpass))
            {
                BuildLog.WriteLine("Copying editor firstpass: " + editorFirstpass + " -> " + tempFirstpassFile);
                File.Copy(editorFirstpass, tempFirstpassFile, true);
            }

            // VR support must be on or shaders compile incorrectly
            BuildLog.WriteLine("Forcing VR support on");
            bool wasVirtualRealitySupported = PlayerSettings.virtualRealitySupported;
            PlayerSettings.virtualRealitySupported = true;

            // Assembly rename map.
            // H3VRCode-CSharp → Assembly-CSharp is included here so the rename happens during
            // MonoScriptTransferWrite (before LZ4 compression), ensuring it is visible even when
            // the type-tree ends up inside a compressed LZ4 block where PostProcessBundles'
            // byte-search cannot reach it.  The OnMonoScriptTransferWrite hook restores the
            // original name after OrigTransferWrite, so in-memory state stays correct for
            // subsequent builds.
            var replaceMap = new Dictionary<string, string>
            {
                {AssemblyName + ".dll", profile.PackageName + ".dll"},
                {AssemblyFirstpassName + ".dll", profile.PackageName + "-firstpass.dll"},
                {AssemblyRename + ".dll", AssemblyName + ".dll"},
                {AssemblyFirstpassRename + ".dll", AssemblyFirstpassName + ".dll"}
            };
            BuildLog.WriteLine("Enabling bundle processing.");
            BuildLog.WriteLine("Replace map:");
            foreach (var key in replaceMap.Keys)
                BuildLog.WriteLine("  " + key + " -> " + replaceMap[key]);
            BuildLog.WriteLine("Ignored types (Assembly-CSharp.dll):");
            foreach (var type in StripAssemblyTypes)
                BuildLog.WriteLine("  " + type);
            AssetBundleIO.EnableProcessing(replaceMap, false, true);

            // Get the list of asset bundle configurations and build them
            BuildLog.WriteLine("Collecting bundles from build items");
            var bundles = profile.BuildItems.SelectMany(x => x.ConfigureBuild()).ToArray();

            BuildLog.WriteLine(bundles.Length + " bundles to build.");
            var bundleNameMap = new Dictionary<string, string>();
            for (var i = 0; i < bundles.Length; i++)
            {
                var originalName = bundles[i].assetBundleName;
                // Prefix bundle name to prevent runtime load conflicts
                var buildTimeName = (profile.Author + "." + profile.PackageName + "." + originalName).ToLower();
                if (bundleNameMap.ContainsKey(buildTimeName))
                    throw new MeatKitBuildException("Two or more AssetBundles share the same name - this is not " +
                                                    "supported. Make sure all your AssetBundles have unique names.");
                bundleNameMap[buildTimeName] = originalName;
                bundles[i].assetBundleName = buildTimeName;
            }

            // Guard H3VRCode TypeTree during BuildAssetBundles
            Action _beforeEATI = delegate
            {
                // Stage H3VRCode into ScriptAssemblies before EATI processes them
                ManagedPluginDomainFix.EnsureH3VRCodeInScriptAssemblies();
                NativeHookManager.InsideEATI = true;
                // Block ALL Assets/Managed/ DLLs during bundle build (cleared after)
                NativeHookManager.InsideBundleEATI = true;
                // Re-prime MonoBehaviour caches so EATI generates TypeTrees from full H3VRCode
                ManagedPluginDomainFix.ReprimeMBCachesBeforeEATI();

            };

            Action _afterEATI = delegate
            {
                // Re-prime m_ScriptCache after EATI resets it
                ManagedPluginDomainFix.ReprimeSilentAfterEATI();
            };

            // Prime TypeTrees before registering EATI hooks to avoid SaveAssets() race
            ManagedPluginDomainFix.PrimeTypeTreesForBuild();



            NativeHookManager.BeforeEATICallbacks.Add(_beforeEATI);
            NativeHookManager.AfterEATICallbacks.Add(_afterEATI);

            // NOTE: no StartAssetEditing wrapper here. Proven by gate test:
            // building bundles while asset editing is active makes Unity refuse
            // with "Cannot build AssetBundles while imports of assets are in progress".
            AssetBundleManifest bundleManifest = null;
            try
            {
                BuildLog.WriteLine("Calling BuildAssetBundles (isCompiling=" + EditorApplication.isCompiling + " InsideEATI=" + NativeHookManager.InsideEATI + ")");
                bundleManifest = BuildPipeline.BuildAssetBundles(bundleOutputPath, bundles,
                    BuildAssetBundleOptions.ChunkBasedCompression,
                    BuildTarget.StandaloneWindows64);
                BuildLog.WriteLine("BuildAssetBundles returned. manifest=" + (bundleManifest != null ? "OK" : "NULL") + " isCompiling=" + EditorApplication.isCompiling);
            }
            finally
            {
                NativeHookManager.BeforeEATICallbacks.Remove(_beforeEATI);
                NativeHookManager.AfterEATICallbacks.Remove(_afterEATI);
                NativeHookManager.InsideEATI = false;
                NativeHookManager.InsideBundleEATI = false;

                // Must be here, not after the manifest check: a throw from BuildAssetBundles
                // never reaches the old call site, leaving the write hook armed. See DoBuild's
                // finally, which is what actually covers every other failure path.
                AssetBundleIO.DisableProcessing();
            }

            if (bundleManifest == null)
                throw new MeatKitBuildException("AssetBundle build failed to produce a manifest. Check the console for errors.");

            BuildLog.WriteLine("Bundles built");

            // If no firstpass existed before build, capture the one generated by standalone compile
            if (new FileInfo(tempFirstpassFile).Length == 0 && File.Exists(editorFirstpass) && new FileInfo(editorFirstpass).Length > 0)
            {
                File.Copy(editorFirstpass, tempFirstpassFile, true);
                BuildLog.WriteLine("Updated firstpass backup with standalone-compiled version (" + new FileInfo(tempFirstpassFile).Length + " bytes)");
            }

            // Validate firstpass backup before restore (0-byte DLL causes domain reload crash)
            long firstpassBackupSize = new FileInfo(tempFirstpassFile).Length;
            bool firstpassBackupIsValid = firstpassBackupSize > 0;

            // Restore editor DLLs with original mtime to prevent spurious recompile
            if (File.Exists(tempAssemblyFile))
            {
                try
                {
                    File.Copy(tempAssemblyFile, editorAssembly, true);
                    File.SetLastWriteTimeUtc(editorAssembly, editorAsmMtime);
                    BuildLog.WriteLine("Restored Assembly-CSharp.dll");
                }
                catch (Exception ex)
                {
                    BuildLog.WriteLine("WARNING: Failed to restore Assembly-CSharp.dll: " + ex.Message);
                }
            }
            // Restore firstpass (with mtime to suppress Unity's mtime-based recompile trigger)
            if (firstpassBackupIsValid && File.Exists(tempFirstpassFile))
            {
                try
                {
                    File.Copy(tempFirstpassFile, editorFirstpass, true);
                    File.SetLastWriteTimeUtc(editorFirstpass, editorFpMtime);
                    long restoredSize = new FileInfo(editorFirstpass).Length;
                    BuildLog.WriteLine("Restored Assembly-CSharp-firstpass.dll (" + restoredSize + " bytes, mtime=" + editorFpMtime.ToString("HH:mm:ss.fff") + ")");
                    // Sanity check restored file
                    if (restoredSize == 0)
                    {
                        BuildLog.WriteLine("ERROR: Restored firstpass is 0 bytes!  This will cause domain reload failure on next build.");
                    }
                }
                catch (Exception ex)
                {
                    BuildLog.WriteLine("WARNING: Failed to restore Assembly-CSharp-firstpass.dll: " + ex.Message);
                }
            }
            else if (!firstpassBackupIsValid)
            {
                BuildLog.WriteLine("SKIPPED: Assembly-CSharp-firstpass.dll restore — backup is " + firstpassBackupSize + " bytes (invalid)");
            }

            // Cleanup manifest files
            BuildLog.WriteLine("Cleaning unused files");
            foreach (var file in Directory.GetFiles(bundleOutputPath, "*.manifest"))
                File.Delete(file);
            File.Delete(Path.Combine(bundleOutputPath, profile.Version));

            // Rename built bundles back to their original names
            BuildLog.WriteLine("Restoring original bundle names");
            foreach (var entry in bundleNameMap)
            {
                var buildTimeNamePath = Path.Combine(bundleOutputPath, entry.Key);
                var originalNamePath = Path.Combine(bundleOutputPath, entry.Value);
                if (!File.Exists(buildTimeNamePath))
                    throw new MeatKitBuildException("One or more AssetBundles have failed to build! Check the " +
                                                    "console/build items for errors. Make sure your bundle names " +
                                                    "don't contain any illegal characters. " +
                                                    "Name of bundle that failed: " + entry.Value);
                File.Move(buildTimeNamePath, originalNamePath);
            }

            // Binary-replace H3VRCode-CSharp → Assembly-CSharp in every output bundle file.
            // Both strings are 15 bytes, so the replacement is in-place and preserves every
            // length prefix in the binary format (including MMHOOK-H3VRCode-CSharp variants).
            PostProcessBundles(bundleOutputPath);

            if (PlayerSettings.virtualRealitySupported != wasVirtualRealitySupported)
                PlayerSettings.virtualRealitySupported = wasVirtualRealitySupported;

            BuildLog.WriteLine("Exporting editor assembly");
            var requiredScripts = AssetBundleIO.SerializedScriptNames;
            ExportEditorAssembly(bundleOutputPath, tempAssemblyFile, requiredScripts);
            try { if (File.Exists(tempAssemblyFile))  File.Delete(tempAssemblyFile);  } catch { }
            try { if (File.Exists(tempFirstpassFile)) File.Delete(tempFirstpassFile); } catch { }

            // Thunderstore packaging
            BuildLog.WriteLine("Writing Thunderstore manifest");
            profile.WriteThunderstoreManifest(bundleOutputPath + "manifest.json");

            Texture2D icon = profile.Icon;
            var importSettings = (TextureImporter) AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(profile.Icon));
            if (!importSettings.isReadable ||
                importSettings.textureCompression != TextureImporterCompression.Uncompressed)
            {
                BuildLog.WriteLine("Fixing icon import settings");
                importSettings.isReadable = true;
                importSettings.textureCompression = TextureImporterCompression.Uncompressed;
                importSettings.SaveAndReimport();
            }

            if (profile.Icon.width != 256 || profile.Icon.height != 256)
            {
                BuildLog.WriteLine("Resizing icon to 256x256");
                icon = icon.ScaleTexture(256, 256);
            }

            File.WriteAllBytes(bundleOutputPath + "icon.png", icon.EncodeToPNG());

            var readmePath = bundleOutputPath + "README.md";
            File.Copy(AssetDatabase.GetAssetPath(profile.ReadMe), readmePath);

            if (profile.Changelog)
                File.Copy(AssetDatabase.GetAssetPath(profile.Changelog), bundleOutputPath + "CHANGELOG.md");

            string packageName = profile.Author + "-" + profile.PackageName;
            if (profile.BuildAction == BuildAction.CopyToProfile)
            {
                BuildLog.WriteLine("Copying built files to profile");
                string pluginFolder = Path.Combine(profile.OutputProfile, "BepInEx/plugins/" + packageName);
                if (Directory.Exists(pluginFolder)) Directory.Delete(pluginFolder, true);
                Directory.CreateDirectory(pluginFolder);
                Extensions.CopyFilesRecursively(bundleOutputPath, pluginFolder);
            }

            if (profile.BuildAction == BuildAction.CreateThunderstorePackage)
            {
                BuildLog.WriteLine("Zipping built files");
                using (var zip = new ZipFile())
                {
                    zip.AddDirectory(bundleOutputPath, "");
                    var zipPath = Path.Combine(bundleOutputPath, packageName + "-" + profile.Version + ".zip");
                    zip.Save(zipPath);
                    
                    if (MeatKitCache.OpenFolderAfterBuild && File.Exists(zipPath))
                        EditorUtility.RevealInFinder(zipPath);
                }
            }
            else
            {
                if (MeatKitCache.OpenFolderAfterBuild && File.Exists(readmePath))
                    EditorUtility.RevealInFinder(readmePath);
            }

            BuildLog.SetCompletionStatus(false, "", null);
            MeatKitCache.LastBuildDuration = sw.Elapsed;
            MeatKitCache.LastBuildTime = DateTime.Now;
        }

        public static void CleanBuild(BuildProfile profile)
        {
            string outputPath = profile.ExportPath;
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
            Directory.CreateDirectory(outputPath);
        }

        /// <summary>
        /// Binary-replaces "H3VRCode-CSharp" with "Assembly-CSharp" in all bundle files.
        /// Both are 15 bytes so the replacement is in-place and format-safe.
        /// </summary>
        private static void PostProcessBundles(string outputPath)
        {
            byte[] oldBytes = System.Text.Encoding.ASCII.GetBytes(AssemblyRename); // "H3VRCode-CSharp" (15)
            byte[] newBytes = System.Text.Encoding.ASCII.GetBytes(AssemblyName);   // "Assembly-CSharp"  (15)
            // Sanity-check: both must be the same length for in-place replacement to be safe.
            if (oldBytes.Length != newBytes.Length)
            {
                BuildLog.WriteLine("[PostProcessBundles] SKIP: assembly name lengths differ (" +
                                   oldBytes.Length + " vs " + newBytes.Length + ") — binary replacement unsafe");
                return;
            }
            int patched = 0;
            foreach (var file in Directory.GetFiles(outputPath))
            {
                try
                {
                    byte[] data = File.ReadAllBytes(file);
                    bool modified = false;
                    for (int i = 0; i <= data.Length - oldBytes.Length; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < oldBytes.Length; j++)
                        {
                            if (data[i + j] != oldBytes[j]) { match = false; break; }
                        }
                        if (match)
                        {
                            for (int j = 0; j < newBytes.Length; j++)
                                data[i + j] = newBytes[j];
                            i += newBytes.Length - 1; // skip past replacement
                            modified = true;
                            patched++;
                        }
                    }
                    if (modified)
                        File.WriteAllBytes(file, data);
                }
                catch (Exception ex)
                {
                    BuildLog.WriteLine("[PostProcessBundles] WARNING: failed to patch " + file + ": " + ex.Message);
                }
            }
            BuildLog.WriteLine("[PostProcessBundles] Patched " + patched + " occurrence(s) of \"" +
                               AssemblyRename + "\" → \"" + AssemblyName + "\" across bundle files");
        }

    }
}
