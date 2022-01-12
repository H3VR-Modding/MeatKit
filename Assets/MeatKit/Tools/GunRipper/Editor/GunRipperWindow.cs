#if H3VR_IMPORTED
using FistVR;
#endif
using MeatKit;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class GunRipperWindow : EditorWindow
{

	public GameObject SelectedGameObject;
	public string ExportPath = "Meatkit/Tools/GunRipper/Export";


	[MenuItem("Tools/Gun Ripper")]
	public static void Open()
	{
		GetWindow<GunRipperWindow>("Gun Ripper").Show();
	}

	#if H3VR_IMPORTED

	private void OnGUI()
	{
		EditorGUILayout.LabelField("Selected GameObject", EditorStyles.boldLabel);

		EditorGUI.BeginChangeCheck();
		SelectedGameObject = EditorGUILayout.ObjectField(SelectedGameObject, typeof(GameObject), true) as GameObject;

		if (string.IsNullOrEmpty(ExportPath)) ExportPath = "Meatkit/Tools/GunRipper/Export";
		ExportPath = EditorGUILayout.TextField(ExportPath);

		if (!SelectedGameObject)
		{
			GUILayout.Label("Please select a game object");
			return;
		}

		EditorGUILayout.Space();

		FVRFireArm firearmComp = SelectedGameObject.GetComponent<FVRFireArm>();
		FVRFireArmMagazine magazineComp = SelectedGameObject.GetComponent<FVRFireArmMagazine>();
		FVRFireArmAttachment attachment = SelectedGameObject.GetComponent<FVRFireArmAttachment>();

		if (firearmComp != null && firearmComp.AudioClipSet != null && GUILayout.Button("Rip Firearm Audio"))
		{
			Debug.Log("Ripping Audio!");
			RipAudio(firearmComp.AudioClipSet, "AudioSet");
		}

		if (magazineComp != null && magazineComp.Profile != null && GUILayout.Button("Rip Magazine Audio"))
		{
			Debug.Log("Ripping Audio!");
			RipAudio(magazineComp.Profile, "AudioSet");
		}

		if(attachment != null && (attachment.AudClipAttach != null || attachment.AudClipDettach != null) && GUILayout.Button("Rip Attachment Audio"))
        {
			if(attachment.AudClipAttach != null)
            {
				Debug.Log("Ripping Audio!");
				RipAudio(attachment.AudClipAttach, "AudioAttach");
			}

			if (attachment.AudClipDettach != null)
			{
				Debug.Log("Ripping Audio!");
				RipAudio(attachment.AudClipDettach, "AudioDettach");
			}
		}

		if (firearmComp != null && (firearmComp.RecoilProfile != null || firearmComp.RecoilProfileStocked != null) && GUILayout.Button("Rip Firearm Recoil"))
		{
			if(firearmComp.RecoilProfile != null)
            {
				Debug.Log("Ripping Stockless Recoil!");
				RipRecoil(firearmComp.RecoilProfile, "Recoil");
			}

			if(firearmComp.RecoilProfileStocked != null)
            {
				Debug.Log("Ripping Stocked Recoil!");
				RipRecoil(firearmComp.RecoilProfileStocked, "RecoilStocked");
			}
		}


	}



	private void RipAudio(AudioEvent audioEvent, string suffix)
    {
		string exportFolderPath = "Assets/" + ExportPath.Trim('/');
		string destinationFolderName = SelectedGameObject.name + "_Rip";
		string destinationFolderPath = exportFolderPath + "/" + destinationFolderName;

		if (!AssetDatabase.IsValidFolder(destinationFolderPath))
		{
			AssetDatabase.CreateFolder(exportFolderPath, destinationFolderName);
		}

		RipAudioClips(audioEvent, destinationFolderPath);
	}


	private void RipAudio(FVRFirearmAudioSet audioSet, string suffix)
    {
		string exportFolderPath = "Assets/" + ExportPath.Trim('/');
		string destinationFolderName = SelectedGameObject.name + "_Rip";
		string destinationFolderPath = exportFolderPath + "/" + destinationFolderName;
		string audioPath = destinationFolderPath + "/" + SelectedGameObject.name + "_" + suffix + ".asset";

		if (!AssetDatabase.IsValidFolder(destinationFolderPath))
		{
			AssetDatabase.CreateFolder(exportFolderPath, destinationFolderName);
		}

		FVRFirearmAudioSet audioCopy = CreateInstance<FVRFirearmAudioSet>();
		CopyFields(audioCopy, audioSet);
		RipAudioClips(audioCopy, destinationFolderPath);

		AssetDatabase.DeleteAsset(audioPath);
		AssetDatabase.CreateAsset(audioCopy, audioPath);
		AssetDatabase.SaveAssets();
	}

	private void RipRecoil(FVRFireArmRecoilProfile recoil, string suffix)
    {
		string exportFolderPath = "Assets/" + ExportPath.Trim('/');
		string destinationFolderName = SelectedGameObject.name + "_Rip";
		string destinationFolderPath = exportFolderPath + "/" + destinationFolderName;
		string recoilPath = destinationFolderPath + "/" + SelectedGameObject.name + "_" + suffix + ".asset";

		if (!AssetDatabase.IsValidFolder(destinationFolderPath))
		{
			AssetDatabase.CreateFolder(exportFolderPath, destinationFolderName);
		}

		FVRFireArmRecoilProfile recoilCopy = CreateInstance<FVRFireArmRecoilProfile>();
		CopyFields(recoilCopy, recoil);

		AssetDatabase.DeleteAsset(recoilPath);
		AssetDatabase.CreateAsset(recoilCopy, recoilPath);
		AssetDatabase.SaveAssets();
	}




	private void CopyFields(UnityEngine.Object copyAsset, UnityEngine.Object origAsset, bool allowMismatch = false)
	{
		Type type = origAsset.GetType();
		if (!allowMismatch && type != copyAsset.GetType())
		{
			return;
		}

		BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Default;
		PropertyInfo[] pinfos = type.GetProperties(flags);
		foreach (var pinfo in pinfos)
		{

			if (pinfo.CanWrite)
			{
				try
				{
					pinfo.SetValue(copyAsset, pinfo.GetValue(origAsset, null), null);
				}
				catch
				{

				}
			}
		}
		FieldInfo[] finfos = type.GetFields(flags);
		foreach (var finfo in finfos)
		{
			finfo.SetValue(copyAsset, finfo.GetValue(origAsset));
		}
	}


	private void RipAudioClips(System.Object asset, string exportPath)
    {
		Type type = asset.GetType();
		BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Default;

		FieldInfo[] finfos = type.GetFields(flags);
		foreach (var finfo in finfos)
		{
			Debug.Log("Field: " + finfo.Name + ", Type: " + finfo.FieldType);

			if(finfo.GetValue(asset) is List<AudioClip>)
            {
				Debug.Log("List!");

				List<AudioClip> audioList = finfo.GetValue(asset) as List<AudioClip>;

				for (int i = 0; i < audioList.Count; i++)
                {
					AudioClip clip = audioList[i];
					if(clip != null)
                    {
						Debug.Log("Audio Clip! " + clip.name);
						audioList[i] = SavWav.Save(exportPath, clip);
					}
				}
            }

			if(finfo.FieldType == typeof(AudioEvent))
            {
				Debug.Log("Audio Event!");
				RipAudioClips(finfo.GetValue(asset), exportPath);
			}
		}
	}
#endif
}





