#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using FistVR;

public class MagazineHelper : EditorWindow
{
	//string myString = "Hello World";
	//bool groupEnabled;
	//bool myBool = true;
	//float myFloat = 1.23f;

	//Magazine properties
	public FVRFireArmMagazine Magazine;
	public bool IsCurved;
	public Transform MagazineRadiusCenter;

	//Cartridge properties
	public GameObject FirstCartridge;
	public int NumberOfCartridges = 1;

	public bool MirrorX;
	public float CartridgeOffsetY = 0f;
	public float CartridgeOffsetZ = 0f;

	public float CartridgeAngleOffsetX;

	//Follower properties
	public bool GenerateFollowerPoints;
	public bool UseFollowerOffsets = false;
	public bool InvertFollowerOffsets = false;

	public GameObject Follower;

	public float FollowerOffsetY = 0f;
	public float FollowerOffsetZ = 0f;

	//Gizmo properties
	public bool ShowGizmoToggle = false;
	public float GizmoSize = 0.01f;

	//Private
	GameObject _cartridgeRoot;

	GameObject[] _CartridgeObjectList;
	MeshFilter[] _CartridgeMeshFilterList;
	MeshRenderer[] _CartridgeMeshRendererList;

	float _cartridgeCurrentX;
	float _cartridgeCurrentY;
	float _cartridgeCurrentZ;

	bool _ready1 = true;
	bool _ready2 = true;

	GameObject _followerRoot;

	GameObject[] _followerObjectList;

	float _followerCurrentX;
	float _followerCurrentY;
	float _followerCurrentZ;

	float _cartridgeAngleX;
	float _cartridgeAngleY;

	MagazineGizmos _gizmo;

	// Add menu item named "Magazine Helper" to the Window menu
	[MenuItem("Tools/Magazine Helper")]
	public static void ShowWindow()
	{
		//Show existing window instance. If one doesn't exist, make one.
		EditorWindow.GetWindow(typeof(MagazineHelper));
	}

