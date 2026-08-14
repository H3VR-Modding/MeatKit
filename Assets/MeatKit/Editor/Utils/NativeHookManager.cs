using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    /// <summary>
    /// This class helps manage detours into the native code of the Editor.
    /// Any detours into native code should be registered using this as it will automatically dispose of them
    /// right before the Editor reloads the mono domain, preventing editor crashes.
    /// </summary>
    [InitializeOnLoad]
    public static class NativeHookManager
    {
        // Actual name: ShutdownPlatformSupportModulesInManaged(void)
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ShutdownManaged();

        private static readonly ShutdownManaged OrigShutdownManaged;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte ExtractAssemblyTypeInfoAllDelegate(int policyIndex, uint assemblyId, byte buildTarget, long typeInfoCollector);

        private static ExtractAssemblyTypeInfoAllDelegate _origEATI;

        /// <summary>True when the ExtractAssemblyTypeInfoAll native hook was successfully installed.</summary>
        internal static bool EATIHookInstalled { get { return _origEATI != null; } }

        // Fired before/after EATI runs; safe to modify from main thread
        internal static readonly List<Action> BeforeEATICallbacks = new List<Action>();
        internal static readonly List<Action> AfterEATICallbacks = new List<Action>();

        // Gates EATI per-assembly; return false to skip TypeTree extraction for that DLL.
        // Hooked to block H3VRCode re-extraction during builds (when InsideEATI is true).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte AssemblyHasValidTypeInfoDelegate(long a1);

        private static AssemblyHasValidTypeInfoDelegate _origAHVTI;

        /// <summary>True when the AssemblyHasValidTypeInfo native hook was successfully installed.</summary>
        internal static bool AHVTIHookInstalled { get { return _origAHVTI != null; } }

        // Post-bundle standalone script compile step; made a no-op during MeatKit builds to prevent
        // H3VRCode compile failure (it's absent from the freshly-cleared ScriptAssemblies).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte BuildPlayerExtractAndValidateDelegate(
            long a1, IntPtr a2, uint platformGroup, uint platform, IntPtr a5, long a6, long a7);

        private static BuildPlayerExtractAndValidateDelegate _origBPEVST;

        /// <summary>True when the BuildPlayerExtractAndValidateScriptTypes native hook was successfully installed.</summary>
        internal static bool BPEVSTHookInstalled { get { return _origBPEVST != null; } }

        // TransferScriptingObject<GenerateTypeTreeTransfer>: clears the cached SerData (a4+136)
        // before each call during bundle builds so the TypeTree is always regenerated fresh from
        // the live pClass (a4+8) rather than a stale cached TypeTree loaded from Library/metadata.
        // This fixes the Build 2 anvilPrefab regression where the editor-domain startup EATI
        // populates cache+136 from stale metadata, and that stale cache is read by the bundle
        // type-table writer before ReprimeSilentAfterEATI's clearing takes effect.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void d_TransferScriptingObjectGTT(long a1, long a2, long a3, long a4);

        private static d_TransferScriptingObjectGTT _origTSOGTT;

        /// <summary>True when the TransferScriptingObject&lt;GenerateTypeTreeTransfer&gt; hook is installed.</summary>
        internal static bool TSOGTTHookInstalled { get { return _origTSOGTT != null; } }

        // Schedules a domain reload. Suppressed during MonoScript repair to prevent an infinite
        // reload loop (CheckConsistency → FixRuntimeScriptReference → reload → repeat).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void d_RequestScriptReload(IntPtr thisApp);

        private static d_RequestScriptReload _origRequestScriptReload;

        // PluginImporter::IsCompatibleWithEditorCPUAndOS — hooked to force-return true for
        // Assets/Managed/ DLLs whose .meta defaults to enabled:0, making them visible to the
        // script compiler, component menus, and MonoScript::BelongsToEditorCompatibleAssembly.
        // Signature: bool IsCompatibleWithEditorCPUAndOS(this, const string& buildTargetGroup, const string& buildTarget)
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte d_IsCompatibleWithEditorCPUAndOS(IntPtr thisPluginImporter, IntPtr buildTargetGroupName, IntPtr buildTargetName);

        private static d_IsCompatibleWithEditorCPUAndOS _origIsCompatibleWithEditorCPUAndOS;

        /// <summary>True when the IsCompatibleWithEditorCPUAndOS hook was successfully installed.</summary>
        internal static bool IsCompatibleHookInstalled { get { return _origIsCompatibleWithEditorCPUAndOS != null; } }

        /// <summary>
        /// When true, the Application_RequestScriptReload hook is a no-op.
        /// Set by ManagedPluginDomainFix before ctor-time H3VRCode repair; cleared in delayCall.
        /// </summary>
        internal static volatile bool SuppressRequestScriptReload = false;

        /// <summary>True when the Application_RequestScriptReload suppression hook was successfully installed.</summary>
        internal static bool RequestScriptReloadHookInstalled { get { return _origRequestScriptReload != null; } }

        // Set true by Build.cs _beforeEATI callback during active MeatKit builds.
        // The AHVTI hook only skips H3VRCode while this is true; cleared by _afterEATI.
        internal static volatile bool InsideEATI = false;

        // True exclusively during a MeatKit bundle build (set by Build.cs _beforeEATI, cleared after
        // BuildAssetBundles). When set, AHVTI blocks ALL Assets/Managed/ DLLs; otherwise (standalone/
        // startup EATI) only H3VRCode is blocked so other plugin DLLs remain available to the compiler.
        internal static volatile bool InsideBundleEATI = false;

        // When true, the AHVTI hook allows ALL assemblies through (no blocking).
        // Set by ManagedPluginDomainFix during a targeted DLL reimport to let EATI
        // regenerate the correct TypeTree for H3VRCode without a domain reload.
        internal static volatile bool BypassAHVTIBlock = false;

        // DLL basenames (without extension) from Assets/Managed/ that AHVTI should protect from
        // EATI during bundle builds (InsideBundleEATI=true).  Populated once at static-ctor time
        // so the check is a simple HashSet lookup with no file I/O on each AHVTI call.
        private static readonly HashSet<string> _managedPluginDllNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Native pointers (m_CachedPtr) of PluginImporter instances for DLLs in Assets/Managed/.
        // The IsCompatibleWithEditorCPUAndOS hook uses this set to force-compatible only our DLLs,
        // leaving Unity Extension DLLs (e.g. Standalone/UnityEngine.UI.dll) to the original check.
        private static readonly HashSet<IntPtr> _managedPluginNativePtrs = new HashSet<IntPtr>();

        // Keep track of all the applied detours so we can quickly undo them before the mono domain is reloaded
        private static readonly List<NativeDetour> Detours = new List<NativeDetour>();

        // P/Invoke declarations for detecting and terminating the Mono IO-worker thread before
        // OrigShutdownManaged runs, to avoid a STOA deadlock on post-build close.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateThread(IntPtr hThread, uint dwExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        // ExitProcess + watchdog P/Invokes.  ExitProcess fires DLL_PROCESS_DETACH; the watchdog
        // is a pure-native Sleep+ExitProcess thread that can't be suspended by STOA.
        [DllImport("kernel32.dll")]
        private static extern void ExitProcess(uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateThread(
            IntPtr lpThreadAttributes, UIntPtr dwStackSize,
            IntPtr lpStartAddress, IntPtr lpParameter,
            uint dwCreationFlags, out uint lpThreadId);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        // NtQueryInformationThread class 9 = ThreadQuerySetWin32StartAddress
        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationThread(
            IntPtr ThreadHandle, int ThreadInformationClass,
            out IntPtr ThreadInformation, int ThreadInformationLength, IntPtr ReturnLength);

        // Fired after compilation but before domain reload; only reliable window for survival code
        internal static readonly List<Action> BeforeShutdownCallbacks = new List<Action>();

        static NativeHookManager()
        {
            if (!EditorVersion.IsSupportedVersion) return;

            // Populate the set of managed plugin DLL names to protect from EATI.
            // Done here (before any detour is installed) so the set is ready before
            // the first AHVTI callback fires.
            try
            {
                string managedDir = System.IO.Path.Combine(UnityEngine.Application.dataPath, "Managed");
                if (System.IO.Directory.Exists(managedDir))
                {
                    foreach (string dll in System.IO.Directory.GetFiles(managedDir, "*.dll"))
                        _managedPluginDllNames.Add(System.IO.Path.GetFileNameWithoutExtension(dll));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NativeHookManager] Failed to enumerate Assets/Managed/*.dll: " + ex.Message);
            }

            // Collect native pointers for Assets/Managed/ PluginImporters so the
            // IsCompatibleWithEditorCPUAndOS hook can selectively force-compatible only our DLLs.
            try
            {
                FieldInfo cachedPtrField = typeof(UnityEngine.Object).GetField(
                    "m_CachedPtr", BindingFlags.Instance | BindingFlags.NonPublic);
                if (cachedPtrField != null)
                {
                    string managedAssetDir = "Assets/Managed";
                    foreach (string dllName in _managedPluginDllNames)
                    {
                        string assetPath = managedAssetDir + "/" + dllName + ".dll";
                        PluginImporter importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
                        if (importer != null)
                        {
                            IntPtr nativePtr = (IntPtr)cachedPtrField.GetValue(importer);
                            if (nativePtr != IntPtr.Zero)
                                _managedPluginNativePtrs.Add(nativePtr);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NativeHookManager] Failed to collect managed plugin native pointers: " + ex.Message);
            }

            NativeHookFunctionOffsets offsets = EditorVersion.Current.FunctionOffsets;

            // Apply our detours here and save the trampoline to call the original function.
            // Wrapped in try/catch: if ApplyEditorDetour throws (e.g. wrong binary offset) we must NOT
            // let the exception propagate out of the static constructor -- a throwing static constructor
            // causes TypeInitializationException on every subsequent access to any member of this type,
            // which would break BeforeShutdownCallbacks.Add() in ManagedPluginDomainFix.
            try
            {
                OrigShutdownManaged = ApplyEditorDetour<ShutdownManaged>(offsets.ShutdownManaged, new ShutdownManaged(OnShutdownManaged));
            }
            catch (Exception ex)
            {
                Debug.LogError("[NativeHookManager] Failed to install ShutdownManaged detour. " +
                               "Domain reload file-copy safety net will not function. Exception: " + ex.Message);
            }

            // Install EATI hook when offset is available for this Unity version.
            long eatIOffset = offsets.ExtractAssemblyTypeInfoAll;
            if (eatIOffset != 0)
            {
                try
                {
                    _origEATI = ApplyEditorDetour<ExtractAssemblyTypeInfoAllDelegate>(
                        eatIOffset,
                        new ExtractAssemblyTypeInfoAllDelegate(OnExtractAssemblyTypeInfoAll));
                }
                catch (Exception ex)
                {
                    Debug.LogError("[NativeHookManager] Failed to install EATI detour. " +
                                   "Pre/post-EATI callbacks will not fire; build will use fallback TypeTree guard. Exception: " + ex.Message);
                }
            }

            // Install AssemblyHasValidTypeInfo hook to skip H3VRCode assemblies during EATI.
            // When InsideEATI is true, returning false prevents EATI from overwriting
            // Library/metadata for H3VRCode, eliminating TypeTree corruption.
            long ahvtiOffset = offsets.AssemblyHasValidTypeInfo;
            if (ahvtiOffset != 0)
            {
                try
                {
                    _origAHVTI = ApplyEditorDetour<AssemblyHasValidTypeInfoDelegate>(
                        ahvtiOffset,
                        new AssemblyHasValidTypeInfoDelegate(OnAssemblyHasValidTypeInfo));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[NativeHookManager] Failed to install AHVTI detour. " +
                                     "H3VRCode TypeTree will still be guarded by backup/restore. Exception: " + ex.Message);
                }
            }

            // Install BuildPlayer_ExtractAndValidateScriptTypes hook.
            // Makes it a no-op (return 1 = success) so the already-valid bundle manifest is
            // preserved and the H3VRCode-less standalone compile doesn't block the build.
            long bpevstOffset = offsets.BuildPlayerExtractAndValidateScriptTypes;
            if (bpevstOffset != 0)
            {
                try
                {
                    _origBPEVST = ApplyEditorDetour<BuildPlayerExtractAndValidateDelegate>(
                        bpevstOffset,
                        new BuildPlayerExtractAndValidateDelegate(OnBuildPlayerExtractAndValidateScriptTypes));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[NativeHookManager] Failed to install BPEVST detour. " +
                                     "Standalone script compile may fail and block the build. Exception: " + ex.Message);
                }
            }

            // Install Application_RequestScriptReload hook. Suppresses reload requests during the
            // ctor-time H3VRCode MonoScript repair window to prevent an infinite domain-reload loop
            // caused by CheckConsistency firing on newly-valid H3VRCode class pointers.
            long rrOffset = offsets.RequestScriptReload;
            if (rrOffset != 0)
            {
                try
                {
                    _origRequestScriptReload = ApplyEditorDetour<d_RequestScriptReload>(
                        rrOffset,
                        new d_RequestScriptReload(OnRequestScriptReload));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[NativeHookManager] Failed to install RequestScriptReload hook. " +
                                     "H3VRCode ctor repair will fall back to delayCall (broken inspector). Exception: " + ex.Message);
                }
            }

            // Install IsCompatibleWithEditorCPUAndOS hook.
            // Forces all PluginImporter DLLs to report as editor-compatible, fixing the
            // .meta enabled:0 default that causes H3VRCode (and other Assets/Managed/ DLLs)
            // to be excluded from script compilation, component menus, and MonoScript lookups.
            long icOffset = offsets.IsCompatibleWithEditorCPUAndOS;
            if (icOffset != 0)
            {
                try
                {
                    _origIsCompatibleWithEditorCPUAndOS = ApplyEditorDetour<d_IsCompatibleWithEditorCPUAndOS>(
                        icOffset,
                        new d_IsCompatibleWithEditorCPUAndOS(OnIsCompatibleWithEditorCPUAndOS));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[NativeHookManager] Failed to install IsCompatibleWithEditorCPUAndOS hook. " +
                                     "Assets/Managed/ DLLs may not appear in compile references. Exception: " + ex.Message);
                }
            }

            // Install TransferScriptingObject<GenerateTypeTreeTransfer> hook.
            // Clears MonoScriptCache+136 (CachedSerializationData) before each bundle type-table
            // TypeTree generation, forcing fresh TypeTree from the live pClass (MonoScriptCache+8).
            // This prevents stale TypeTrees (from editor-startup EATI or prior builds) from being
            // baked into the bundle, fixing the Build 2 anvilPrefab regression.
            long tsoGttOffset = offsets.TransferScriptingObjectGTT;
            if (tsoGttOffset != 0)
            {
                try
                {
                    _origTSOGTT = ApplyEditorDetour<d_TransferScriptingObjectGTT>(
                        tsoGttOffset,
                        new d_TransferScriptingObjectGTT(OnTransferScriptingObjectGTT));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[NativeHookManager] Failed to install TSOGTT detour. " +
                                     "Build 2 anvilPrefab regression may persist. Exception: " + ex.Message);
                }
            }

            // STOA hook REMOVED — permanently no-op'ing STOA hung Unity on WM_CLOSE (Mono needs it
            // for teardown). IO-worker deadlock is fixed instead by TerminateMonoIOWorkers() below.

            // NOTE: PIOLA and ReloadAllUsedAssemblies hooks are not installed — both are
            // re-entrant from an [InitializeOnLoad] context and cause crashes in mono.dll.

            // On process exit, editorApplicationQuit fires before exit().  Kill mono I/O
            // workers and dispose detours here so CRT atexit doesn't deadlock on STOA
            // or still-active NativeDetour callbacks.  No ExitProcess — normal close.
            RegisterQuitCallback();
        }

        private static void RegisterQuitCallback()
        {
            try
            {
                var field = typeof(EditorApplication).GetField(
                    "editorApplicationQuit",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (field != null)
                {
                    var existing = (UnityEngine.Events.UnityAction)field.GetValue(null);
                    field.SetValue(null, (UnityEngine.Events.UnityAction)delegate
                    {
                        // Start the native watchdog BEFORE Unity's quit handler.
                        // If CleanupMono's STOA hangs (after our cleanup below),
                        // ExitProcess fires after 5s.  Metadata is already written
                        // at this point (before CoreShutdown runs).
                        StartNativeWatchdog(5000);

                        if (existing != null)
                        {
                            try { existing(); }
                            catch { }
                        }

                        TerminateMonoIOWorkers();

                        foreach (var detour in Detours) detour.Dispose();
                        Detours.Clear();
                    });
                }
            }
            catch { }
        }

        public static T ApplyEditorDetour<T>(long from, Delegate to) where T : class
        {
            // Avoid crashing the editor if we're loaded in the wrong Unity version
            if (!EditorVersion.IsSupportedVersion) return null;

            // Get the base address of the Unity module and the address in memory of the function
            IntPtr editorBase = DynDll.OpenLibrary("Unity.exe");
            IntPtr fromPtr = (IntPtr)(editorBase.ToInt64() + from);

            // Get a function pointer for the managed callback
            var toPtr = Marshal.GetFunctionPointerForDelegate(to);

            // Make a detour and add it to the list
            var detour = new NativeDetour(fromPtr, toPtr, new NativeDetourConfig { ManualApply = true });
            Detours.Add(detour);

            // Apply the detour and generate a trampoline for it, which we return
            var original = detour.GenerateTrampoline(to.GetType().GetMethod("Invoke")).CreateDelegate(typeof(T)) as T;
            detour.Apply();
            return original;
        }

        public static Delegate GetDelegateForFunctionPointer<T>(long from)
        {
            // Avoid crashing the editor if we're loaded in the wrong Unity version
            if (!EditorVersion.IsSupportedVersion) return null;

            // Get the base address for the Unity module and apply the offset
            IntPtr editorBase = DynDll.OpenLibrary("Unity.exe");
            return Marshal.GetDelegateForFunctionPointer((IntPtr)(editorBase.ToInt64() + from), typeof(T));
        }

        // NOTE: GetCompatibleWithEditorOrAnyPlatform hook not installed.
        // Its prologue contains RIP-relative instructions that MonoMod copies verbatim to the
        // trampoline page, causing corrupted memory accesses. The IsCompatibleWithEditorCPUAndOS
        // hook handles H3VRCode references instead (it calls GetCompatibleWithEditorOrAnyPlatform
        // internally, so force-returning true for our DLLs achieves the same result).

        // Saved thisApp pointer from the most-recently suppressed RequestScriptReload call.
        // Replayed by ManagedPluginDomainFix when suppression is lifted so that any
        // compilation-complete RequestScriptReload that was swallowed during domain reload
        // still causes the next compilation pass (and final domain reload) to happen.
        private static volatile IntPtr _suppressedReloadApp = IntPtr.Zero;

        // RequestScriptReload hook: returns early when suppression is active, but records
        // the call so it can be replayed once the suppression window closes.
        private static void OnRequestScriptReload(IntPtr thisApp)
        {
            if (SuppressRequestScriptReload)
            {
                _suppressedReloadApp = thisApp; // overwrite is fine; all calls use the same App ptr
                return;
            }
            _origRequestScriptReload(thisApp);
        }

        /// <summary>
        /// Replays a suppressed RequestScriptReload. Call after clearing SuppressRequestScriptReload.
        /// No-op if nothing was suppressed.
        /// </summary>
        internal static void ReplayPendingScriptReloadIfNeeded()
        {
            IntPtr app = _suppressedReloadApp;
            if (app == IntPtr.Zero || _origRequestScriptReload == null) return;
            _suppressedReloadApp = IntPtr.Zero; // consume the pending call
            _origRequestScriptReload(app);
        }

        /// <summary>
        /// Discards a pending suppressed RequestScriptReload without replaying it.
        /// Use after RebuildFromAwake-based repairs — RebuildFromAwake triggers a reload
        /// as a side effect that should not propagate.
        /// </summary>
        internal static void DiscardPendingScriptReload()
        {
            _suppressedReloadApp = IntPtr.Zero;
        }

        // During bundle builds, clear CachedSerializationData (cache+136) before each call so the
        // TypeTree is regenerated from the live pClass — fixes the Build 2 anvilPrefab regression
        // caused by stale TypeTrees cached from editor startup or a prior build run.
        private static void OnTransferScriptingObjectGTT(long a1, long a2, long a3, long a4)
        {
            if (InsideBundleEATI && a4 != 0)
            {
                try
                {
                    Marshal.WriteIntPtr((IntPtr)(a4 + 136), IntPtr.Zero);
                }
                catch { }
            }
            _origTSOGTT(a1, a2, a3, a4);
        }

        // Force-return true for Assets/Managed/ DLLs only. Unity Extension DLLs are passed through
        // — forcing them causes CS1704 duplicate reference errors from platform-override variants.
        private static byte OnIsCompatibleWithEditorCPUAndOS(IntPtr thisPluginImporter, IntPtr buildTargetGroupName, IntPtr buildTargetName)
        {
            if (_managedPluginNativePtrs.Contains(thisPluginImporter))
                return 1;
            return _origIsCompatibleWithEditorCPUAndOS(thisPluginImporter, buildTargetGroupName, buildTargetName);
        }

        private static byte OnBuildPlayerExtractAndValidateScriptTypes(
            long a1, IntPtr a2, uint platformGroup, uint platform, IntPtr a5, long a6, long a7)
        {
            // Skip the standalone compile. The bundle manifest was already built before this
            // call, so returning 1 (success) allows the pipeline to continue normally.
            return 1; // 1 = success
        }

        private static byte OnExtractAssemblyTypeInfoAll(int policyIndex, uint assemblyId, byte buildTarget, long typeInfoCollector)
        {
            // For standalone EATIs (buildTarget != 1): force InsideEATI=true so the AHVTI hook
            // blocks H3VRCode re-extraction regardless of whether Build.cs already set the flag.
            bool wasInsideEATI = InsideEATI;
            bool isStandaloneEATI = (buildTarget != 1);
            if (isStandaloneEATI)
            {
                InsideEATI = true;
            }

            foreach (var cb in BeforeEATICallbacks)
                try { cb(); }
                catch (Exception ex) { Debug.LogException(ex); }

            byte result = _origEATI(policyIndex, assemblyId, buildTarget, typeInfoCollector);

            foreach (var cb in AfterEATICallbacks)
                try { cb(); }
                catch (Exception ex) { Debug.LogException(ex); }

            if (isStandaloneEATI) InsideEATI = wasInsideEATI;
            return result;
        }

        // During bundle builds: return false for ALL Assets/Managed/ DLLs to prevent EATI from
        // overwriting Library/metadata. During standalone/startup EATIs: block only H3VRCode
        // so BepInEx/OtherLoader/Sodalite remain available to the script compiler.
        // Unity string at *a1: *(long*)a1==0 → small-string at a1+8; else heap char*.
        private static byte OnAssemblyHasValidTypeInfo(long a1)
        {
            if (BypassAHVTIBlock)
                return _origAHVTI(a1);

            if (InsideEATI && a1 != 0)
            {
                try
                {
                    long firstWord = Marshal.ReadInt64((IntPtr)a1);
                    IntPtr namePtr = (firstWord == 0) ? (IntPtr)(a1 + 8) : (IntPtr)firstWord;
                    string name = Marshal.PtrToStringAnsi(namePtr);
                    if (name != null && IsBlockedForCurrentEATI(name))
                    {
                        return 0; // false → EATI skips this assembly; Library/metadata unchanged
                    }
                }
                catch { }
            }
            return _origAHVTI(a1);
        }

        // True when 'name' should be excluded from EATI: always H3VRCode, and during bundle builds
        // all Assets/Managed/ DLLs to prevent TypeTree/m_pClass cache corruption.
        private static bool IsBlockedForCurrentEATI(string name)
        {
            // H3VRCode is always blocked while InsideEATI is true.
            if (name.IndexOf("H3VRCode", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            // All other Assets/Managed/ DLLs are only blocked during the actual bundle build.
            if (InsideBundleEATI)
            {
                string baseName = System.IO.Path.GetFileNameWithoutExtension(name);
                if (!string.IsNullOrEmpty(baseName) && _managedPluginDllNames.Contains(baseName))
                    return true;
                // Block user assemblies too — Build 1's stub H3VRCode leaves stale TypeTrees in
                // Library/metadata for Assembly-CSharp types inheriting AnvilAsset (Build 2 regression).
                if (baseName != null && (
                    baseName.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Equals("Assembly-CSharp-firstpass", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }

        private static void OnShutdownManaged()
        {
            // Fire pre-shutdown callbacks, then dispose detours BEFORE native teardown
            // so CRT atexit handlers (which fire during exit()) don't deadlock on the
            // still-active NativeDetour callbacks.
            try
            {
                foreach (var cb in BeforeShutdownCallbacks)
                {
                    try { cb(); }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
            }
            finally
            {
                // Dispose detours first — metadata writes can now complete.
                foreach (var detour in Detours) detour.Dispose();
                Detours.Clear();

                TerminateMonoIOWorkers();

                OrigShutdownManaged();
            }
        }

        // Terminates all threads whose Win32 start address lies within mono.dll.
        // These are Mono's internal IO-worker threads that can block in uninterruptible I/O waits.
        // If they're alive when mono_thread_suspend_all_other_threads runs during domain unload,
        // STOA deadlocks because it can't suspend them.  Killing them lets STOA succeed naturally.
        // Safe to call before domain teardown: the domain is about to be unloaded anyway, so
        // thread-held Mono locks become irrelevant.
        private static void TerminateMonoIOWorkers()
        {
            try
            {
                IntPtr monoBase = DynDll.OpenLibrary("mono.dll");
                if (monoBase == IntPtr.Zero) return;
                long monoStart = monoBase.ToInt64();
                long monoEnd   = monoStart + 16L * 1024 * 1024;

                uint selfTid = GetCurrentThreadId();
                const uint THREAD_TERMINATE = 0x0001;
                const uint THREAD_QUERY_INFORMATION = 0x0040;
                const int  ThreadQuerySetWin32StartAddress = 9;

                foreach (System.Diagnostics.ProcessThread pt in
                         System.Diagnostics.Process.GetCurrentProcess().Threads)
                {
                    if ((uint)pt.Id == selfTid) continue;
                    IntPtr hThread = OpenThread(THREAD_TERMINATE | THREAD_QUERY_INFORMATION, false, (uint)pt.Id);
                    if (hThread == IntPtr.Zero) continue;
                    try
                    {
                        IntPtr startAddr;
                        int status = NtQueryInformationThread(hThread,
                            ThreadQuerySetWin32StartAddress,
                            out startAddr, IntPtr.Size, IntPtr.Zero);
                        if (status == 0)
                        {
                            long addr = startAddr.ToInt64();
                            if (addr >= monoStart && addr < monoEnd)
                            {
                                Debug.Log(string.Format(
                                    "[NativeHookManager] Terminating Mono IO-worker (TID 0x{0:X}) before domain teardown",
                                    pt.Id));
                                TerminateThread(hThread, 0);
                            }
                        }
                    }
                    finally
                    {
                        CloseHandle(hThread);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NativeHookManager] TerminateMonoIOWorkers failed: " + ex.Message);
            }
        }

        // Unmanaged watchdog: if OrigShutdownManaged hangs on STOA deadlock, ExitProcess
        // fires after timeoutMs.  Uses only kernel32 Sleep/ExitProcess — no managed calls
        // (managed threads are suspended by STOA).  Fires DLL_PROCESS_DETACH.
        private static void StartNativeWatchdog(uint timeoutMs)
        {
            try
            {
                IntPtr k32 = GetModuleHandle("kernel32.dll");
                IntPtr sleep = GetProcAddress(k32, "Sleep");
                IntPtr exitProc = GetProcAddress(k32, "ExitProcess");
                if (sleep == IntPtr.Zero || exitProc == IntPtr.Zero) return;

                System.IO.MemoryStream ms = new System.IO.MemoryStream();
                ms.Write(new byte[] { 0x48, 0xC7, 0xC1 }, 0, 3);
                ms.Write(BitConverter.GetBytes(timeoutMs), 0, 4);
                ms.Write(new byte[] { 0x48, 0xB8 }, 0, 2);
                ms.Write(BitConverter.GetBytes(sleep.ToInt64()), 0, 8);
                ms.Write(new byte[] { 0x48, 0x83, 0xEC, 0x28, 0xFF, 0xD0, 0x48, 0x83, 0xC4, 0x28 }, 0, 10);
                ms.Write(new byte[] { 0x48, 0xC7, 0xC1, 0x00, 0x00, 0x00, 0x00 }, 0, 7);
                ms.Write(new byte[] { 0x48, 0xB8 }, 0, 2);
                ms.Write(BitConverter.GetBytes(exitProc.ToInt64()), 0, 8);
                ms.Write(new byte[] { 0x48, 0x83, 0xEC, 0x28, 0xFF, 0xD0 }, 0, 6);
                byte[] code = ms.ToArray();

                IntPtr exec = VirtualAlloc(IntPtr.Zero, (UIntPtr)code.Length, 0x3000, 0x40);
                if (exec == IntPtr.Zero) return;
                Marshal.Copy(code, 0, exec, code.Length);
                uint tid;
                CreateThread(IntPtr.Zero, UIntPtr.Zero, exec, IntPtr.Zero, 0, out tid);
            }
            catch { }
        }
    }
}
