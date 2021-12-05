using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using AssetsTools.NET;
using System;
using System.IO;
using Object = UnityEngine.Object;

namespace MeatKit
{
    public class PrefabLoader : EditorWindow
    {
        private AssetBundle _bundle;
        private string[] _assets = new string[0];
        private int _selectedAsset = 0;
    
        [MenuItem("MeatKit/Asset Bundle/Prefab Loader")]
        private static void Init()
        {
            GetWindow<PrefabLoader>().Show();
        }

        private void OnGUI()
        {
            if (GUILayout.Button("Select Asset Bundle"))
            {
                // If there's already a bundle loaded, unload it.
                if (_bundle) _bundle.Unload(false);

                // Ask for the new bundle, load it, and get its assets
                string assetBundlePath = EditorUtility.OpenFilePanel("Select Asset Bundle", String.Empty, String.Empty);
                var replaceMap = new Dictionary<string, string>
                {
                    {"Assembly-CSharp.dll", "H3VRCode-CSharp.dll"},
                    {"Assembly-CSharp-firstpass.dll", "H3VRCode-CSharp-firstpass.dll"}
                };

                string exportDirPath = Path.Combine(MeatKit.MeatKitDir, "ProcessedAssetBundles");
                DirectoryInfo exportDir = Directory.Exists(exportDirPath) ? new DirectoryInfo(exportDirPath) : Directory.CreateDirectory(exportDirPath);
                FileInfo importedFile = new FileInfo(exportDir.FullName + new FileInfo(assetBundlePath).Name + "-imported");
                if (importedFile.Exists)
                    importedFile.Delete();
                MeatKit.ProcessBundle(assetBundlePath, importedFile.FullName, replaceMap, AssetBundleCompressionType.NONE);
                _bundle = AssetBundle.LoadFromFile(importedFile.FullName);
                _assets = _bundle.GetAllAssetNames();
                _selectedAsset = 0;
            }

            if (_assets.Length > 0)
            {
                _selectedAsset = EditorGUILayout.Popup(_selectedAsset, _assets);
                if (GUILayout.Button("Spawn")) Instantiate(_bundle.LoadAsset(_assets[_selectedAsset]));
            }
        }
    }
}