	void OnGUI()
	{
		GUILayout.Label("Cartridge Settings", EditorStyles.boldLabel);
		EditorGUIUtility.labelWidth = 300f;
		//myString = EditorGUILayout.TextField ("Text Field", myString);

		//groupEnabled = EditorGUILayout.BeginToggleGroup ("Optional Settings", groupEnabled);
		//myBool = EditorGUILayout.Toggle ("Toggle", myBool);
		//myFloat = EditorGUILayout.Slider ("Slider", myFloat, -3f, 3f);
		//EditorGUILayout.EndToggleGroup ();

		Magazine = (FVRFireArmMagazine)EditorGUILayout.ObjectField("Magazine", Magazine, typeof(FVRFireArmMagazine), true);
		if (Magazine == null)
		{
			EditorGUILayout.HelpBox("Please add Magazine!", MessageType.Error);
			_ready1 = false;
		}
		
		IsCurved = EditorGUILayout.Toggle("is curved", IsCurved);
		if (IsCurved) {
			MagazineRadiusCenter = (Transform)EditorGUILayout.ObjectField("Magazine Radius Center", MagazineRadiusCenter, typeof(Transform),true);
			if (MagazineRadiusCenter == null)
			{
				EditorGUILayout.HelpBox("Please add Magazine Radius Center!", MessageType.Error);
				_ready1 = false;
			}
			else
			{
				CartridgeAngleOffsetX = EditorGUILayout.Slider("Cartridge Angle Offset X", CartridgeAngleOffsetX, -10f, 10f);
				ShowGizmoToggle = EditorGUILayout.Toggle("Show Curved Mag Gizmo", ShowGizmoToggle);
				GizmoSize = EditorGUILayout.FloatField("Gizmo Size", GizmoSize);
			}
		}
		else ShowGizmoToggle = false;

		ShowGizmo(ShowGizmoToggle);
		if (ShowGizmoToggle) _gizmo.GizmoSize = GizmoSize;

		FirstCartridge = (GameObject) EditorGUILayout.ObjectField ("First Cartridge",FirstCartridge, typeof(GameObject), true);
		if (FirstCartridge == null) 
		{
			EditorGUILayout.HelpBox ("Please add Reference Cartridge!", MessageType.Error);
			_ready1 = false;
		}
		
		NumberOfCartridges = EditorGUILayout.IntField ("Number of Cartridges", NumberOfCartridges);
		if (NumberOfCartridges <= 0)
			NumberOfCartridges = 1;

		MirrorX = EditorGUILayout.Toggle ("Is double stacked Magazine (mirror X axis)", MirrorX);
		if (!IsCurved)
		{
			CartridgeOffsetY = EditorGUILayout.Slider("Cartridge Offset Y", CartridgeOffsetY, -0.1f, 0.1f);
			CartridgeOffsetZ = EditorGUILayout.Slider("Cartridge Offset Z", CartridgeOffsetZ, -0.1f, 0.1f);
		}

		GenerateFollowerPoints = EditorGUILayout.BeginToggleGroup ("Generate Follower Points", GenerateFollowerPoints);
		{
			Follower = (GameObject)EditorGUILayout.ObjectField("Last Round Follower", Follower, typeof(GameObject), true);
			if (Follower == null && GenerateFollowerPoints)
			{
				EditorGUILayout.HelpBox("Please add Follower!", MessageType.Error);
				_ready2 = false;
			}


			FollowerOffsetY = CartridgeOffsetY;
			FollowerOffsetZ = CartridgeOffsetZ;

		}
		EditorGUILayout.EndToggleGroup ();

		if (_ready1 && !GenerateFollowerPoints) {
			if (GUILayout.Button ("Add Cartridges", GUILayout.ExpandWidth (true)))
				AddCartridges ();
			if (GUILayout.Button ("Clear Cartridges", GUILayout.ExpandWidth (true)))
				ClearCartridges (true);			
		} else if (_ready1 && _ready2) 
		{
			if (GUILayout.Button ("Add Cartridges and FollowerPoints", GUILayout.ExpandWidth (true))) 
			{	
				AddCartridges ();
				AddFollowerPoints ();
			}
			if (GUILayout.Button ("Clear Cartridges and FollowerPoints", GUILayout.ExpandWidth (true))) 
			{
				ClearCartridges (true);
				ClearFollowerPoints (true);
			}
			if (GUILayout.Button ("Remove FollowerPoint Visuals", GUILayout.ExpandWidth (true))) 
			{
				RemoveFollowerPointVisuals ();
			}
		}
		_ready1 = true;
		_ready2 = true;
	}



