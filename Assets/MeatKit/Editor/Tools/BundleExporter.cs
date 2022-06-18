﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using UnityEditor;

namespace MeatKit
{
    public static partial class MeatKit
    {
        private static void ProcessBundle(string source, string destination, IDictionary<string, string> replaceMap,
            AssetBundleCompressionType recompressAs, Dictionary<string, List<string>> scripts = null)
        {
            if (scripts == null) scripts = new Dictionary<string, List<string>>();
            
            try
            {
                // Step 1: Load the asset bundle.
                EditorUtility.DisplayProgressBar("Processing bundle", "Loading asset bundle...", 0f);
                var am = new AssetsManager();
                var bundle = am.LoadBundleFile(source);
                var assets = am.LoadAssetsFileFromBundle(bundle, 0);

                // Step 2: For each MonoScript asset, alter it's assembly name
                EditorUtility.DisplayProgressBar("Processing bundle", "Modifying assets...", 0.33f);
                var modifications = new List<AssetsReplacer>();
                foreach (AssetFileInfoEx assetInfo in assets.table.assetFileInfo)
                {
                    // We only want MonoScripts (type 115)
                    if (assetInfo.curFileType != 115) continue;

                    // Get the field for this asset
                    var field = am.GetTypeInstance(assets, assetInfo).GetBaseField();
                    var assemblyNameValue = field["m_AssemblyName"].GetValue();
                    var classNameValue = field["m_ClassName"].GetValue();
                    var namespaceValue = field["m_Namespace"].GetValue();
                    var asmName = assemblyNameValue.AsString();
                    string namespaceStr = namespaceValue.AsString();
                    var fullTypeName = (namespaceStr != "" ? namespaceStr + "." : "") + classNameValue.AsString();
                    
                    // Check if we want to replace this name.
                    if (replaceMap.ContainsKey(asmName) && !StripAssemblyTypes.Contains(fullTypeName))
                    {
                        
                        
                        // Modify it's assembly name
                        assemblyNameValue.Set(replaceMap[asmName]);

                        // Write the modifications to the list
                        var newBytes = field.WriteToByteArray();
                        modifications.Add(new AssetsReplacerFromMemory(0, assetInfo.index, (int) assetInfo.curFileType,
                            0xFFFF, newBytes));
                    } else if (asmName.EndsWith(".mm.dll"))
                    {
                        // If this script originated from a MonoMod DLL we need to modify it as well.
                        // We want to just lop off anything after the third-to-last . in the name,
                        // then also replace H3VRCode-CSharp with Assembly-CSharp.
                        int idx = asmName.LastIndexOf('.', asmName.Length - 8);
                        assemblyNameValue.Set(asmName.Substring(0, idx).Replace("H3VRCode-CSharp", "Assembly-CSharp") + ".dll");
                        
                        // Write the modifications to the list
                        var newBytes = field.WriteToByteArray();
                        modifications.Add(new AssetsReplacerFromMemory(0, assetInfo.index, (int) assetInfo.curFileType,
                            0xFFFF, newBytes));
                    }
                    
                    // Add the monoscript entry to the dictionary
                    asmName = assemblyNameValue.AsString();
                    if (!scripts.ContainsKey(asmName)) scripts[asmName] = new List<string>();
                    scripts[asmName].Add(fullTypeName);
                }

                // Step 3: Write the modified assets back into an uncompressed bundle
                EditorUtility.DisplayProgressBar("Processing bundle", "Saving changes...", 0.66f);
                using (var fileStream = new FileStream(destination + ".uncompressed", FileMode.Create))
                {
                    var bunRepl = new BundleReplacerFromAssets(assets.name, assets.name, assets.file, modifications);
                    var bunWriter = new AssetsFileWriter(fileStream);
                    bundle.file.Write(bunWriter, new List<BundleReplacer> {bunRepl});
                }

                // Unload the existing bundle
                am.UnloadAll();

                // Step 4: Re-compress the bundle if requested
                EditorUtility.DisplayProgressBar("Processing bundle", "Recompressing bundle...", 1f);
                if (recompressAs != AssetBundleCompressionType.NONE)
                {
                    var compressedBundle = am.LoadBundleFile(destination + ".uncompressed");
                    using (var fs = File.OpenWrite(destination))
                    using (var writer = new AssetsFileWriter(fs))
                    {
                        compressedBundle.file.Pack(compressedBundle.file.reader, writer, recompressAs);
                    }
                    
                    am.UnloadAll();
                    File.Delete(destination + ".uncompressed");
                }
                else File.Move(destination + ".uncompressed", destination);

            }
            finally
            {
                EditorUtility.ClearProgressBar();
                GC.Collect();
            }
        }
    }
}