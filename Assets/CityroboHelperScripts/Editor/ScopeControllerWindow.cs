#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using OpenScripts2;
using FistVR;

public class ScopeControllerWindow : EditorWindow
{
	CustomScopeInterface ScopeInterface;
	CustomReflexSightInterface ReflexSightInterface;

	[MenuItem("Tools/Scope Controller Window")]
	public static void ShowWindow()
	{
		//Show existing window instance. If one doesn't exist, make one.
		EditorWindow.GetWindow(typeof(ScopeControllerWindow));
	}

	void OnGUI()
	{
		GUILayout.Label("Scope Objects", EditorStyles.boldLabel);
		EditorGUIUtility.labelWidth = 300f;
		ScopeInterface = EditorGUILayout.ObjectField("Scope", ScopeInterface, typeof(CustomScopeInterface), true) as CustomScopeInterface;
		ReflexSightInterface = EditorGUILayout.ObjectField("Reflex Sight", ReflexSightInterface, typeof(CustomReflexSightInterface), true) as CustomReflexSightInterface;
		if (ScopeInterface != null)
        {
			//ScopeInterface.AttachmentInterface.gameObject.SetActive(true);
			ScopeInterface.TextScreenRoot.SetActive(true);
			if (GUILayout.Button("Previous Zoom", GUILayout.ExpandWidth(true)))
            {
				ScopeInterface.PreviousZoomLevel();
            }
			if (GUILayout.Button("Next Zoom", GUILayout.ExpandWidth(true)))
			{
				ScopeInterface.NextZoomLevel();
			}
			if (GUILayout.Button("Previous Reticle", GUILayout.ExpandWidth(true)))
			{
				ScopeInterface.PreviousReticleTexture();
			}
			if (GUILayout.Button("Next Reticle", GUILayout.ExpandWidth(true)))
			{
				ScopeInterface.NextReticleTexture();
			}
		}
        if (ReflexSightInterface != null)
        {
			//ReflexSightInterface.reflexSightInterface.gameObject.SetActive(true);
			if (GUILayout.Button("Previous Zero", GUILayout.ExpandWidth(true)))
			{
				ReflexSightInterface.PreviousZeroDistance();
			}
			if (GUILayout.Button("Next Zero", GUILayout.ExpandWidth(true)))
			{
				ReflexSightInterface.NextZeroDistance();
			}
			if (GUILayout.Button("Previous Reticle", GUILayout.ExpandWidth(true)))
			{
				ReflexSightInterface.PreviousReticleTexture();
			}
			if (GUILayout.Button("Next Reticle", GUILayout.ExpandWidth(true)))
			{
				ReflexSightInterface.NextReticleTexture();
			}
			if (GUILayout.Button("Previous Brightness", GUILayout.ExpandWidth(true)))
			{
				ReflexSightInterface.PreviousBrightnessSetting();
			}
			if (GUILayout.Button("Next Brightness", GUILayout.ExpandWidth(true)))
			{
				ReflexSightInterface.NextBrightnessSetting();
			}
		}
	}

    public void ClearConsole()
    {
		var assembly = Assembly.GetAssembly(typeof(UnityEditor.ActiveEditorTracker));
		var type = assembly.GetType("UnityEditorInternal.LogEntries");
		var method = type.GetMethod("Clear");
		method.Invoke(new object(), null);
	}
}
#endif