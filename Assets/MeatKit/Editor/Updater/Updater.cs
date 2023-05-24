using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Valve.Newtonsoft.Json.Linq;

namespace MeatKit
{
    public static class Updater
    {
        public const string UpdatePackageName = "MeatKitUpdate.unitypackage";
        public const string UpdateUrl = "https://api.github.com/repos/H3VR-Modding/MeatKit/releases";

        
        public static bool CheckingForUpdate { get; private set; }
        private static bool AllowPreReleases { get; set; }

        public static SimpleVersion _currentVersion;

        public static SimpleVersion CurrentVersion
        {
            get
            {
                if (_currentVersion == null)
                {
                    if (File.Exists("ProjectSettings/MeatKitVersion.txt"))
                        _currentVersion = SimpleVersion.Parse(File.ReadAllText("ProjectSettings/MeatKitVersion.txt"));
                    else
                    {
                        File.WriteAllText("ProjectSettings/MeatKitVersion.txt", "0.0.0");
                        _currentVersion = SimpleVersion.Parse("0.0.0");
                    }
                }

                return _currentVersion;
            }
        }

        public static SimpleVersion OnlineVersion { get; private set; }

        private static long OnlineReleaseId { get; set; }

        public static void CheckForUpdate(bool allowPrerelease)
        {
            CheckingForUpdate = true;
            AllowPreReleases = allowPrerelease;
            var request = UnityWebRequest.Get(UpdateUrl);
            AsyncDownloader.WaitForCompletion(request, UpdateCheckComplete);
        }

        private static void UpdateCheckComplete(UnityWebRequest request)
        {
            if (request.isError)
            {
                Debug.LogError("Error fetching releases for " + request.url + "\n" + request.error);
            }
            else
            {
                var response = JArray.Parse(request.downloadHandler.text);
                var latestRelease = response.First(r => !r["prerelease"].Value<bool>() || AllowPreReleases);
                OnlineVersion = SimpleVersion.Parse(latestRelease["tag_name"].Value<string>());
                OnlineReleaseId = latestRelease["id"].Value<long>();
            }

            MeatKitCache.LastUpdateCheckTime = DateTime.Now;
            CheckingForUpdate = false;

            // Force a repaint on the update window if it's open
            UpdaterEditorWindow window = EditorWindow.GetWindow<UpdaterEditorWindow>();
            if (window) window.Repaint();
        }

        public static void StartUpdate()
        {
            if (OnlineVersion == null || OnlineReleaseId == 0) return;

            // Try and fetch the assets on the release
            AsyncDownloader.WaitForCompletion(UnityWebRequest.Get(UpdateUrl + "/" + OnlineReleaseId + "/assets"), CheckForUpdateAsset);
        }

        private static void CheckForUpdateAsset(UnityWebRequest request)
        {
            var response = JArray.Parse(request.downloadHandler.text);

            // Check if any of the filenames are "MeatKitUpdate.unitypackage"
            var updateAsset = response.FirstOrDefault(t => t["name"].Value<string>() == "MeatKitUpdate.unitypackage");
            if (updateAsset == null)
            {
                EditorUtility.DisplayDialog("Failed to update", "Could not find the unity package associated with the target version. Nothing has been modified.", "Ok.");
                return;
            }

            // Start a new request to download the package.
            AsyncDownloader.WaitForCompletion(UnityWebRequest.Get(updateAsset["browser_download_url"].Value<string>()), ApplyUpdatePackage);
        }

        private static void ApplyUpdatePackage(UnityWebRequest request)
        {
            // Save the downloaded file to a temp location
            string tempFile = Path.GetTempFileName();
            File.WriteAllBytes(tempFile, request.downloadHandler.data);

            // Wipe the MeatKit folder so it's fresh and ready for the new stuff
            string dataDir = Path.Combine(Application.dataPath, "MeatKit");
            Directory.Delete(dataDir, true);
            File.Delete(dataDir + ".meta");
            
            // Import all the files from it
            AssetDatabase.ImportPackage(tempFile, false);

            // Kick the asset database to refresh now that we're done 
            AssetDatabase.Refresh();
            
            // Remove the temp file
            File.Delete(tempFile);
            
            // Update the saved version number
            File.WriteAllText("ProjectSettings/MeatKitVersion.txt", OnlineVersion.ToString());
        }
    }
}