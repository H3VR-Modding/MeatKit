using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using UnityEditor;

namespace MeatKit
{
    public static partial class MeatKit
    {
        private static void ProcessBundle(string source, string destination, IDictionary<string, string> replaceMap,
            AssetBundleCompressionType recompressAs)
        {
            try
            {
                // Progress bar just in case these files are massive
                EditorUtility.DisplayProgressBar("Exporting bundle", "Loading file...", 0);

                // Load the bundle, then the assets in the bundle
                var am = new AssetsManager();
                var bundle = am.LoadBundleFile(source);
                var assets = am.LoadAssetsFileFromBundle(bundle, 0);

                // Step 2: For each MonoScript asset, alter it's assembly name
                EditorUtility.DisplayProgressBar("Exporting bundle", "Scanning and making changes...", 25);
                var modifications = new List<AssetsReplacer>();
                foreach (var assetInfo in assets.table.GetAssetsOfType(115))
                {
                    // Get the field for this asset
                    var field = am.GetTypeInstance(assets, assetInfo).GetBaseField();
                    var assemblyNameValue = field["m_AssemblyName"].GetValue();

                    // Modify it's assembly name
                    var asmName = assemblyNameValue.AsString();
                    if (replaceMap.ContainsKey(asmName))
                    {
                        assemblyNameValue.Set(replaceMap[asmName]);

                        // Write the modifications to the list
                        var newBytes = field.WriteToByteArray();
                        modifications.Add(new AssetsReplacerFromMemory(0, assetInfo.index, (int) assetInfo.curFileType,
                            0xFFFF, newBytes));
                    }
                }

                // Write asset changes to memory
                // TODO: I don't like that this uses a byte array here
                EditorUtility.DisplayProgressBar("Exporting bundle", "Saving changes...", 50);
                byte[] newAssetData;
                using (var stream = new MemoryStream())
                using (var writer = new AssetsFileWriter(stream))
                {
                    assets.file.Write(writer, 0, modifications, 0);
                    newAssetData = stream.ToArray();
                }


                // Write the whole assets data to the bundle file
                using (var fileStream = new FileStream(destination + ".uncompressed", FileMode.Create))
                {
                    var bunRepl = new BundleReplacerFromMemory(assets.name, null, true, newAssetData, -1);
                    var bunWriter = new AssetsFileWriter(fileStream);
                    bundle.file.Write(bunWriter, new List<BundleReplacer> {bunRepl});
                }

                EditorUtility.DisplayProgressBar("Exporting bundle", "Compressing bundle...", 75);
                am.UnloadAll();
                var compressedBundle = am.LoadBundleFile(destination + ".uncompressed");
                using (var stream = File.OpenWrite(destination))
                using (var writer = new AssetsFileWriter(stream))
                {
                    compressedBundle.file.Pack(compressedBundle.file.reader, writer, recompressAs);
                }

                am.UnloadAll();

                EditorUtility.DisplayProgressBar("Exporting bundle", "Cleaning up", 100);
                File.Delete(destination + ".uncompressed");
            }
            finally
            {
                // Clear the progress bar when we're done
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