	private void AddCartridges()
	{
		ClearCartridges ();
		_CartridgeObjectList = new GameObject[NumberOfCartridges];
		_CartridgeMeshFilterList = new MeshFilter[NumberOfCartridges];
		_CartridgeMeshRendererList = new MeshRenderer[NumberOfCartridges];

		_CartridgeObjectList [0] = FirstCartridge;
		_CartridgeMeshFilterList [0] = FirstCartridge.GetComponent<MeshFilter> ();
		_CartridgeMeshRendererList [0] = FirstCartridge.GetComponent<MeshRenderer> ();


		_cartridgeCurrentX = FirstCartridge.transform.position.x;
		_cartridgeCurrentY = FirstCartridge.transform.position.y;
		_cartridgeCurrentZ = FirstCartridge.transform.position.z;

		if (_cartridgeRoot == null) {
			_cartridgeRoot = new GameObject ();
			_cartridgeRoot.name = "Cartridge Root";
			_cartridgeRoot.transform.parent = FirstCartridge.transform.parent;
			_cartridgeRoot.transform.localPosition = new Vector3 (0, 0, 0);
			_cartridgeRoot.transform.localEulerAngles = new Vector3 (0, 0, 0);
			_cartridgeRoot.transform.localScale = new Vector3 (1, 1, 1);
		}

		for (int i = 2; i <= NumberOfCartridges; i++) {

			if (IsCurved)
            {
				Vector3 curvedPos = CalculateNextCartridgePos(CartridgeAngleOffsetX*(i-1));

				_cartridgeCurrentY = curvedPos.y;
				_cartridgeCurrentZ = curvedPos.z;
			}
			else
            {
				_cartridgeCurrentY += CartridgeOffsetY;
				_cartridgeCurrentZ += CartridgeOffsetZ;
			}

			Quaternion rot = FirstCartridge.transform.localRotation;
			Vector3 euler;
			if (IsCurved) 
				euler = new Vector3(FirstCartridge.transform.localRotation.eulerAngles.x - CartridgeAngleOffsetX * (i - 1), FirstCartridge.transform.localRotation.eulerAngles.y, FirstCartridge.transform.localRotation.eulerAngles.z);
			else 
				euler = new Vector3(FirstCartridge.transform.localRotation.eulerAngles.x, FirstCartridge.transform.localRotation.eulerAngles.y, FirstCartridge.transform.localRotation.eulerAngles.z);
			rot.eulerAngles = euler;
			Vector3 pos = new Vector3 (_cartridgeCurrentX, _cartridgeCurrentY, _cartridgeCurrentZ);
			GameObject nextCartridge = GameObject.Instantiate (FirstCartridge, pos,rot,_cartridgeRoot.transform);

			nextCartridge.name = FirstCartridge.name + " (" + i.ToString() + ")";

			if (MirrorX && i % 2 == 0)
				nextCartridge.transform.localPosition = new Vector3(-nextCartridge.transform.localPosition.x, nextCartridge.transform.localPosition.y, nextCartridge.transform.localPosition.z);

			_CartridgeObjectList[i-1] = nextCartridge;
			_CartridgeMeshFilterList[i-1] = nextCartridge.GetComponent<MeshFilter> ();
			_CartridgeMeshRendererList[i-1] = nextCartridge.GetComponent<MeshRenderer> ();
		}

		Magazine.DisplayBullets = _CartridgeObjectList;
		Magazine.DisplayMeshFilters = _CartridgeMeshFilterList;
		Magazine.DisplayRenderers = _CartridgeMeshRendererList;
	}

	private void ClearCartridges(bool all = false)
	{
		if (_cartridgeRoot != null) 
		{
			int children = _cartridgeRoot.transform.childCount;
			for (int i = children -1 ; i >= 0; i--) {
				GameObject.DestroyImmediate (_cartridgeRoot.transform.GetChild (i).gameObject);
			}
			if (all)
				DestroyImmediate (_cartridgeRoot);
		}
	}

	private void AddFollowerPoints()
	{
		ClearFollowerPoints ();
		_followerObjectList = new GameObject[NumberOfCartridges];

		_followerObjectList [0] = Follower;


		_followerCurrentX = Follower.transform.position.x;
		_followerCurrentY = Follower.transform.position.y;
		_followerCurrentZ = Follower.transform.position.z;

		if (_followerRoot == null) {
			_followerRoot = new GameObject ();
			_followerRoot.name = "Follower Root";
			_followerRoot.transform.parent = Follower.transform.parent;
			_followerRoot.transform.localPosition = new Vector3 (0, 0, 0);
			_followerRoot.transform.localEulerAngles = new Vector3 (0, 0, 0);
			_followerRoot.transform.localScale = new Vector3 (1, 1, 1);
		}

		for (int i = 2; i <= NumberOfCartridges; i++) {

			if (IsCurved)
			{
				Vector3 curvedPos = CalculateNextCartridgePos(CartridgeAngleOffsetX * (i - 1));

				_followerCurrentY = curvedPos.y;
				_followerCurrentZ = curvedPos.z;
			}
			else
			{
				_followerCurrentY += FollowerOffsetY;
				_followerCurrentZ += FollowerOffsetZ;
			}


			Vector3 pos = new Vector3 (_followerCurrentX, _followerCurrentY, _followerCurrentZ);
			Quaternion rot = Follower.transform.localRotation;
			Vector3 euler;
			if (IsCurved) 
				euler = new Vector3(Follower.transform.localRotation.eulerAngles.x - CartridgeAngleOffsetX * (i - 1), Follower.transform.localRotation.eulerAngles.y, Follower.transform.localRotation.eulerAngles.z);
			else 
				euler = new Vector3(Follower.transform.localRotation.eulerAngles.x, Follower.transform.localRotation.eulerAngles.y, Follower.transform.localRotation.eulerAngles.z);
			rot.eulerAngles = euler;

			GameObject nextFollowerPoint = GameObject.Instantiate (Follower,pos,rot,_followerRoot.transform);

			nextFollowerPoint.name = Follower.name + " (" + i.ToString() + ")";

			_followerObjectList[i-1] = nextFollowerPoint;
		}
	}

