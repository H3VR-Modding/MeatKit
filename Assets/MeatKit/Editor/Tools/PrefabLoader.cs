using System.Collections.Generic;
using MeatKit;
using UnityEditor;
using UnityEngine;

public class PrefabLoader : EditorWindow
{
    private AssetBundle _bundle;
    private string[] _assets = new string[0];
    private int _selectedAsset = 0;

    private static readonly Dictionary<string, string> AssemblyNameReplaceMap = new Dictionary<string, string>
    {
        {MeatKit.MeatKit.AssemblyName + ".dll", MeatKit.MeatKit.AssemblyRename + ".dll"},
        {MeatKit.MeatKit.AssemblyFirstpassName + ".dll", MeatKit.MeatKit.AssemblyFirstpassRename + ".dll"}
    };

    private static readonly Dictionary<string, AssetBundle> LoadedAssetBundles = new Dictionary<string, AssetBundle>();

    [MenuItem("MeatKit/Prefab Loader")]
    private static void Init()
    {
        GetWindow<PrefabLoader>().Show();
    }

    private void OnGUI()
    {
        if (GUILayout.Button("Select Asset Bundle"))
        {
            // If there's already a bundle loaded, unload it.
            if (_bundle) _bundle = null;

            // Ask for the new bundle, load it, and get its assets
            string assetBundlePath = EditorUtility.OpenFilePanel("Select Asset Bundle", string.Empty, string.Empty);
            
            // Make sure the user actually selected a file
            if (!string.IsNullOrEmpty(assetBundlePath))
            {
                // Check if we already loaded it
                if (!LoadedAssetBundles.TryGetValue(assetBundlePath, out _bundle))
                {
                    _bundle = AssetBundle.LoadFromFile(assetBundlePath);
                    LoadedAssetBundles[assetBundlePath] = _bundle;
                }
                
                // Make sure a valid bundle was selected
                if (_bundle != null)
                {
                    _assets = _bundle.GetAllAssetNames();
                    _selectedAsset = 0;
                }
            }
        }

        // Only show spawn button if there's at least one asset
        if (_bundle != null && _assets.Length > 0)
        {
            _selectedAsset = EditorGUILayout.Popup(_selectedAsset, _assets);
            if (GUILayout.Button("Spawn"))
            {
                AssetBundleIO.EnableProcessing(AssemblyNameReplaceMap);
                Instantiate(_bundle.LoadAsset(_assets[_selectedAsset]));
                AssetBundleIO.DisableProcessing();
            }
            
            // Warn the user about the play mode thing
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("References on a prefab loaded object will break after a restart of the editor unless you enter play mode first.", MessageType.Warning);
            }
        }
    }
}
