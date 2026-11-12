using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MeatKit
{
    // Copies H3VRCode DLLs to ScriptAssemblies/ on domain shutdown, widens Mono's probe path
    // to include UnityExtensions (fixes Sodalite/UnityEngine.UI), and injects H3VRCode's
    // MonoImage into MonoManager for correct TypeTree/CSD generation.
    [InitializeOnLoad]
    static class ManagedPluginDomainFix
    {
        private const bool DebugLogging = false;
        private const string H3VRCodeReimportFlagKey = "ManagedPluginDomainFix_H3VRReimportDone";


        private static readonly string _managedDir;
        private static readonly string _scriptAssembliesDir;
        private static readonly string _unityExtensionsDir;
        private static readonly string _pendingManifestPath;

        // Cached reflection — resolved once, valid for AppDomain lifetime.
        private static readonly FieldInfo _cachedPtrField = typeof(UnityEngine.Object).GetField(
            "m_CachedPtr", BindingFlags.NonPublic | BindingFlags.Instance);

        // Cached module handles — resolved lazily, valid for process lifetime.
        private static IntPtr _monoModule;
        private static IntPtr _unityModule;
        private static IntPtr GetMonoModule()
        {
            if (_monoModule == IntPtr.Zero)
            {
                _monoModule = GetModuleHandle("mono");
                if (_monoModule == IntPtr.Zero) _monoModule = GetModuleHandle("mono.dll");
            }
            return _monoModule;
        }

        private static IntPtr GetUnityModule()
        {
            if (_unityModule == IntPtr.Zero)
            {
                _unityModule = GetModuleHandle("Unity");
                if (_unityModule == IntPtr.Zero) _unityModule = GetModuleHandle("Unity.exe");
            }
            return _unityModule;
        }
        
        static ManagedPluginDomainFix()
        {
            _managedDir = Path.Combine(Application.dataPath, "Managed");
            string libraryDir = Path.GetFullPath(Path.Combine(Path.Combine(Application.dataPath, ".."), "Library"));
            _scriptAssembliesDir = Path.Combine(libraryDir, "ScriptAssemblies");
            _unityExtensionsDir = Path.Combine(EditorApplication.applicationContentsPath, "UnityExtensions");
            _pendingManifestPath = Path.Combine(Path.Combine(libraryDir, "PendingDllImports"), "manifest.txt");

            ApplyPendingDllImports();
            SetMonoAssemblySearchPaths();
            PreloadUnityExtensionDlls();
            AppDomain.CurrentDomain.AssemblyResolve += ResolveUnityExtensionAssembly;
            CopyH3VRCodeDlls(true);
            NativeHookManager.BeforeShutdownCallbacks.Add(delegate { CopyH3VRCodeDlls(false); });
            // Fixes edited scripts never reflecting changes without a full Editor restart.
            if (EditorVersion.Current.FunctionOffsets.GetMonoManagerPtr != 0)
            {
                NativeHookManager.BeforeShutdownCallbacks.Add(delegate
                {
                    try { CaptureAndEvictStaleScriptAssemblyImages(); }
                    catch (Exception ex) { Debug.LogWarning("[ManagedPluginDomainFix] CaptureAndEvictStaleScriptAssemblyImages failed: " + ex.Message); }
                });
            }

            // Note: PIOLA and RUA hooks cannot be installed safely from an [InitializeOnLoad]
            // static ctor. ShutdownManaged + BeforeEATI callbacks cover all required work.

            // Suppress RequestScriptReload — CheckConsistency triggers it via
            // FixRuntimeScriptReference after [InitializeOnLoad] returns, causing infinite reload.
            if (NativeHookManager.RequestScriptReloadHookInstalled)
            {
                NativeHookManager.SuppressRequestScriptReload = true;
                RepairH3VRCodeMonoScripts("ctor");

                // Apply the serialization gate patch as early as possible.
                // This is a process-lifetime code section patch — no domain dependencies.
                // Once applied, ALL CSD rebuilds correctly include custom struct fields
                // (e.g. Anvil.AssetID on m_anvilPrefab) regardless of MonoManager state.
                PatchMonoManagerScriptImages();
                ClearH3VRSerializationCaches();

                EditorApplication.delayCall += delegate
                {
                    EnsureH3VRCodeInScriptAssemblies();

                    // CSD may have been lazily rebuilt between ctor and this delayCall.
                    // Clear to force fresh rebuild with gate patch active.
                    ClearH3VRSerializationCaches();

                    // Reimport .object.asset files so the Library cache reflects the
                    // correct TypeTree (with AnvilAsset fields).
                    RepairStaleObjectAssetCache();

                    // Verify MonoScripts are healthy after either Renew or fallback repair.
                    try
                    {
                        if (EditorVersion.IsSupportedVersion)
                        {
                            string vh3vr = MeatKit.AssemblyRename + ".dll", vh3vrFp = MeatKit.AssemblyFirstpassRename + ".dll";
                            int vTotal = 0, vBroken = 0;
                            foreach (MonoScript vs in MonoImporter.GetAllRuntimeMonoScripts())
                            {
                                if ((UnityEngine.Object)vs == null) continue;
                                string vf = Path.GetFileName(AssetDatabase.GetAssetPath(vs));
                                if (vf != vh3vr && vf != vh3vrFp) continue;
                                vTotal++;
                                if (vs.GetClass() == null) vBroken++;
                            }
                            if (vBroken > 0)
                                Debug.LogWarning(string.Format("[ManagedPluginDomainFix] Verify: {0}/{1} scripts STILL null after ctor repair", vBroken, vTotal));
                            else
                                Log("Verify: all " + vTotal + " scripts healthy");
                        }
                    }
                    catch (Exception vex) { Debug.LogWarning("[ManagedPluginDomainFix] Verify: " + vex); }

                    EditorPrefs.DeleteKey(H3VRCodeReimportFlagKey);
                    NativeHookManager.SuppressRequestScriptReload = false;
                    NativeHookManager.DiscardPendingScriptReload();
                };
            }
            else
            {
                Debug.LogWarning("[ManagedPluginDomainFix] RequestScriptReload hook not installed — falling back to delayCall repair");
                EditorApplication.delayCall += delegate 
                { 
                    RepairH3VRCodeMonoScripts("delayCall");
                    PatchMonoManagerScriptImages();
                    ClearH3VRSerializationCaches();
                    EnsureH3VRCodeInScriptAssemblies();
                    RepairStaleObjectAssetCache();
                };
            }
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void d_mono_set_assemblies_path([MarshalAs(UnmanagedType.LPStr)] string path);

        private static void SetMonoAssemblySearchPaths()
        {
            try
            {
                IntPtr monoModule = GetMonoModule();
                if (monoModule == IntPtr.Zero)
                {
                    Debug.LogWarning("[ManagedPluginDomainFix] mono module not found");
                    return;
                }

                IntPtr setPathPtr = GetProcAddress(monoModule, "mono_set_assemblies_path");
                if (setPathPtr == IntPtr.Zero)
                {
                    Debug.LogWarning("[ManagedPluginDomainFix] mono_set_assemblies_path not found");
                    return;
                }

                var setPath = (d_mono_set_assemblies_path)Marshal.GetDelegateForFunctionPointer(
                    setPathPtr, typeof(d_mono_set_assemblies_path));

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var dirList = new List<string>();

                System.Action<string> addDir = d =>
                {
                    if (!string.IsNullOrEmpty(d) && Directory.Exists(d) && seen.Add(d)) dirList.Add(d);
                };

                // Restore Unity's three initial Mono search paths.
                string appContents = EditorApplication.applicationContentsPath;
                addDir(Path.Combine(appContents, "Managed"));
                string monoLib = Path.Combine(Path.Combine(appContents, "Mono"), "lib");
                addDir(Path.Combine(Path.Combine(monoLib, "mono"), "2.0"));
                addDir(Path.Combine(appContents, "UnityScript"));

                // Add UnityExtensions directories so UnityEngine.UI and other extension
                // DLLs are resolvable during ProcessInitializeOnLoadAttributes (Sodalite fix).
                if (Directory.Exists(_unityExtensionsDir))
                    foreach (var dll in Directory.GetFiles(_unityExtensionsDir, "*.dll", SearchOption.AllDirectories))
                        addDir(Path.GetDirectoryName(dll));

                // Add Assets/Managed/ so that plugin DLLs are resolvable through Mono's probe path.
                addDir(_managedDir);

                setPath(string.Join(Path.PathSeparator.ToString(), dirList.ToArray()));
                Log("SetMonoAssemblySearchPaths: " + dirList.Count + " directories registered");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ManagedPluginDomainFix] SetMonoAssemblySearchPaths failed: " + ex);
            }
        }

        private static bool IsManagedAssembly(string path)
        {
            try { AssemblyName.GetAssemblyName(path); return true; }
            catch { return false; }
        }

        private static void PreloadUnityExtensionDlls()
        {
            if (!Directory.Exists(_unityExtensionsDir)) return;
            var allDlls = Directory.GetFiles(_unityExtensionsDir, "*.dll", SearchOption.AllDirectories);
            Array.Sort(allDlls, (a, b) => a.Length.CompareTo(b.Length));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int loaded = 0, skipped = 0;
            foreach (var dll in allDlls)
            {
                if (!seen.Add(Path.GetFileNameWithoutExtension(dll))) continue;
                if (!IsManagedAssembly(dll)) { skipped++; continue; }
                try { InternalEditorUtility.LoadAssemblyWrapper(Path.GetFileName(dll), dll); loaded++; }
                catch { skipped++; }
            }
            Log("PreloadUnityExtensionDlls: loaded=" + loaded + " skipped=" + skipped);
        }

        private static Assembly ResolveUnityExtensionAssembly(object sender, ResolveEventArgs args)
        {
            var simpleName = new AssemblyName(args.Name).Name;
            // Prefer an already-loaded copy. LoadAssemblyWrapper does not dedupe against
            // plugin-loaded assemblies, so a raced bind double-loads the DLL and splits
            // type identity.
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (string.Equals(loaded.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                        return loaded;
                }
                catch { }
            }
            // Search UnityExtensions first (recursive), then Assets/Managed/ (flat).
            string[][] searchPaths = new string[][] {
                Directory.Exists(_unityExtensionsDir) ? Directory.GetFiles(_unityExtensionsDir, simpleName + ".dll", SearchOption.AllDirectories) : new string[0],
                File.Exists(Path.Combine(_managedDir, simpleName + ".dll")) ? new[] { Path.Combine(_managedDir, simpleName + ".dll") } : new string[0]
            };
            foreach (string[] dlls in searchPaths)
            {
                if (dlls.Length == 0) continue;
                Array.Sort(dlls, (a, b) => a.Length.CompareTo(b.Length));
                foreach (var dll in dlls)
                {
                    try
                    {
                        var asm = InternalEditorUtility.LoadAssemblyWrapper(Path.GetFileName(dll), dll);
                        Log("AssemblyResolve resolved " + simpleName + " from " + dll);
                        return asm;
                    }
                    catch { }
                }
            }
            return null;
        }

        // Called each reload via BeforeShutdownCallbacks (inside OnShutdownManaged).
        // Copies H3VRCode DLLs to ScriptAssemblies/. Core helper for shutdown, ctor, and pre-EATI paths.
        private static void CopyH3VRCodeDlls(bool onlyIfMissing)
        {
            if (!Directory.Exists(_managedDir)) return;
            if (!Directory.Exists(_scriptAssembliesDir)) Directory.CreateDirectory(_scriptAssembliesDir);
            foreach (var name in new[] { MeatKit.AssemblyRename, MeatKit.AssemblyFirstpassRename })
            {
                var src  = Path.Combine(_managedDir, name + ".dll");
                var dest = Path.Combine(_scriptAssembliesDir, name + ".dll");
                try
                {
                    if (onlyIfMissing) { if (!File.Exists(dest) && File.Exists(src)) File.Copy(src, dest); }
                    else CopyDllIfChanged(src, dest);
                }
                catch (Exception ex) { Debug.LogWarning("[ManagedPluginDomainFix] DLL copy failed: " + name + ".dll: " + ex.Message); }
            }
        }

        private static bool FileContentsEqual(string a, string b)
        {
            try
            {
                byte[] ba = File.ReadAllBytes(a), bb = File.ReadAllBytes(b);
                if (ba.Length != bb.Length) return false;
                for (int i = 0; i < ba.Length; i++)
                    if (ba[i] != bb[i]) return false;
                return true;
            }
            catch { return false; }
        }

        private static void CopyDllIfChanged(string src, string dest)
        {
            if (!File.Exists(src)) return;
            if (File.Exists(dest) && FileContentsEqual(src, dest)) return;
            File.Copy(src, dest, true);
            File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(src));
        }

        internal static void EnsureH3VRCodeInScriptAssemblies() { CopyH3VRCodeDlls(false); }

        // Sharing-violation recovery: stages locked DLL, disables .meta, copies next reload.
        internal static void StageForPendingImport(string pendingPath, string destPath)
        {
            string metaPath = destPath + ".meta";
            string existingMeta = File.Exists(metaPath) ? File.ReadAllText(metaPath) : "";
            string guid = ExtractGuidFromMeta(existingMeta);

            // Disable the plugin so domain reload doesn't re-lock the file.
            File.WriteAllText(metaPath, BuildPluginMeta(guid, anyEnabled: false));

            // Record the pending operation.
            string pendingDir = Path.GetDirectoryName(pendingPath);
            if (!Directory.Exists(pendingDir)) Directory.CreateDirectory(pendingDir);
            File.AppendAllText(_pendingManifestPath, pendingPath + "|" + destPath + "\n");
        }

        private static void ApplyPendingDllImports()
        {
            if (string.IsNullOrEmpty(_pendingManifestPath) || !File.Exists(_pendingManifestPath))
                return;

            string[] lines;
            try { lines = File.ReadAllLines(_pendingManifestPath); }
            catch { return; }
            // NOTE: do NOT delete manifest here. Only delete after successful copy.

            bool anyApplied = false;
            var remaining = new List<string>();
            var seenEntries = new HashSet<string>();
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (!seenEntries.Add(line)) continue; // skip duplicate manifest entries
                int sep = line.IndexOf('|');
                if (sep < 0) continue;
                string pendingPath = line.Substring(0, sep);
                string destPath    = line.Substring(sep + 1);
                string metaPath    = destPath + ".meta";

                if (!File.Exists(pendingPath))
                {
                    ReEnablePluginMeta(metaPath);
                    continue;
                }
                try
                {
                    File.Copy(pendingPath, destPath, true);
                    File.Delete(pendingPath);

                    ReEnablePluginMeta(metaPath);
                    anyApplied = true;
                }
                catch (IOException)
                {
                    remaining.Add(line);
                    Debug.LogWarning("[ManagedPluginDomainFix] DLL still locked, pending copy deferred to next domain: " +
                        Path.GetFileName(destPath));
                }
                catch (Exception ex)
                {
                    ReEnablePluginMeta(metaPath);
                    Debug.LogWarning("[ManagedPluginDomainFix] Failed to apply pending DLL " +
                        Path.GetFileName(destPath) + ": " + ex.Message);
                }
            }

            // Rewrite manifest with only the entries that couldn't be applied yet.
            if (remaining.Count > 0)
                File.WriteAllLines(_pendingManifestPath, remaining.ToArray());
            else
                File.Delete(_pendingManifestPath);

            if (anyApplied)
                EditorApplication.delayCall += delegate { AssetDatabase.Refresh(); };
        }

        private static void ReEnablePluginMeta(string metaPath)
        {
            if (!File.Exists(metaPath)) return;
            File.WriteAllText(metaPath, BuildPluginMeta(ExtractGuidFromMeta(File.ReadAllText(metaPath)), anyEnabled: true));
        }

        private static string ExtractGuidFromMeta(string metaContent)
        {
            foreach (var raw in metaContent.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("guid: "))
                    return line.Substring(6).Trim();
            }
            return Guid.NewGuid().ToString("N");
        }

        private static string BuildPluginMeta(string guid, bool anyEnabled)
        {
            string e = anyEnabled ? "1" : "0";
            return "fileFormatVersion: 2\nguid: " + guid + "\nPluginImporter:\n  serializedVersion: 2\n  iconMap: {}\n  executionOrder: {}\n" +
                   "  isPreloaded: 0\n  isOverridable: 0\n  platformData:\n    data:\n      first:\n        Any:\n      second:\n        enabled: " + e + "\n        settings: {}\n" +
                   "    data:\n      first:\n        Editor: Editor\n      second:\n        enabled: " + e + "\n        settings:\n          DefaultValueInitialized: true\n" +
                   "    data:\n      first:\n        Windows Store Apps: WindowsStoreApps\n      second:\n        enabled: 0\n        settings:\n          CPU: AnyCPU\n" +
                   "  userData:\n  assetBundleName:\n  assetBundleVariant:";
        }

        private static string FindFirstObjectAssetPath()
        {
            foreach (string p in AssetDatabase.GetAllAssetPaths())
            {
                if (p.EndsWith(".object.asset")) return p;
            }
            return null;
        }

        // Clears CachedSerializationData (MonoScriptCache+136) for all H3VR MonoScripts.
        // The CSD can be lazily rebuilt with an incomplete class hierarchy after domain reload;
        // clearing forces a fresh rebuild when the next SerializedObject is created.
        private static void ClearH3VRSerializationCaches()
        {
            if (_cachedPtrField == null) return;
            try
            {
                string h3vr = MeatKit.AssemblyRename + ".dll";
                string h3vrFp = MeatKit.AssemblyFirstpassRename + ".dll";
                int cleared = 0;
                foreach (MonoScript ms in MonoImporter.GetAllRuntimeMonoScripts())
                {
                    if ((UnityEngine.Object)ms == null) continue;
                    string ap = AssetDatabase.GetAssetPath(ms);
                    if (string.IsNullOrEmpty(ap)) continue;
                    string file = Path.GetFileName(ap);
                    if (file != h3vr && file != h3vrFp) continue;

                    IntPtr nMS = IntPtr.Zero;
                    try { nMS = (IntPtr)_cachedPtrField.GetValue(ms); } catch (Exception) { }
                    if (nMS == IntPtr.Zero) continue;

                    IntPtr msCache = Marshal.ReadIntPtr(new IntPtr(nMS.ToInt64() + MonoScriptCacheOffset));
                    if (msCache == IntPtr.Zero) continue;

                    IntPtr csd = Marshal.ReadIntPtr(new IntPtr(msCache.ToInt64() + CacheSerDataOffset));
                    if (csd != IntPtr.Zero)
                    {
                        Marshal.WriteIntPtr(new IntPtr(msCache.ToInt64() + CacheSerDataOffset), IntPtr.Zero);
                        cleared++;
                    }
                }
                if (cleared > 0)
                    Log("ClearH3VRSerializationCaches: cleared CSD for " + cleared + " MonoScripts");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ManagedPluginDomainFix] ClearH3VRSerializationCaches: " + ex);
            }
        }

        // Evicts and reimports stale .object.asset ScriptableObjects after a post-build domain reload.
        // FVRObject SOs retain stale per-instance caches (SO+160) that ReleaseMonoScriptCaches skips
        // (it only iterates MonoBehaviours). The stale cache omits AnvilAsset parent fields, causing
        // FindProperty("m_anvilPrefab") to return null. UnloadAsset nulls SO+160, forcing
        // SetupScriptingCache to rebuild from MonoScript+216. YAML is authoritative — no SaveAssets().
        private static void RepairStaleObjectAssetCache()
        {
            try
            {
                string[] allPaths = AssetDatabase.GetAllAssetPaths();
                var objectPaths = new List<string>();
                foreach (string p in allPaths)
                {
                    if (p.EndsWith(".object.asset")) objectPaths.Add(p);
                }
                if (objectPaths.Count == 0) return;

                // Quick check: if m_anvilPrefab.AssetName already has a value, skip.
                string testPath = FindFirstObjectAssetPath();
                if (testPath != null)
                {
                    UnityEngine.Object testObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(testPath);
                    if (testObj != null)
                    {
                        SerializedObject testSo = new SerializedObject(testObj);
                        SerializedProperty assetNameProp = testSo.FindProperty("m_anvilPrefab.AssetName");
                        if (assetNameProp != null && !string.IsNullOrEmpty(assetNameProp.stringValue))
                        {
                            Log("Object asset cache OK — m_anvilPrefab present (" + objectPaths.Count + " assets)");
                            return;
                        }
                        // Stale SO detected — unload it before proceeding.
                        Resources.UnloadAsset(testObj);
                    }
                }

                Log(string.Format("Stale Library cache detected — evicting {0} .object.asset SOs and reimporting", objectPaths.Count));

                // Evict stale SOs — UnloadAsset nulls SO+160, letting SetupScriptingCache rebuild fresh.
                int unloaded = 0;
                foreach (string assetPath in objectPaths)
                {
                    try
                    {
                        UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                        if (obj != null)
                        {
                            Resources.UnloadAsset(obj);
                            unloaded++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[ManagedPluginDomainFix] Error unloading " + assetPath + ": " + ex.Message);
                    }
                }
                Log("Evicted " + unloaded + " stale ScriptableObjects");

                // Reimport from disk — re-reads YAML with the now-correct TypeTree and updates Library cache.
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (string assetPath in objectPaths)
                    {
                        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
                Log(string.Format("Reimported {0} .object.asset files from YAML source", objectPaths.Count));

                // Step 3: Verify fix.
                if (testPath != null)
                {
                    UnityEngine.Object verifyObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(testPath);
                    if (verifyObj != null)
                    {
                        SerializedObject verifySo = new SerializedObject(verifyObj);
                        SerializedProperty verifyProp = verifySo.FindProperty("m_anvilPrefab.AssetName");
                        if (verifyProp == null || string.IsNullOrEmpty(verifyProp.stringValue))
                            Debug.LogWarning("[ManagedPluginDomainFix] Object asset cache repair FAILED — m_anvilPrefab still missing after evict+reimport");
                        else
                            Log("Object asset cache repair verified OK — m_anvilPrefab.AssetName=" + verifyProp.stringValue);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ManagedPluginDomainFix] RepairStaleObjectAssetCache: " + ex);
            }
        }

        private static bool _typeTreePrimed = false;
        private static bool _forcedReimportScheduled = false;
        private const string ReimportAttemptsPrefKey = "MPF_ReimportAttempts";
        private const int    MaxReimportAttempts = 2;

        // before BuildAssetBundles. No-op after first call per domain (_typeTreePrimed guard).
        public static void PrimeTypeTreesForBuild()
        {
            if (_typeTreePrimed) return;
            try
            {
                var initMethod = typeof(MonoScript).GetMethod("Init",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var getNsMethod = typeof(MonoScript).GetMethod("GetNamespace",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (initMethod == null || getNsMethod == null) return;
                string h3vr   = MeatKit.AssemblyRename + ".dll";
                string h3vrFp = MeatKit.AssemblyFirstpassRename + ".dll";
                int primed = 0;
                // StartAssetEditing/StopAssetEditing + SaveAssets to persist and clear dirty flags.
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (MonoScript script in MonoImporter.GetAllRuntimeMonoScripts())
                    {
                        if ((UnityEngine.Object)script == null) continue;
                        string assetPath = AssetDatabase.GetAssetPath(script);
                        if (string.IsNullOrEmpty(assetPath)) continue;
                        string file = Path.GetFileName(assetPath);
                        if (file != h3vr && file != h3vrFp) continue;
                        string ns = (string)getNsMethod.Invoke(script, null);
                        initMethod.Invoke(script, new object[] { "", script.name, ns, file, false });
                        primed++;
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
                // Persist the primed TypeTree state and clear dirty flags before shutdown.
                AssetDatabase.SaveAssets();
                Log("PrimeTypeTreesForBuild: primed=" + primed);
                _typeTreePrimed = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ManagedPluginDomainFix] PrimeTypeTreesForBuild failed: " + ex);
            }
        }

        // Builds a HashSet of native MonoScript pointers for all Assets/Managed/ scripts.
        private static HashSet<IntPtr> BuildManagedScriptNativePtrSet()
        {
            var set = new HashSet<IntPtr>();
            if (_cachedPtrField == null) return set;
            foreach (MonoScript ms in MonoImporter.GetAllRuntimeMonoScripts())
            {
                if ((UnityEngine.Object)ms == null) continue;
                string ap = AssetDatabase.GetAssetPath(ms);
                if (string.IsNullOrEmpty(ap) || !ap.StartsWith("Assets/Managed/", StringComparison.OrdinalIgnoreCase)) continue;
                IntPtr nMS = IntPtr.Zero;
                try { nMS = (IntPtr)_cachedPtrField.GetValue(ms); } catch (Exception) { }
                if (nMS != IntPtr.Zero) set.Add(nMS);
            }
            return set;
        }

        // Native MonoScript/MonoScriptCache offsets (Unity 5.6.7f1 x64, verified by IDA).
        private const int MonoScriptCacheOffset   = 216;  // MonoScript::m_ScriptCache
        private const int CacheClassOffset        = 8;    // MonoScriptCache::m_pClass
        private const int CacheSerDataOffset      = 136;  // MonoScriptCache::CachedSerializationData*

        // MonoManager layout (Unity 5.6.7f1 x64, IDA-verified):
        //   m_pScriptImages: +520 begin, +528 end | GetAssemblyIndexFromImage RVA: 0x14C25D0

        // Patches the serialization gate (RVA 0xE3F670) that uses GetAssemblyIndexFromImage
        // to check whether a MonoImage is in MonoManager::m_pScriptImages.
        // H3VRCode isn't in the bitset at reload time (loaded later by PluginManager), so the
        // gate returns false and drops struct fields like m_anvilPrefab from the TypeTree.
        // Patch (RVA 0xE3F6D5): 83 F8 FF 0F 95 C0 → B0 01 90 90 90 90 (always return 1).
        private const long RVA_GatePatchSite = 0xE3F6D5;
        private static readonly byte[] GateOrigBytes = new byte[] { 0x83, 0xF8, 0xFF, 0x0F, 0x95, 0xC0 };
        private static readonly byte[] GatePatchBytes = new byte[] { 0xB0, 0x01, 0x90, 0x90, 0x90, 0x90 };
        private static bool _gatePatchApplied = false;

        private static void PatchMonoManagerScriptImages()
        {
            if (_gatePatchApplied)
            {
                Log("PatchMonoManagerScriptImages: gate patch already applied");
                return;
            }
            try
            {
                IntPtr unityMod = GetUnityModule();
                if (unityMod == IntPtr.Zero)
                {
                    Log("PatchMonoManagerScriptImages: Unity module handle not available");
                    return;
                }

                IntPtr patchAddr = new IntPtr(unityMod.ToInt64() + RVA_GatePatchSite);

                // Verify the original bytes are what we expect
                byte[] current = new byte[6];
                Marshal.Copy(patchAddr, current, 0, 6);
                bool bytesMatch = true;
                for (int i = 0; i < 6; i++)
                {
                    if (current[i] != GateOrigBytes[i]) { bytesMatch = false; break; }
                }

                if (!bytesMatch)
                {
                    // Check if already patched
                    bool alreadyPatched = true;
                    for (int i = 0; i < 6; i++)
                    {
                        if (current[i] != GatePatchBytes[i]) { alreadyPatched = false; break; }
                    }
                    if (alreadyPatched)
                    {
                        _gatePatchApplied = true;
                        Log("PatchMonoManagerScriptImages: gate already patched (from previous domain)");
                        return;
                    }
                    var hexSb = new System.Text.StringBuilder();
                    for (int i = 0; i < current.Length; i++) hexSb.AppendFormat("{0:X2} ", current[i]);
                    Log("PatchMonoManagerScriptImages: unexpected bytes at gate patch site: " + hexSb);
                    return;
                }

                // Make the page writable
                uint oldProtect;
                if (!VirtualProtect(patchAddr, (UIntPtr)6, 0x40 /* PAGE_EXECUTE_READWRITE */, out oldProtect))
                {
                    Log("PatchMonoManagerScriptImages: VirtualProtect failed");
                    return;
                }

                // Apply the patch
                Marshal.Copy(GatePatchBytes, 0, patchAddr, 6);

                // Restore page protection
                uint ignored;
                VirtualProtect(patchAddr, (UIntPtr)6, oldProtect, out ignored);

                // Flush instruction cache
                FlushInstructionCache(GetCurrentProcess(), patchAddr, (UIntPtr)6);

                _gatePatchApplied = true;
                Log(string.Format("PatchMonoManagerScriptImages: gate function patched at 0x{0:X} (always return true for non-mscorlib serializable types)", patchAddr.ToInt64()));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ManagedPluginDomainFix] PatchMonoManagerScriptImages: " + ex);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll")]
        private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        // Clears CachedSerializationData for all MBs/MonoScripts before EATI, forcing
        // TypeTree rebuild from live pClass (fixes Build 2 stale-metadata regression).
        internal static void ReprimeMBCachesBeforeEATI()
        {
            try
            {
                if (_cachedPtrField == null) return;
                FieldInfo cachedPtrField = _cachedPtrField;
                // Step 1: Build lookup of H3VRCode MonoScript native pointers for MB+160 patching.
                var msLookup = BuildManagedScriptNativePtrSet();

                // Step 2: Clear cache+136 for ALL MBs (forces rebuild from live pClass).
                int fixed0 = 0, cleared = 0, alreadyOk = 0;

                UnityEngine.Object[] allMBs = null;
                try { allMBs = Resources.FindObjectsOfTypeAll(typeof(MonoBehaviour)); }
                catch (Exception ex) { BuildLog.WriteLine("ReprimeMBCachesBeforeEATI: FindObjectsOfTypeAll failed: " + ex.Message); return; }

                foreach (UnityEngine.Object obj in allMBs)
                {
                    MonoBehaviour mb = obj as MonoBehaviour;
                    if (mb == null) continue;

                    IntPtr nMB = IntPtr.Zero;
                    try { nMB = (IntPtr)cachedPtrField.GetValue(obj); } catch (Exception) { }
                    if (nMB == IntPtr.Zero) continue;

                    try
                    {
                        // (a) For H3VRCode MBs: also patch MB+160 to match MonoScript+216
                        MonoScript ms2 = MonoScript.FromMonoBehaviour(mb);
                        if ((UnityEngine.Object)ms2 != null)
                        {
                            IntPtr nMS2 = IntPtr.Zero;
                            try { nMS2 = (IntPtr)cachedPtrField.GetValue(ms2); } catch (Exception) { }
                            if (nMS2 != IntPtr.Zero && msLookup.Contains(nMS2))
                            {
                                IntPtr correctCache = Marshal.ReadIntPtr(new IntPtr(nMS2.ToInt64() + MonoScriptCacheOffset));
                                if (correctCache != IntPtr.Zero)
                                {
                                    IntPtr pClass = Marshal.ReadIntPtr(new IntPtr(correctCache.ToInt64() + CacheClassOffset));
                                    if (pClass != IntPtr.Zero)
                                    {
                                        IntPtr mbCache = Marshal.ReadIntPtr(new IntPtr(nMB.ToInt64() + 160));
                                        if (mbCache != correctCache)
                                        {
                                            Marshal.WriteIntPtr(new IntPtr(nMB.ToInt64() + 160), correctCache);
                                            fixed0++;
                                        }
                                        else { alreadyOk++; }
                                    }
                                }
                            }
                        }

                        // (b) Clear cache+136 for ALL MBs (forces rebuild from correct pClass).
                        IntPtr mbCacheToCheck = Marshal.ReadIntPtr(new IntPtr(nMB.ToInt64() + 160));
                        if (mbCacheToCheck != IntPtr.Zero)
                        {
                            IntPtr serData = Marshal.ReadIntPtr(new IntPtr(mbCacheToCheck.ToInt64() + CacheSerDataOffset));
                            if (serData != IntPtr.Zero)
                            {
                                Marshal.WriteIntPtr(new IntPtr(mbCacheToCheck.ToInt64() + CacheSerDataOffset), IntPtr.Zero);
                                cleared++;
                            }
                        }
                    }
                    catch (Exception) { }
                }
                BuildLog.WriteLine(string.Format(
                    "ReprimeMBCachesBeforeEATI: scanned {0} MBs, patched MB+160={1}, alreadyOk={2}, clearedSerData(MB)={3}",
                    allMBs.Length, fixed0, alreadyOk, cleared));

                // Step 3: Clear MonoScript+216+136 directly (may differ from MB+160 cache).
                int msCleared = 0;
                foreach (MonoScript ms3 in MonoImporter.GetAllRuntimeMonoScripts())
                {
                    if ((UnityEngine.Object)ms3 == null) continue;
                    IntPtr nMS3 = IntPtr.Zero;
                    try { nMS3 = (IntPtr)cachedPtrField.GetValue(ms3); } catch (Exception) { }
                    if (nMS3 == IntPtr.Zero) continue;
                    try
                    {
                        IntPtr msCache = Marshal.ReadIntPtr(new IntPtr(nMS3.ToInt64() + MonoScriptCacheOffset));
                        if (msCache != IntPtr.Zero)
                        {
                            IntPtr serData = Marshal.ReadIntPtr(new IntPtr(msCache.ToInt64() + CacheSerDataOffset));
                            if (serData != IntPtr.Zero)
                            {
                                Marshal.WriteIntPtr(new IntPtr(msCache.ToInt64() + CacheSerDataOffset), IntPtr.Zero);
                                msCleared++;
                            }
                        }
                    }
                    catch (Exception) { }
                }
                BuildLog.WriteLine("ReprimeMBCachesBeforeEATI: cleared MonoScript+216+136 for " + msCleared + " scripts");
            }
            catch (Exception ex)
            {
                BuildLog.WriteLine("ReprimeMBCachesBeforeEATI failed: " + ex.Message);
            }
        }

        // Re-primes MonoScript caches after EATI by calling Init() for all Assets/Managed/ scripts.
        // Skips StartAssetEditing/SaveAssets to avoid import cycles mid-build.
        internal static void ReprimeSilentAfterEATI()
        {
            try
            {
                var initMethod = typeof(MonoScript).GetMethod("Init",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var getNsMethod = typeof(MonoScript).GetMethod("GetNamespace",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (initMethod == null || getNsMethod == null) return;

                // Used to read native MonoScript pointer for cache-patching.
                FieldInfo cachedPtrField = _cachedPtrField;
                // Ensure correct H3VRCode DLLs in ScriptAssemblies/ before Init() (standalone
                // compile may have replaced them with stubs lacking Anvil namespace).
                EnsureH3VRCodeInScriptAssemblies();
                int primed = 0;
                foreach (MonoScript script in MonoImporter.GetAllRuntimeMonoScripts())
                {
                    if ((UnityEngine.Object)script == null) continue;
                    string assetPath = AssetDatabase.GetAssetPath(script);
                    if (string.IsNullOrEmpty(assetPath)) continue;
                    if (!assetPath.StartsWith("Assets/Managed/", StringComparison.OrdinalIgnoreCase)) continue;
                    string file = Path.GetFileName(assetPath);
                    string ns   = (string)getNsMethod.Invoke(script, null);

                    initMethod.Invoke(script, new object[] { "", script.name, ns, file, false });
                    // Clear CachedSerializationData after Init() to discard stub TypeTree.
                    if (cachedPtrField != null)
                    {
                        IntPtr nMS = IntPtr.Zero;
                        try { nMS = (IntPtr)cachedPtrField.GetValue(script); } catch (Exception) { }
                        if (nMS != IntPtr.Zero)
                        {
                            try
                            {
                                IntPtr cache = Marshal.ReadIntPtr(new IntPtr(nMS.ToInt64() + MonoScriptCacheOffset));
                                if (cache != IntPtr.Zero)
                                {
                                    IntPtr oldSerData = Marshal.ReadIntPtr(new IntPtr(cache.ToInt64() + CacheSerDataOffset));
                                    if (oldSerData != IntPtr.Zero)
                                    {
                                        Marshal.WriteIntPtr(new IntPtr(cache.ToInt64() + CacheSerDataOffset), IntPtr.Zero);
                                    }
                                }
                            }
                            catch (Exception) { }
                        }
                    }
                    primed++;
                }
                BuildLog.WriteLine("ReprimeSilentAfterEATI: primed " + primed + " plugin-DLL scripts");

                // Part 2: patch MB+160 for managed-script MBs (safe: MonoBehaviour has no Anvil fields).
                if (cachedPtrField != null)
                {
                    var msLookup = BuildManagedScriptNativePtrSet();

                    int mbPart2Fixed = 0;
                    UnityEngine.Object[] allMBs = null;
                    try { allMBs = Resources.FindObjectsOfTypeAll(typeof(MonoBehaviour)); }
                    catch (Exception ex2) { BuildLog.WriteLine("ReprimeSilentAfterEATI Part2: FindObjectsOfTypeAll(MB) failed: " + ex2.Message); }

                    if (allMBs != null)
                    {
                        foreach (UnityEngine.Object obj2 in allMBs)
                        {
                            MonoBehaviour mb2 = obj2 as MonoBehaviour;
                            if (mb2 == null) continue;
                            MonoScript ms3 = MonoScript.FromMonoBehaviour(mb2);
                            if ((UnityEngine.Object)ms3 == null) continue;
                            IntPtr nMS3 = IntPtr.Zero;
                            try { nMS3 = (IntPtr)cachedPtrField.GetValue(ms3); } catch (Exception) { }
                            if (nMS3 == IntPtr.Zero || !msLookup.Contains(nMS3)) continue;
                            IntPtr nMB2 = IntPtr.Zero;
                            try { nMB2 = (IntPtr)cachedPtrField.GetValue(obj2); } catch (Exception) { }
                            if (nMB2 == IntPtr.Zero) continue;
                            try
                            {
                                IntPtr correctCache2 = Marshal.ReadIntPtr(new IntPtr(nMS3.ToInt64() + MonoScriptCacheOffset));
                                if (correctCache2 == IntPtr.Zero) continue;
                                IntPtr correctClass2 = Marshal.ReadIntPtr(new IntPtr(correctCache2.ToInt64() + CacheClassOffset));
                                if (correctClass2 == IntPtr.Zero) continue;
                                IntPtr mbCachePtr = Marshal.ReadIntPtr(new IntPtr(nMB2.ToInt64() + 160));
                                if (mbCachePtr == correctCache2) continue;
                                Marshal.WriteIntPtr(new IntPtr(nMB2.ToInt64() + 160), correctCache2);
                                mbPart2Fixed++;
                            }
                            catch (Exception) { }
                        }
                        BuildLog.WriteLine("ReprimeSilentAfterEATI Part2: scanned " + allMBs.Length + " MBs, fixed=" + mbPart2Fixed);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ManagedPluginDomainFix] ReprimeSilentAfterEATI failed: " + ex);
            }
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void d_MonoScriptRebuildFromAwake(IntPtr thisPtr);
        // RVA of MonoScript::RebuildFromAwake in Unity.exe 5.6.7f1 x64.
        private const long RebuildFromAwakeRva = 0xe369d0L;
        // caller: "ctor" (direct ctor call) or "delayCall" (backup, fires after PIOLA).
        private static void RepairH3VRCodeMonoScripts(string caller)
        {
            try
            {
                if (!EditorVersion.IsSupportedVersion) return;
                string h3vr   = MeatKit.AssemblyRename + ".dll";
                string h3vrFp = MeatKit.AssemblyFirstpassRename + ".dll";
                // First pass: count nulls.
                int total = 0, broken = 0;
                foreach (MonoScript s in MonoImporter.GetAllRuntimeMonoScripts())
                {
                    if ((UnityEngine.Object)s == null) continue;
                    string f = Path.GetFileName(AssetDatabase.GetAssetPath(s));
                    if (f != h3vr && f != h3vrFp) continue;
                    total++;
                    if (s.GetClass() == null) broken++;
                }

                if (broken == 0)
                {
                    EditorPrefs.DeleteKey(ReimportAttemptsPrefKey);  // reset for future fresh runs
                    Log("RepairH3VRCodeMonoScripts(" + caller + "): all " + total + " scripts healthy");
                    return;
                }

                Debug.LogWarning(string.Format(
                    "[ManagedPluginDomainFix] RepairH3VRCodeMonoScripts({2}): {0}/{1} H3VRCode scripts null — rebuilding ALL {1} scripts (stale m_pClass fix)",
                    broken, total, caller));

                IntPtr unityBase = GetModuleHandle("Unity.exe");
                if (unityBase == IntPtr.Zero) unityBase = GetUnityModule();
                if (unityBase == IntPtr.Zero)
                {
                    Debug.LogWarning("[ManagedPluginDomainFix] RepairH3VRCodeMonoScripts: Unity.exe module not found");
                    return;
                }

                IntPtr fnPtr = new IntPtr(unityBase.ToInt64() + RebuildFromAwakeRva);
                var rebuildFromAwake = (d_MonoScriptRebuildFromAwake)Marshal.GetDelegateForFunctionPointer(
                    fnPtr, typeof(d_MonoScriptRebuildFromAwake));

                if (_cachedPtrField == null) return;
                FieldInfo cachedPtrField = _cachedPtrField;
                // Rebuild ALL scripts (not just null). After domain reload, prior m_pClass ptrs
                // become dangling — RebuildFromAwake updates to current domain's MonoClass.
                int repaired = 0, skipped = 0;
                foreach (MonoScript s in MonoImporter.GetAllRuntimeMonoScripts())
                {
                    if ((UnityEngine.Object)s == null) continue;
                    string f = Path.GetFileName(AssetDatabase.GetAssetPath(s));
                    if (f != h3vr && f != h3vrFp) continue;

                    IntPtr nativePtr = (IntPtr)cachedPtrField.GetValue(s);
                    if (nativePtr == IntPtr.Zero) { skipped++; continue; }
                    rebuildFromAwake(nativePtr);
                    repaired++;
                }
                // Verification pass.
                int stillBroken = 0;
                foreach (MonoScript s in MonoImporter.GetAllRuntimeMonoScripts())
                {
                    if ((UnityEngine.Object)s == null) continue;
                    string f = Path.GetFileName(AssetDatabase.GetAssetPath(s));
                    if (f != h3vr && f != h3vrFp) continue;
                    if (s.GetClass() == null) stillBroken++;
                }

                Log(string.Format(
                    "RepairH3VRCodeMonoScripts({3}): repaired={0} skipped={1} stillBroken={2}",
                    repaired, skipped, stillBroken, caller));

                // Discard repair-triggered RequestScriptReload.
                if (stillBroken == 0)
                {
                    NativeHookManager.DiscardPendingScriptReload();
                    return;
                }

                // Schedule force-reimport if still broken (max once per domain).
                // Discard repair-triggered RequestScriptReload.
                NativeHookManager.DiscardPendingScriptReload();
                int _reimportAttempts = EditorPrefs.GetInt(ReimportAttemptsPrefKey, 0);
                if (stillBroken > 0 && !_forcedReimportScheduled && _reimportAttempts < MaxReimportAttempts)
                {
                    EditorPrefs.SetInt(ReimportAttemptsPrefKey, _reimportAttempts + 1);
                    _forcedReimportScheduled = true;
                    string mainPath = "Assets/Managed/" + h3vr;
                    string fpPath   = "Assets/Managed/" + h3vrFp;
                    Debug.LogWarning(string.Format(
                        "[ManagedPluginDomainFix] RepairH3VRCodeMonoScripts({0}): {1} scripts still null — scheduling force reimport to restore inspector",
                        caller, stillBroken));
                    EditorApplication.delayCall += delegate
                    {
                        try
                        {
                            AssetDatabase.ImportAsset(mainPath, ImportAssetOptions.ForceUpdate);
                            AssetDatabase.ImportAsset(fpPath,   ImportAssetOptions.ForceUpdate);
                        }
                        catch (Exception reimportEx)
                        {
                            Debug.LogWarning("[ManagedPluginDomainFix] Force reimport failed: " + reimportEx.Message);
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ManagedPluginDomainFix] RepairH3VRCodeMonoScripts(" + caller + "): " + ex);
            }
        }

        // Cached delegate for hot-path calls from OnMonoScriptTransferWrite.
        private static d_MonoScriptRebuildFromAwake _rebuildFromAwakeDelegate;

        // Calls RebuildFromAwake + clears CachedSerializationData on a native MonoScript pointer
        // immediately before bundle serialization to ensure the TypeTree uses the live H3VRCode pClass.
        internal static void ReprimeSingleNativeScript(IntPtr nativePtr)
        {
            if (nativePtr == IntPtr.Zero) return;
            try
            {
                if (_rebuildFromAwakeDelegate == null)
                {
                    IntPtr unityBase = GetModuleHandle("Unity.exe");
                    if (unityBase == IntPtr.Zero) unityBase = GetUnityModule();
                    if (unityBase == IntPtr.Zero) return;
                    IntPtr fnPtr = new IntPtr(unityBase.ToInt64() + RebuildFromAwakeRva);
                    _rebuildFromAwakeDelegate = (d_MonoScriptRebuildFromAwake)
                        Marshal.GetDelegateForFunctionPointer(fnPtr, typeof(d_MonoScriptRebuildFromAwake));
                }
                _rebuildFromAwakeDelegate(nativePtr);
                // RebuildFromAwake queues a RequestScriptReload as a side effect; discard it to
                // prevent accumulating pending reloads that would fire after BuildAssetBundles.
                NativeHookManager.DiscardPendingScriptReload();
                IntPtr freshCache = Marshal.ReadIntPtr(new IntPtr(nativePtr.ToInt64() + MonoScriptCacheOffset));
                if (freshCache != IntPtr.Zero)
                {
                    Marshal.WriteIntPtr(new IntPtr(freshCache.ToInt64() + CacheSerDataOffset), IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                BuildLog.WriteLine("ReprimeSingleNativeScript: " + ex.Message);
            }
        }

        // GetMonoManagerPtr() -- trivial no-arg accessor, called directly (never detoured).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr d_GetMonoManagerPtr();

        private static d_GetMonoManagerPtr _getMonoManagerPtr;
        private static bool _getMonoManagerPtrResolveFailed;

        private static IntPtr ResolveMonoManagerPtr()
        {
            if (_getMonoManagerPtr == null)
            {
                if (_getMonoManagerPtrResolveFailed) return IntPtr.Zero;
                long rva = EditorVersion.Current.FunctionOffsets.GetMonoManagerPtr;
                if (rva == 0) { _getMonoManagerPtrResolveFailed = true; return IntPtr.Zero; }
                IntPtr unityBase = GetUnityModule();
                if (unityBase == IntPtr.Zero) return IntPtr.Zero;
                IntPtr fnPtr = (IntPtr)(unityBase.ToInt64() + rva);
                _getMonoManagerPtr = (d_GetMonoManagerPtr)Marshal.GetDelegateForFunctionPointer(fnPtr, typeof(d_GetMonoManagerPtr));
            }
            return _getMonoManagerPtr();
        }

        // MonoImage* vector on MonoManager: begin ptr at +520, end ptr at +528, 8 bytes/element.
        // Indices 0-2 are core assemblies; ordinary script assemblies start at index 3.
        private const int MonoImagesVectorBeginOffset = 520;
        private const int MonoImagesVectorEndOffset = 528;
        private const int FirstOrdinaryScriptAssemblyIndex = 3;

        // mono.dll's internal (non-exported) name-cache hash table lookup/remove, resolved by RVA.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr d_g_hash_table_lookup(IntPtr hashTable, IntPtr key);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int d_g_hash_table_remove(IntPtr hashTable, IntPtr key);

        private static d_g_hash_table_lookup _monoImagesHashLookup;
        private static d_g_hash_table_remove _monoImagesHashRemove;

        // MonoImage struct field offsets used as register_image's cache keys, plus the two
        // name-cache hash tables and the mutex mono.dll itself locks around them.
        private const int MonoImageFlagsOffset = 28;
        private const int MonoImageNameOffset = 32;
        private const int MonoImageAssemblyNameOffset = 40;
        private const long LoadedImagesHashGlobalRva = 0x265750;
        private const long LoadedImagesRefonlyHashGlobalRva = 0x265780;
        private const long HashLookupFunctionRva = 0x2278;
        private const long HashRemoveFunctionRva = 0x24DC;
        private const long ImagesLockInitedFlagRva = 0x265788;
        private const long ImagesMutexRva = 0x265758;

        [DllImport("kernel32.dll")]
        private static extern void EnterCriticalSection(IntPtr lpCriticalSection);

        [DllImport("kernel32.dll")]
        private static extern void LeaveCriticalSection(IntPtr lpCriticalSection);

        // Used to confirm a native pointer is actually mapped before dereferencing it.
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr VirtualQuery(IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, UIntPtr dwLength);

        private const uint MEM_COMMIT = 0x1000;
        private const uint PAGE_NOACCESS = 0x01;
        private const uint PAGE_GUARD = 0x100;

        private static bool IsReadablePointer(IntPtr addr)
        {
            if (addr == IntPtr.Zero) return false;
            MEMORY_BASIC_INFORMATION mbi;
            IntPtr queryResult = VirtualQuery(addr, out mbi, (UIntPtr)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION)));
            if (queryResult == IntPtr.Zero) return false;
            if (mbi.State != MEM_COMMIT) return false;
            if ((mbi.Protect & PAGE_NOACCESS) != 0) return false;
            if ((mbi.Protect & PAGE_GUARD) != 0) return false;
            return true;
        }

        private static bool EnsureMonoInternalHashDelegatesLoaded(out IntPtr monoModule)
        {
            monoModule = GetMonoModule();
            if (monoModule == IntPtr.Zero) return false;
            if (_monoImagesHashLookup != null && _monoImagesHashRemove != null) return true;

            IntPtr lookupPtr = (IntPtr)(monoModule.ToInt64() + HashLookupFunctionRva);
            IntPtr removePtr = (IntPtr)(monoModule.ToInt64() + HashRemoveFunctionRva);
            _monoImagesHashLookup = (d_g_hash_table_lookup)Marshal.GetDelegateForFunctionPointer(lookupPtr, typeof(d_g_hash_table_lookup));
            _monoImagesHashRemove = (d_g_hash_table_remove)Marshal.GetDelegateForFunctionPointer(removePtr, typeof(d_g_hash_table_remove));
            return true;
        }

        // Removes a stale MonoImage's entries from register_image's name-cache so the next load
        // creates a fresh image instead of reusing this one. Never closes/frees the image itself --
        // just unlinks the cache entry, which is enough to fix the staleness and avoids the crashes
        // that came from actually closing a still-referenced image.
        private static void EvictStaleImageFromNameCache(IntPtr staleImage)
        {
            IntPtr monoModule;
            if (!EnsureMonoInternalHashDelegatesLoaded(out monoModule)) return;

            if (!IsReadablePointer(staleImage))
            {
                Debug.LogWarning("[ManagedPluginDomainFix] EvictStaleImageFromNameCache: rejected unreadable staleImage=0x" + staleImage.ToInt64().ToString("X"));
                return;
            }

            byte flags = Marshal.ReadByte(staleImage, MonoImageFlagsOffset);
            IntPtr nameKey = Marshal.ReadIntPtr(staleImage, MonoImageNameOffset);
            IntPtr assemblyNameKey = Marshal.ReadIntPtr(staleImage, MonoImageAssemblyNameOffset);

            if (!IsReadablePointer(nameKey)) nameKey = IntPtr.Zero;
            if (!IsReadablePointer(assemblyNameKey)) assemblyNameKey = IntPtr.Zero;

            long hashGlobalRva = (flags & 0x8) != 0 ? LoadedImagesRefonlyHashGlobalRva : LoadedImagesHashGlobalRva;
            IntPtr hashTable = Marshal.ReadIntPtr((IntPtr)(monoModule.ToInt64() + hashGlobalRva));
            if (hashTable == IntPtr.Zero) return;

            IntPtr mutexAddr = (IntPtr)(monoModule.ToInt64() + ImagesMutexRva);
            bool lockInited = Marshal.ReadInt32((IntPtr)(monoModule.ToInt64() + ImagesLockInitedFlagRva)) != 0;
            if (lockInited) EnterCriticalSection(mutexAddr);
            try
            {
                if (nameKey != IntPtr.Zero && _monoImagesHashLookup(hashTable, nameKey) == staleImage)
                    _monoImagesHashRemove(hashTable, nameKey);
                if (assemblyNameKey != IntPtr.Zero && _monoImagesHashLookup(hashTable, assemblyNameKey) == staleImage)
                    _monoImagesHashRemove(hashTable, assemblyNameKey);
            }
            finally
            {
                if (lockInited) LeaveCriticalSection(mutexAddr);
            }
        }

        // Called each reload via BeforeShutdownCallbacks, before this reload's own LoadAssemblies
        // runs. Reads every ordinary script-assembly slot off MonoManager and evicts it from the
        // name-cache immediately, so edited scripts actually get reloaded instead of Mono silently
        // reusing the previous compile forever (the root cause of scripts never reflecting changes
        // without a full Editor restart).
        private static void CaptureAndEvictStaleScriptAssemblyImages()
        {
            IntPtr pMonoManager = ResolveMonoManagerPtr();
            if (pMonoManager == IntPtr.Zero) return;

            IntPtr begin = Marshal.ReadIntPtr(pMonoManager, MonoImagesVectorBeginOffset);
            IntPtr end = Marshal.ReadIntPtr(pMonoManager, MonoImagesVectorEndOffset);
            if (begin == IntPtr.Zero || end == IntPtr.Zero || end.ToInt64() <= begin.ToInt64()) return;

            long elementCount = (end.ToInt64() - begin.ToInt64()) / 8;
            int evicted = 0;
            for (long i = FirstOrdinaryScriptAssemblyIndex; i < elementCount; i++)
            {
                IntPtr slotAddr = new IntPtr(begin.ToInt64() + i * 8);
                IntPtr image = Marshal.ReadIntPtr(slotAddr);
                if (image == IntPtr.Zero) continue;
                // The last one or two entries are sometimes an unmapped pointer at this exact
                // moment -- skip rather than risk dereferencing it.
                if (!IsReadablePointer(image)) continue;
                try
                {
                    EvictStaleImageFromNameCache(image);
                    evicted++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ManagedPluginDomainFix] EvictStaleImageFromNameCache failed for 0x" + image.ToInt64().ToString("X") + ": " + ex.Message);
                }
            }
            if (evicted > 0)
                Log("CaptureAndEvictStaleScriptAssemblyImages: evicted " + evicted + " image(s) from the name-cache");
        }

        private static void Log(string msg)
        {
#pragma warning disable 0162
            if (DebugLogging) Debug.Log("[ManagedPluginDomainFix] " + msg);
#pragma warning restore 0162
        }

    }
}
