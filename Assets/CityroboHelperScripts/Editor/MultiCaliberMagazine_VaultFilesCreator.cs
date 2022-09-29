using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using FistVR;
using OpenScripts2;

public class MultiCaliberMagazine_VaultFilesCreator : EditorWindow 
{

	private GameObject MagazinePrefab;
	private FVRFireArmMagazine Magazine;
	private MultiCaliberMagazine MultiMag;
	private FVRObject MagazineFVRObject;
	private ItemSpawnerID MagazineISID;

	private Dictionary<FireArmRoundType, FVRObject> _createdFVRObjects;
	private List<GameObject> _allPrefabs;

	[MenuItem("Tools/MultiCaliberMagazine VaultFiles-Creator")]
	public static void ShowWindow()
	{
		//Show existing window instance. If one doesn't exist, make one.
		EditorWindow.GetWindow(typeof(MultiCaliberMagazine_VaultFilesCreator));
	}

	void OnGUI()
    {
		GUILayout.Label("MultiCaliberMagazine VaultFiles-Creator", EditorStyles.boldLabel);
		EditorGUIUtility.labelWidth = 300f;

		MagazinePrefab = (GameObject)EditorGUILayout.ObjectField("Magazine", MagazinePrefab, typeof(GameObject), true);
        if (MagazinePrefab != null) Magazine = MagazinePrefab.GetComponent<FVRFireArmMagazine>();

		MultiMag = (MultiCaliberMagazine)EditorGUILayout.ObjectField("MultiCaliberMagazine", MultiMag, typeof(MultiCaliberMagazine), true);
		MagazineFVRObject = (FVRObject)EditorGUILayout.ObjectField("FVRObject", MagazineFVRObject, typeof(FVRObject), true);
		MagazineISID = (ItemSpawnerID)EditorGUILayout.ObjectField("ItemSpawnerID", MagazineISID, typeof(ItemSpawnerID), true);

		if (GUILayout.Button("Create Prefabs, FVRObjects and ItemSpawnerIDs", GUILayout.ExpandWidth(true)))
        {
			CreateObjects();
        }
		/*
		if (GUILayout.Button("Configure Items", GUILayout.ExpandWidth(true)))
		{
			ConfigureObjects();
		}
		*/
	}

	void CreateObjects()
    {
		_createdFVRObjects = new Dictionary<FireArmRoundType, FVRObject>();
		_allPrefabs = new List<GameObject>();
		_allPrefabs.Add(MagazinePrefab);

        foreach (var caliberDefinition in MultiMag.CaliberDefinitions)
        {
			if (caliberDefinition.RoundType == Magazine.RoundType)
			{
				continue;
			}
			string RoundTypeName = caliberDefinition.RoundType.ToString();
			string[] paths = GetPaths(MagazineFVRObject, false, RoundTypeName);
			AssetDatabase.CopyAsset(paths[0], paths[1]);

			paths = GetPaths(MagazineISID, false, RoundTypeName);
			AssetDatabase.CopyAsset(paths[0], paths[1]);
			
			paths = GetPaths(MagazinePrefab, true, RoundTypeName);
			AssetDatabase.CopyAsset(paths[0], paths[1]);
        }

		ConfigureObjects();
    }

