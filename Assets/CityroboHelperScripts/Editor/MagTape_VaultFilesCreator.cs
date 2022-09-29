using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using OpenScripts2;
using FistVR;
using System.IO;

public class MagTape_VaultFilesCreator : EditorWindow
{
	private GameObject primaryMagPrefab;
	private FVRObject primaryMagFVRObject;
	private ItemSpawnerID primaryMagISID;

	private MagazineTape primaryMagTape;

	[MenuItem("Tools/MagTape VaultFiles-Creator")]
	public static void ShowWindow()
	{
		//Show existing window instance. If one doesn't exist, make one.
		EditorWindow.GetWindow(typeof(MagTape_VaultFilesCreator));
	}

	void OnGUI()
	{
		GUILayout.Label("MagTape VaultFiles-Creator", EditorStyles.boldLabel);
		EditorGUIUtility.labelWidth = 300f;

		primaryMagPrefab = (GameObject)EditorGUILayout.ObjectField("Primary Magazine Prefab", primaryMagPrefab, typeof(GameObject), true);
		if (primaryMagPrefab != null) primaryMagTape = primaryMagPrefab.GetComponent<MagazineTape>();

		primaryMagFVRObject = (FVRObject)EditorGUILayout.ObjectField("Primary Magazine FVRObject", primaryMagFVRObject, typeof(FVRObject), true);
		primaryMagISID = (ItemSpawnerID)EditorGUILayout.ObjectField("Primary Magazine ItemSpawnerID", primaryMagISID, typeof(ItemSpawnerID), true);

		if (primaryMagPrefab != null && GUILayout.Button("Create Prefabs, FVRObjects and ItemSpawnerIDs", GUILayout.ExpandWidth(true)))
		{
			CreateObjects();
		}
	}

    void CreateObjects()
    {
		string prefabPath = AssetDatabase.GetAssetPath(primaryMagPrefab);
		string objectPath = AssetDatabase.GetAssetPath(primaryMagFVRObject);
		string ISIDPath = AssetDatabase.GetAssetPath(primaryMagISID);

		string copyPrefabPath = Path.GetDirectoryName(prefabPath) + "/" + primaryMagPrefab.name + "_secondary.prefab";
		string copyObjectPath = Path.GetDirectoryName(objectPath) + "/" + primaryMagFVRObject.name + "_secondary.asset";
		string copyISIDPath = Path.GetDirectoryName(ISIDPath) + "/" + primaryMagISID.name + "_secondary.asset";

		GameObject copyPrefab = Instantiate(primaryMagPrefab, null);
		MagazineTape copyMagTape = copyPrefab.GetComponent<MagazineTape>();
		GameObject secondaryPrefab = copyMagTape.SecondaryMagazine.gameObject;

		secondaryPrefab.transform.SetParent(null);
		copyPrefab.transform.SetParent(copyMagTape.SecondaryMagazine.transform);

		AssetDatabase.CopyAsset(objectPath, copyObjectPath);
		FVRObject copyFVRObject = AssetDatabase.LoadAssetAtPath<FVRObject>(copyObjectPath);

		AssetDatabase.CopyAsset(ISIDPath, copyISIDPath);
		ItemSpawnerID copyISID = AssetDatabase.LoadAssetAtPath<ItemSpawnerID>(copyISIDPath);

		secondaryPrefab.GetComponent<FVRFireArmMagazine>().ObjectWrapper = copyFVRObject;
		primaryMagTape.SecondaryMagazine.ObjectWrapper = copyFVRObject;

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
	}
}
