using System;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    public class UpdaterEditorWindow : EditorWindow
    {
        private bool _allowPrerelease;
        
        [MenuItem("MeatKit/Check for updates")]
        public static void Open()
        {
            GetWindow<UpdaterEditorWindow>("MeatKit Updater").Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("MeatKit Updater", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Installed version: " + Updater.CurrentVersion);

            SimpleVersion onlineVersion = Updater.OnlineVersion;
            if (onlineVersion == null) EditorGUILayout.LabelField("Online version: Unknown (check for updates)");
            else EditorGUILayout.LabelField("Online version: " + onlineVersion);

            if (MeatKitCache.LastUpdateCheckTime != default(DateTime))
                GUILayout.Label("Last update check: " + MeatKitCache.LastUpdateCheckTime);
            else GUILayout.Label("Last update check: Never");

            if (!Updater.CheckingForUpdate)
            {
                if (GUILayout.Button("Check for updates"))
                {
                    Updater.CheckForUpdate(_allowPrerelease);
                }

                _allowPrerelease = GUILayout.Toggle(_allowPrerelease, "Allow pre-release versions");

                if (Updater.CurrentVersion.CompareTo(onlineVersion) < 0)
                {
                    if (GUILayout.Button("Update to " + onlineVersion, GUILayout.Height(50)))
                    {
                        Updater.StartUpdate();
                    }
                }
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                GUILayout.Button("Checking...");
                EditorGUI.EndDisabledGroup();
            }
        }
    }
}