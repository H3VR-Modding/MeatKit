using System;
using System.IO;
using UnityEngine;
using Valve.Newtonsoft.Json;

namespace MeatKit
{
    public class MeatKitCache
    {
        private const string CacheFileName = "meatkit.json";

        [JsonProperty("LastBuildTime")]
        private DateTime _lastBuildTime;

        [JsonProperty("LastBuildDuration")]
        private TimeSpan _lastBuildDuration;
        
        [JsonProperty("GameManagedLocation")]
        private string _gameManagedLocation;

        [JsonProperty("LastImportedAssembly")]
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
                else _instance = JsonConvert.DeserializeObject<MeatKitCache>(File.ReadAllText(CacheFileName));
                return _instance;
            }
        }

        private static void WriteCache()
        {
            File.WriteAllText(CacheFilePath, JsonConvert.SerializeObject(_instance));
        }
        
        public static DateTime LastBuildTime
        {
            get { return Instance._lastBuildTime; }
            set
            {
                Instance._lastBuildTime = value;
                WriteCache();
            }
        }
        
        public static TimeSpan LastBuildDuration
        {
            get { return Instance._lastBuildDuration; }
            set
            {
                Instance._lastBuildDuration = value;
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