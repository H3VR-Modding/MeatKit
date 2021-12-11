using System;
using System.Globalization;
using System.IO;
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
    }
}