using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    [InitializeOnLoad]
    public static class AssetBundleExport
    {
        /// <summary>
        /// Toggle for enabling the asset bundle processing, just so we don't
        /// accidentally mess with stuff we don't mean to.
        /// </summary>
        public static bool ProcessingEnabled { get; private set; }

        private static Dictionary<string, string> _replaceMap;
        private static Dictionary<string, List<string>> _scriptUsage;

        static AssetBundleExport()
        {
            // Apply the one hook we need here
            OrigMonoScriptTransfer = NativeHookManager.ApplyEditorDetour<MonoScriptTransfer>(0xE321E0, new MonoScriptTransfer(OnMonoScriptTransfer));
        }

        public static void EnableProcessing(Dictionary<string, string> replaceMap)
        {
            _replaceMap = replaceMap;
            _scriptUsage = new Dictionary<string, List<string>>();
            ProcessingEnabled = true;
        }

        public static Dictionary<string, List<string>> DisableProcessing()
        {
            ProcessingEnabled = false;
            return _scriptUsage;
        }
        
        /// <summary>
        /// This is a detour on some of the native Unity editor code which is part of writing data to asset bundles.
        /// When Unity goes to serialize a MonoScript struct, we want to pre-process it a bit before it actually
        /// writes the data to the bundle.
        ///
        /// Our processing includes building a list of used scripts so we can verify the user has their exports setup
        /// properly, as well as remapping the assembly names for some scripts so that references are maintained
        /// correctly when loaded in the game.
        /// </summary>
        private static void OnMonoScriptTransfer(IntPtr monoScript, IntPtr streamedBinaryWrite)
        {
            // If processing is disabled just run the original and skip.
            if (!ProcessingEnabled)
            {
                OrigMonoScriptTransfer(monoScript, streamedBinaryWrite);
                return;
            }
            
            // Create a couple variables for later
            var applied = false;
            var newAssemblyNameLocation = IntPtr.Zero;

            // Read the assembly name and class name from memory
            var className = UnityNativeHelper.ReadNativeString(monoScript, MonoScriptClassName);
            var assemblyName = UnityNativeHelper.ReadNativeString(monoScript, MonoScriptAssemblyName);
            
            // Add it to the scripts usage dictionary
            if (!_scriptUsage.ContainsKey(assemblyName)) _scriptUsage[assemblyName] = new List<string>();
            _scriptUsage[assemblyName].Add(className);

            // Check if we want to remap this assembly name
            var newAssemblyName = "";
            if (_replaceMap.TryGetValue(assemblyName, out newAssemblyName))
            {
                // Write the new assembly name into memory
                newAssemblyNameLocation = UnityNativeHelper.WriteNativeString(monoScript, MonoScriptAssemblyName, newAssemblyName);
                applied = true;
            }

            // Let the original method run
            OrigMonoScriptTransfer(monoScript, streamedBinaryWrite);

            // If we didn't apply any remapping, skip this last part.
            if (!applied) return;
            
            // Cleanup by writing the original value back to memory and freeing the memory allocated for the new name.
            UnityNativeHelper.WriteNativeString(monoScript, MonoScriptAssemblyName, assemblyName);
            Marshal.FreeHGlobal(newAssemblyNameLocation);
        }

        // Actual name: MonoScript::Transfer<StreamedBinaryWrite<0>>(StreamedBinaryWrite<0> &)
        private delegate void MonoScriptTransfer(IntPtr monoScript, IntPtr streamedBinaryWrite);

        private static readonly MonoScriptTransfer OrigMonoScriptTransfer;

        private const int MonoScriptClassName = 224;

        private const int MonoScriptAssemblyName = 320;
    }
}