	void ConfigureObjects()
	{
		foreach (var caliberDefinition in MultiMag.CaliberDefinitions)
		{
			if (caliberDefinition.RoundType == Magazine.RoundType)
			{
				_createdFVRObjects.Add(Magazine.RoundType, MagazineFVRObject);
				continue;
			}

			string RoundTypeName = caliberDefinition.RoundType.ToString();

			string[] magObjPaths = GetPaths(MagazineFVRObject, false, RoundTypeName);
			string[] magISIDpath = GetPaths(MagazineISID, false, RoundTypeName);
			string[] magPrefabPath = GetPaths(MagazinePrefab, true, RoundTypeName);

			FVRObject magFVRObjectCopy = AssetDatabase.LoadAssetAtPath<FVRObject>(magObjPaths[1]);
			//Debug.Log("magFVRObjectCopy: " + magFVRObjectCopy.name);
			EditorUtility.SetDirty(magFVRObjectCopy);

			//Configure FVRObject
			magFVRObjectCopy.m_anvilPrefab.AssetName = magPrefabPath[1];
			magFVRObjectCopy.ItemID = magFVRObjectCopy.ItemID + "_" + RoundTypeName;
			magFVRObjectCopy.SpawnedFromId = magFVRObjectCopy.SpawnedFromId + "_" + RoundTypeName;
			magFVRObjectCopy.RoundType = caliberDefinition.RoundType;
			magFVRObjectCopy.MagazineCapacity = caliberDefinition.Capacity;

			
			//Configure ItemSpawnerID
			ItemSpawnerID magISIDCopy = AssetDatabase.LoadAssetAtPath<ItemSpawnerID>(magISIDpath[1]);
			//Debug.Log("magISIDCopy: " + magISIDCopy.name);
			EditorUtility.SetDirty(magISIDCopy);

			magISIDCopy.ItemID = magISIDCopy.ItemID + "_" + RoundTypeName;
			magISIDCopy.MainObject = magFVRObjectCopy;

			
			//Configure Prefab
			GameObject magPrefabCopy = AssetDatabase.LoadAssetAtPath<GameObject>(magPrefabPath[1]);
			//Debug.Log("magPrefabCopy: " + magPrefabCopy.name);
			EditorUtility.SetDirty(magPrefabCopy);

			FVRFireArmMagazine magCopy = magPrefabCopy.GetComponent<FVRFireArmMagazine>();
			//Debug.Log("magCopy: " + magPrefabCopy.GetComponent<FVRFireArmMagazine>());

			magCopy.RoundType = caliberDefinition.RoundType;
			magCopy.m_capacity = caliberDefinition.Capacity;
			magCopy.m_numRounds = caliberDefinition.Capacity;

			magCopy.DisplayBullets = caliberDefinition.DisplayBullets;
			magCopy.DisplayMeshFilters = caliberDefinition.DisplayMeshFilters;
			magCopy.DisplayRenderers = caliberDefinition.DisplayRenderers;
			magCopy.ObjectWrapper = magFVRObjectCopy;
			caliberDefinition.ObjectWrapper = MagazineFVRObject;

			_createdFVRObjects.Add(caliberDefinition.RoundType, magFVRObjectCopy);
			_allPrefabs.Add(magPrefabCopy);
		}

        foreach (var prefab in _allPrefabs)
        {
			int i = 0;
			bool roundTypeFound = false;
			FVRFireArmMagazine mag = prefab.GetComponent<FVRFireArmMagazine>();
			MultiCaliberMagazine multiCaliberMagazine = prefab.GetComponent<MultiCaliberMagazine>();
            foreach (var caliberDefinition in multiCaliberMagazine.CaliberDefinitions)
            {
				caliberDefinition.ObjectWrapper = _createdFVRObjects[caliberDefinition.RoundType];
				if (!roundTypeFound && mag.RoundType != multiCaliberMagazine.CaliberDefinitions[i].RoundType) i++;
				else roundTypeFound = true;
			}
			multiCaliberMagazine.CurrentCaliberDefinition = i;
		}

		AssetDatabase.SaveAssets();
	}

	string[] GetPaths(UnityEngine.Object OB, bool isPrefab, string RoundTypeName)
    {
		string[] paths = new string[2];
		paths[0] = AssetDatabase.GetAssetPath(OB);

		if (isPrefab) paths[1] = Path.GetDirectoryName(AssetDatabase.GetAssetPath(OB)) + "/" + OB.name + "_" + RoundTypeName + ".prefab";
		else paths[1] = Path.GetDirectoryName(AssetDatabase.GetAssetPath(OB)) + "/" + OB.name + "_" + RoundTypeName + ".asset";

		return paths;
    }
}
