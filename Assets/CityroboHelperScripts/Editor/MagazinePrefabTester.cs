using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using FistVR;
using Cityrobo;

public class MagazinePrefabTester : EditorWindow {

	private GameObject MagazinePrefab;
	string PrefabPath;

	[MenuItem("Window/MagazinePrefabTester")]
	public static void ShowWindow()
	{
		//Show existing window instance. If one doesn't exist, make one.
		EditorWindow.GetWindow(typeof(MagazinePrefabTester));
	}

	public void OnGUI()
    {
		GUILayout.Label("MagazinePrefabTester", EditorStyles.boldLabel);
		EditorGUIUtility.labelWidth = 300f;


		MagazinePrefab = (GameObject)EditorGUILayout.ObjectField("Magazine", MagazinePrefab, typeof(GameObject), true);
		if (MagazinePrefab != null)
		{
			PrefabPath = AssetDatabase.GetAssetPath(MagazinePrefab);
			GameObject fromAssetPath = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
			FVRFireArmMagazine mag = fromAssetPath.GetComponent<FVRFireArmMagazine>();
            Debug.Log(mag);
        }
	}
}