	private void ClearFollowerPoints(bool all = false)
	{
		if (_followerRoot != null) 
		{
			int children = _followerRoot.transform.childCount;
			for (int i = children -1 ; i >= 0; i--) {
				GameObject.DestroyImmediate (_followerRoot.transform.GetChild (i).gameObject);
			}
			if (all)
				DestroyImmediate (_followerRoot);
		}
	}
	private void RemoveFollowerPointVisuals()
	{
		if (_followerRoot == null)
			return;
		foreach (var f in _followerObjectList) {
			if (f != Follower) 
			{
				MeshRenderer renderer = f.GetComponent<MeshRenderer> ();
				if (renderer == null) 
					renderer = f.GetComponentInChildren<MeshRenderer>();
				DestroyImmediate (renderer);
				MeshFilter filter = f.GetComponent<MeshFilter> ();
				if (filter == null) 
					filter = f.GetComponentInChildren<MeshFilter>();
				DestroyImmediate(filter);
			}

		}
	}

	private void ShowGizmo(bool on)
    {
        switch (on)
        {
			case false:
				if (_gizmo != null) DestroyImmediate(_gizmo);
				break;
			case true:
				if (_gizmo == null) _gizmo = MagazineRadiusCenter.gameObject.AddComponent<MagazineGizmos>();
				break;

			default:
                break;
        }
    }


	private Vector3 CalculateNextCartridgePos(float deltaA)
    {
		deltaA *= Mathf.Deg2Rad;
		Vector3 pos = new Vector3();

		Vector3 delta = FirstCartridge.transform.position - MagazineRadiusCenter.position;
		float radius = Mathf.Sqrt(Mathf.Pow(delta.y, 2) + Mathf.Pow(delta.z, 2));
		float angle = Mathf.Atan2(delta.y, delta.z);

		//ClearConsole();

		//Debug.Log("Radius: " + radius);
		//Debug.Log("Angle: " + Mathf.Rad2Deg*angle);

		float y = Mathf.Sin(angle + deltaA) * radius + MagazineRadiusCenter.transform.position.y;
		float z = Mathf.Cos(angle + deltaA) * radius + MagazineRadiusCenter.transform.position.z;



		pos = new Vector3(FirstCartridge.transform.position.x, y, z);

        //Debug.Log("Before:");

        //Debug.Log(pos.y);
		//Debug.Log(pos.z);

		//GameObject test1 = GameObject.Instantiate(firstCartridge, pos, new Quaternion(), cartridge_root.transform);

		return pos;
    }

    public void ClearConsole()
    {
		var assembly = Assembly.GetAssembly(typeof(UnityEditor.ActiveEditorTracker));
		var type = assembly.GetType("UnityEditorInternal.LogEntries");
		var method = type.GetMethod("Clear");
		method.Invoke(new object(), null);
	}


	public void OnDestroy()
    {
		DestroyImmediate(_gizmo);
    }
}
#endif