using System;
using UnityEditor;
using UnityEngine;

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
            string assetBundlePath = EditorUtility.OpenFilePanel("Select Asset Bundle", string.Empty, string.Empty);
            _bundle = AssetBundle.LoadFromFile(assetBundlePath);
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