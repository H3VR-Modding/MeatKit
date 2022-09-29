using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using OpenScripts2;
using FistVR;
using System.IO;

public class MagazineAttachment_VaultFilesCreator : EditorWindow
{
	private GameObject _primaryMagPrefab;
	private FVRObject _primaryMagFVRObject;
	private ItemSpawnerID _primaryMagISID;

	private MagazineTape _primaryMagTape;

	[MenuItem("Tools/MagazineAttachment VaultFiles-Creator")]
	public static void ShowWindow()
	{
		//Show existing window instance. If one doesn't exist, make one.
		EditorWindow.GetWindow(typeof(MagazineAttachment_VaultFilesCreator));
	}

	void OnGUI()
	{
		GUILayout.Label("MagazineAttachment VaultFiles-Creator", EditorStyles.boldLabel);
		EditorGUIUtility.labelWidth = 300f;

		_primaryMagPrefab = (GameObject)EditorGUILayout.ObjectField("Primary Magazine Prefab", _primaryMagPrefab, typeof(GameObject), true);
		if (_primaryMagPrefab != null) _primaryMagTape = _primaryMagPrefab.GetComponent<MagazineTape>();

		_primaryMagFVRObject = (FVRObject)EditorGUILayout.ObjectField("Primary Magazine FVRObject", _primaryMagFVRObject, typeof(FVRObject), true);
		_primaryMagISID = (ItemSpawnerID)EditorGUILayout.ObjectField("Primary Magazine ItemSpawnerID", _primaryMagISID, typeof(ItemSpawnerID), true);

		if (_primaryMagPrefab != null && GUILayout.Button("Create Prefabs, FVRObjects and ItemSpawnerIDs", GUILayout.ExpandWidth(true)))
		{
			CreateObjects();
		}
	}

    void CreateObjects()
    {
		string prefabPath = AssetDatabase.GetAssetPath(_primaryMagPrefab);
		string objectPath = AssetDatabase.GetAssetPath(_primaryMagFVRObject);
		string ISIDPath = AssetDatabase.GetAssetPath(_primaryMagISID);

		string copyPrefabPath = Path.GetDirectoryName(prefabPath) + "/" + _primaryMagPrefab.name + "_secondary.prefab";
		string copyObjectPath = Path.GetDirectoryName(objectPath) + "/" + _primaryMagFVRObject.name + "_secondary.asset";
		string copyISIDPath = Path.GetDirectoryName(ISIDPath) + "/" + _primaryMagISID.name + "_secondary.asset";

		GameObject copyPrefab = Instantiate(_primaryMagPrefab, null);
		MagazineTape copyMagTape = copyPrefab.GetComponent<MagazineTape>();
		GameObject secondaryPrefab = copyMagTape.SecondaryMagazine.gameObject;
		EditorUtility.SetDirty(secondaryPrefab);
		secondaryPrefab.transform.SetParent(null);
		copyPrefab.transform.SetParent(copyMagTape.SecondaryMagazine.transform);

		AssetDatabase.CopyAsset(objectPath, copyObjectPath);
		FVRObject copyFVRObject = AssetDatabase.LoadAssetAtPath<FVRObject>(copyObjectPath);
		EditorUtility.SetDirty(copyFVRObject);

		AssetDatabase.CopyAsset(ISIDPath, copyISIDPath);
		ItemSpawnerID copyISID = AssetDatabase.LoadAssetAtPath<ItemSpawnerID>(copyISIDPath);
		EditorUtility.SetDirty(copyISID);

		secondaryPrefab.GetComponent<FVRFireArmMagazine>().ObjectWrapper = copyFVRObject;
		_primaryMagTape.SecondaryMagazine.ObjectWrapper = copyFVRObject;
		

		copyISID.IsDisplayedInMainEntry = false;
		copyISID.MainObject = copyFVRObject;
		copyISID.ItemID = secondaryPrefab.name;
		copyISID.DisplayName = copyISID.DisplayName + " Secondary";

		copyFVRObject.m_anvilPrefab.AssetName = copyPrefabPath;
		copyFVRObject.SpawnedFromId = secondaryPrefab.name;
		copyFVRObject.DisplayName = copyFVRObject.DisplayName + " Secondary";
		copyFVRObject.ItemID = secondaryPrefab.name;
		copyFVRObject.OSple = false;

		PrefabUtility.CreatePrefab(copyPrefabPath, secondaryPrefab);

		DestroyImmediate(secondaryPrefab);

		AssetDatabase.SaveAssets();
	}
}
