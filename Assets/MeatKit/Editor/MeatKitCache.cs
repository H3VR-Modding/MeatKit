using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    [Serializable]
    public class MeatKitCache
    {
        private const string CacheFileName = "meatkit.json";

        [SerializeField]
        private string _lastBuildTime = default(DateTime).ToString(CultureInfo.InvariantCulture);

        [SerializeField]
        private string _lastBuildDuration = default(TimeSpan).ToString();
        
        [SerializeField]
        private string _gameManagedLocation;

        [SerializeField]
        private string _lastImportedAssembly;

        [SerializeField]
        private string _lastSelectedProfileGuid;

        [SerializeField]
        private string _lastUpdateCheckTime = default(DateTime).ToString(CultureInfo.InvariantCulture);
        
        private static string CacheFilePath
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.dataPath), CacheFileName); }
        }

        private static MeatKitCache _instance;
        private static MeatKitCache Instance
        {
            get
            {
                if (_instance != null) return _instance;
                
                if (!File.Exists(CacheFilePath))
                {
                    _instance = new MeatKitCache();
                    WriteCache();
                }
                else _instance = JsonUtility.FromJson<MeatKitCache>(File.ReadAllText(CacheFileName));
                return _instance;
            }
        }

        private static void WriteCache()
        {
            File.WriteAllText(CacheFilePath, JsonUtility.ToJson(_instance));
        }
        
        public static DateTime LastBuildTime
        {
            get { return DateTime.Parse(Instance._lastBuildTime); }
            set
            {
                Instance._lastBuildTime = value.ToString(CultureInfo.InvariantCulture);
                WriteCache();
            }
        }
        
        public static TimeSpan LastBuildDuration
        {
            get { return TimeSpan.Parse(Instance._lastBuildDuration); }
            set
            {
                Instance._lastBuildDuration = value.ToString();
                WriteCache();
            }
        }
        
        public static string GameManagedLocation
        {
            get { return Instance._gameManagedLocation; }
            set
            {
                Instance._gameManagedLocation = value;
                WriteCache();
            }
        }
        
        public static string LastImportedAssembly
        {
            get { return Instance._lastImportedAssembly; }
            set
            {
                Instance._lastImportedAssembly = value;
                WriteCache();
            }
        }

        public static BuildProfile LastSelectedProfile
        {
            get
            {
                if (string.IsNullOrEmpty(Instance._lastSelectedProfileGuid)) return null;
                var path = AssetDatabase.GUIDToAssetPath(Instance._lastSelectedProfileGuid);
                return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
            }
            set
            {
                if (value == null) Instance._lastSelectedProfileGuid = "";
                Instance._lastSelectedProfileGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value));
                WriteCache();
            }
        }

        public static DateTime LastUpdateCheckTime
        {
            get { return DateTime.Parse(Instance._lastUpdateCheckTime); }
            set
            {
                Instance._lastUpdateCheckTime = value.ToString(CultureInfo.InvariantCulture);
                WriteCache();
            }
        }
    }
}