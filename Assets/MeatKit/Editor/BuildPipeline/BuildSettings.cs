using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using UnityEditor;
using UnityEngine;
using UnityEngine.WSA;
using Object = UnityEngine.Object;
#if H3VR_IMPORTED
using Valve.Newtonsoft.Json;
using Valve.Newtonsoft.Json.Linq;
#endif


namespace MeatKit
{
    public class BuildSettings : ScriptableObject, IValidatable
    {
        private const string FileName = "BuildSettings.asset";

        private static BuildSettings _instance;

        public string PackageName = "";
        public string Author = "";
        public string Version = "";
        public Texture2D Icon;
        public Object ReadMe;
        public string WebsiteURL = "";
        public string Description = "";
        public string[] AdditionalDependencies = new string[0];
        public BuildItem[] BuildItems = new BuildItem[0];

        public AssetBundleCompressionType BundleCompressionType = AssetBundleCompressionType.LZ4;

        // Post build action
        public BuildAction BuildAction = BuildAction.JustBuildFiles;

        [HideInInspector]
        public string OutputProfile = "";
        
        public static BuildSettings Instance
        {
            get
            {
                if (_instance) return _instance;

                // Check if we already have some build settings somewhere
                var search = Extensions.GetAllInstances<BuildSettings>();
                if (search.Length > 0) return search[0];

                // If we don't, go and make it.
                _instance = CreateInstance<BuildSettings>();
                AssetDatabase.CreateAsset(_instance, MeatKit.MeatKitDir + FileName);
                return _instance;
            }
        }

        public Dictionary<string, BuildMessage> Validate()
        {
            var messages = new Dictionary<string, BuildMessage>();

            // Package name needs to match regex
            if (!Regex.IsMatch(PackageName, @"^[a-zA-Z_0-9]+$"))
                messages["PackageName"] =
                    BuildMessage.Error("Package name can only contain letters, numbers, and underscores.");

            // Make sure the version number is a valid x.x.x
            if (!Regex.IsMatch(Version, @"^\d+\.\d+\.\d+$"))
                messages["Version"] = BuildMessage.Error("Version number must be in format 'x.x.x'.");

            // Description must be no longer than 250 chars
            if (Description.Length > 250)
                messages["Description"] = BuildMessage.Error("Description cannot be longer than 250 characters.");

            // Icon must exist and be 256 x 256
            if (!Icon)
                messages["Icon"] = BuildMessage.Error("Missing icon.");
            else if (Icon.width != 256 || Icon.height != 256)
                messages["Icon"] = BuildMessage.Info("Icon will be resized to 256x256.");

            if (!ReadMe)
                messages["ReadMe"] = BuildMessage.Error("Missing readme.");

            switch (BundleCompressionType)
            {
                case AssetBundleCompressionType.NONE:
                    messages["BundleCompressionType"] = BuildMessage.Warning(
                        "Uncompressed bundles are not recommended for publication. They can and will be very large.");
                    break;
                case AssetBundleCompressionType.LZMA:
                    messages["BundleCompressionType"] = BuildMessage.Info(
                        "LZMA can take longer to compress than LZ4, however it will result in smaller file sizes usually.");
                    break;
            }

            switch (BuildAction)
            {
                case BuildAction.JustBuildFiles:
                    messages["BuildAction"] = BuildMessage.Info("This will just create the files for a Thunderstore package in your AssetBundles folder.");
                    break;
                case BuildAction.CopyToProfile:
                    messages["BuildAction"] = BuildMessage.Info("This will copy the built files into the plugins folder of the selected profile.");
                    if (string.IsNullOrEmpty(OutputProfile))
                        messages["OutputProfile"] = BuildMessage.Error("Please set the output profile.");
                    else if (!Directory.Exists(OutputProfile))
                        messages["OutputProfile"] = BuildMessage.Error("Selected profile no longer exists.");
                    else if (!File.Exists(Path.Combine(OutputProfile, "mods.yml")))
                        messages["OutputProfile"] = BuildMessage.Error("Selected folder is not a r2mm profile. Please select the folder which contains the plugins folder.");
                    break;
                case BuildAction.CreateThunderstorePackage:
                    messages["BuildAction"] = BuildMessage.Info("This will zip up the built files as a final build for importing into r2mm / uploading to Thunderstore.");
                    break;
            }

            return messages;
        }

        public bool EnsureValidForEditor()
        {
            // Go over each build item
            bool hasErrors = false, hasWarnings = false;
            foreach (var item in BuildItems)
                // Check if it has any validation messages
            foreach (var message in item.Validate().Values)
                // Log them
                switch (message.Type)
                {
                    case MessageType.Error:
                        Debug.LogError(AssetDatabase.GetAssetPath(item) + ": " + message.Message);
                        hasErrors = true;
                        break;
                    case MessageType.Warning:
                        Debug.LogWarning(AssetDatabase.GetAssetPath(item) + ": " + message.Message);
                        hasWarnings = true;
                        break;
                    case MessageType.Info:
                        Debug.Log(AssetDatabase.GetAssetPath(item) + ": " + message.Message);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

            // If there's errors don't let anything continue
            if (hasErrors)
            {
                EditorUtility.DisplayDialog("Build errors",
                    "There were errors validating your build items. Please check the console for more info.", "Ok.");
                return false;
            }

            // If there's only warnings, let the user decide if they want to continue
            if (hasWarnings)
                return EditorUtility.DisplayDialog("Build warnings",
                    "Some build items validated with warnings. Continue with build anyway?", "Yes", "No");

            // Otherwise continue
            return true;
        }

        public string[] GetRequiredDependencies()
        {
            return BuildItems
                .Where(x => x != null)
                .SelectMany(x => x.RequiredDependencies).ToArray();
        }

        public void WriteThunderstoreManifest(string location)
        {
#if H3VR_IMPORTED
            var obj = new JObject();
            obj["name"] = PackageName;
            obj["author"] = Author;
            obj["version_number"] = Version;
            obj["description"] = Description;
            obj["website_url"] = string.IsNullOrEmpty(WebsiteURL) ? "" : WebsiteURL;

            // ReSharper disable once CoVariantArrayConversion
            obj["dependencies"] = new JArray(GetRequiredDependencies().Concat(AdditionalDependencies).ToArray());

            File.WriteAllText(location, JsonConvert.SerializeObject(obj));
#endif
        }
    }

    public enum BuildAction
    {
        JustBuildFiles,
        CopyToProfile,
        CreateThunderstorePackage
    }
}