using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    [InitializeOnLoad]
    public static class AssetBundleIO
    {
        // Toggles for keeping track if we're processing reads and/or writes
        public static bool ProcessingEnabledRead { get; private set; }
        public static bool ProcessingEnabledWrite { get; private set; }

        // Output dictionaries for remembering what scripts got modified.
        public static Dictionary<string, List<string>> SerializedScriptNames { get; private set; }
        public static Dictionary<string, List<string>> DeserializedScriptNames { get; private set; }

        private static Dictionary<string, string> _replaceMap;

        static AssetBundleIO()
        {
            if (!EditorVersion.IsSupportedVersion) return;
            
            // Apply the one hook we need here
            OrigMonoScriptTransferWrite = NativeHookManager.ApplyEditorDetour<MonoScriptTransferWrite>(EditorVersion.Current.FunctionOffsets.MonoScriptTransferWrite, new MonoScriptTransferWrite(OnMonoScriptTransferWrite));
            OrigMonoScriptTransferRead = NativeHookManager.ApplyEditorDetour<MonoScriptTransferRead>(EditorVersion.Current.FunctionOffsets.MonoScriptTransferRead, new MonoScriptTransferRead(OnMonoScriptTransferRead));
        }

        public static void EnableProcessing(Dictionary<string, string> replaceMap, bool read, bool write)
        {
            _replaceMap = replaceMap;
            SerializedScriptNames = new Dictionary<string, List<string>>();
            DeserializedScriptNames = new Dictionary<string, List<string>>();
            ProcessingEnabledRead = read;
            ProcessingEnabledWrite = write;
        }

        public static void DisableProcessing()
        {
            ProcessingEnabledRead = false;
            ProcessingEnabledWrite = false;
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
            if (!ProcessingEnabledWrite)
            {
                OrigMonoScriptTransferWrite(monoScript, streamedBinaryWrite);
                return;
            }

            // Create a couple variables for later
            var applied = false;

            // Read the assembly name and class name from memory
            var className = UnityNativeHelper.ReadNativeString(monoScript, MonoScriptClassName);
            var assemblyName = UnityNativeHelper.ReadNativeString(monoScript, MonoScriptAssemblyName);
            var namespaceName = UnityNativeHelper.ReadNativeString(monoScript, MonoScriptNamespace);
            var fullName = string.IsNullOrEmpty(namespaceName) ? className : (namespaceName + "." + className);

            Debug.Log("WRITE " + assemblyName + " " + fullName);
            
            // Add it to the scripts usage dictionary
            if (!SerializedScriptNames.ContainsKey(assemblyName)) SerializedScriptNames[assemblyName] = new List<string>();
            SerializedScriptNames[assemblyName].Add(fullName);

            // Prepare some debugging string
            string debug = "  " + assemblyName + " " + fullName + ": ";

            // Check if we want to remap this assembly name
            string newAssemblyName;
            if (_replaceMap.TryGetValue(assemblyName, out newAssemblyName))
            {
                // If we're processing a type that should exist in the main game assembly, skip translation
                if (assemblyName != MeatKit.AssemblyName + ".dll" || !MeatKit.StripAssemblyTypes.Contains(fullName))
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
            if (!ProcessingEnabledRead) return result;

            // Read the assembly name and class name from memory
            var className = UnityNativeHelper.ReadNativeString(monoScript, MonoScriptClassName);
            var assemblyName = UnityNativeHelper.ReadNativeString(monoScript, MonoScriptAssemblyName);
            var namespaceName = UnityNativeHelper.ReadNativeString(monoScript, MonoScriptNamespace);
            var fullName = string.IsNullOrEmpty(namespaceName) ? className : (namespaceName + "." + className);
            
            Debug.Log("READ " + assemblyName + " " + fullName);
            
            // Add it to the scripts usage dictionary
            if (!DeserializedScriptNames.ContainsKey(assemblyName)) DeserializedScriptNames[assemblyName] = new List<string>();
            DeserializedScriptNames[assemblyName].Add(fullName);

            // Check if we want to remap this assembly name
            string newAssemblyName;
            if (_replaceMap.TryGetValue(assemblyName, out newAssemblyName))
            {
                // If we're processing a type that should exist in the main game assembly, skip translation
                if (assemblyName != MeatKit.AssemblyName || !MeatKit.StripAssemblyTypes.Contains(fullName))
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
        
        private const int MonoScriptNamespace = 272;

        private const int MonoScriptAssemblyName = 320;
    }
}
