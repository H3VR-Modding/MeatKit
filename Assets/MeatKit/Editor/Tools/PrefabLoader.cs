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
            
            // Make sure the user actually selected a file
            if (!string.IsNullOrEmpty(assetBundlePath))
            {
                _bundle = AssetBundle.LoadFromFile(assetBundlePath);
                
                // Make sure a valid bundle was selected
                if (_bundle != null)
                {
                    _assets = _bundle.GetAllAssetNames();
                    _selectedAsset = 0;
                }
            }
        }

        // Only show spawn button if there's at least one asset
        if (_assets.Length > 0)
        {
            _selectedAsset = EditorGUILayout.Popup(_selectedAsset, _assets);
            if (GUILayout.Button("Spawn")) Instantiate(_bundle.LoadAsset(_assets[_selectedAsset]));
            
            // Warn the user about the play mode thing
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("References on a prefab loaded object will break after a restart of the editor unless you enter play mode first.", MessageType.Warning);
            }
        }
    }
}