using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace MeatKit
{
    [InitializeOnLoad]
    public static class AssetBundleIO
    {
        /// <summary>
        /// Toggle for enabling the asset bundle processing, just so we don't
        /// accidentally mess with stuff we don't mean to.
        /// </summary>
        public static bool ProcessingEnabled { get; private set; }

        private static Dictionary<string, string> _replaceMap;
        private static Dictionary<string, List<string>> _scriptUsage;

        static AssetBundleIO()
        {
            // Apply the one hook we need here
            OrigMonoScriptTransferWrite = NativeHookManager.ApplyEditorDetour<MonoScriptTransferWrite>(0xE321E0, new MonoScriptTransferWrite(OnMonoScriptTransferWrite));
            OrigMonoScriptTransferRead = NativeHookManager.ApplyEditorDetour<MonoScriptTransferRead>(0xE34000, new MonoScriptTransferRead(OnMonoScriptTransferRead));
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
        private static void OnMonoScriptTransferWrite(IntPtr monoScript, IntPtr streamedBinaryWrite)
        {
            // If processing is disabled just run the original and skip.
            if (!ProcessingEnabled)
            {
                OrigMonoScriptTransferWrite(monoScript, streamedBinaryWrite);
                return;
            }

            // Create a couple variables for later
            var applied = false;

            // Read the assembly name and class name from memory
            var className = UnityNativeHelper.ReadNativeString(monoScript, MonoScriptClassName);
            var assemblyName = UnityNativeHelper.ReadNativeString(monoScript, MonoScriptAssemblyName);

            // Add it to the scripts usage dictionary
            if (!_scriptUsage.ContainsKey(assemblyName)) _scriptUsage[assemblyName] = new List<string>();
            _scriptUsage[assemblyName].Add(className);

            // Prepare some debugging string
            string debug = "  " + assemblyName + " " + className + ": ";
            
            // Check if we want to remap this assembly name
            string newAssemblyName;
            if (_replaceMap.TryGetValue(assemblyName, out newAssemblyName))
            {
                // If we're processing a type that should exist in the main game assembly, skip translation
                if (assemblyName != MeatKit.AssemblyName || !MeatKit.StripAssemblyTypes.Contains(className))
                {
                    // Write the new assembly name into memory
                    UnityNativeHelper.WriteNativeString(monoScript, MonoScriptAssemblyName, newAssemblyName);
                    applied = true;
                    debug += "ReplaceMap";
                }
                else
                {
                    debug += "Ignored";
                }
            }
            
            // If it didn't exist in the replace map, check if it contains H3VRCode-CSharp. This is for MonoMod assemblies.
            else if (assemblyName.Contains(MeatKit.AssemblyRename))
            {
                // Write the new assembly name into memory
                UnityNativeHelper.WriteNativeString(monoScript, MonoScriptAssemblyName, assemblyName.Replace(MeatKit.AssemblyRename, MeatKit.AssemblyName));
                applied = true;
                debug += "MonoMod";
            }
            else
            {
                debug += "Unchanged";
            }

            BuildLog.WriteLine(debug);
            
            // Let the original method run
            OrigMonoScriptTransferWrite(monoScript, streamedBinaryWrite);

            // If we didn't apply any remapping, skip this last part.
            if (!applied) return;

            // Cleanup by writing the original value back to memory and freeing the memory allocated for the new name.
            UnityNativeHelper.WriteNativeString(monoScript, MonoScriptAssemblyName, assemblyName);
        }

        /// <summary>
        /// Any time the editor reads from an asset bundle, we want to apply our remapping so that references from
        /// the game can be properly deserialized.
        /// </summary>
        private static long OnMonoScriptTransferRead(IntPtr monoScript, IntPtr streamedBinaryRead)
        {
            // Run the original method and return the result if processing is disabled.
            long result = OrigMonoScriptTransferRead(monoScript, streamedBinaryRead);
            if (!ProcessingEnabled) return result;

            // Read the assembly name and class name from memory
            var className = UnityNativeHelper.ReadNativeString(monoScript, MonoScriptClassName);
            var assemblyName = UnityNativeHelper.ReadNativeString(monoScript, MonoScriptAssemblyName);

            // Add it to the scripts usage dictionary
            if (!_scriptUsage.ContainsKey(assemblyName)) _scriptUsage[assemblyName] = new List<string>();
            _scriptUsage[assemblyName].Add(className);

            // Check if we want to remap this assembly name
            string newAssemblyName;
            if (_replaceMap.TryGetValue(assemblyName, out newAssemblyName))
            {
                // If we're processing a type that should exist in the main game assembly, skip translation
                if (assemblyName != MeatKit.AssemblyName || !MeatKit.StripAssemblyTypes.Contains(className))
                    // Write the new assembly name into memory
                    UnityNativeHelper.WriteNativeString(monoScript, MonoScriptAssemblyName, newAssemblyName);
            }
            
            // If it didn't exist in the replace map, check if it contains H3VRCode-CSharp. This is for MonoMod assemblies.
            else if (assemblyName.Contains(MeatKit.AssemblyName))
            {
                // Write the new assembly name into memory
                UnityNativeHelper.WriteNativeString(monoScript, MonoScriptAssemblyName, assemblyName.Replace(MeatKit.AssemblyName, MeatKit.AssemblyRename));
            }

            return result;
        }

        // Actual name: MonoScript::Transfer<StreamedBinaryWrite<0>>(StreamedBinaryWrite<0> &)
        private delegate void MonoScriptTransferWrite(IntPtr monoScript, IntPtr streamedBinaryWrite);

        private static readonly MonoScriptTransferWrite OrigMonoScriptTransferWrite;

        // Actual name: MonoScript::Transfer<StreamedBinaryRead<1>>(StreamedBinaryRead<1> &)
        private delegate long MonoScriptTransferRead(IntPtr monoScript, IntPtr streamedBinaryRead);

        private static readonly MonoScriptTransferRead OrigMonoScriptTransferRead;

        private const int MonoScriptClassName = 224;

        private const int MonoScriptAssemblyName = 320;
    }
}
