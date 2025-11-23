using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
#if H3VR_IMPORTED
using Valve.Newtonsoft.Json;
using Valve.Newtonsoft.Json.Linq;
#endif


namespace MeatKit
{
    [CreateAssetMenu(menuName = "MeatKit/Build Profile")]
    public class BuildProfile : ScriptableObject, IValidatable
    {
        private const string BaseOutputPath = "AssetBundles/";

        [Header("Thunderstore Metadata")]
        public string PackageName = "";
        public string Author = "";
        public string Version = "";
        public Texture2D Icon;
        public Object ReadMe;
        public Object Changelog;
        public string WebsiteURL = "";
        public string Description = "";
        public string[] AdditionalDependencies = new string[0];

        [Header("Script Options")]
        public bool StripNamespaces = true;
        public string[] AdditionalNamespaces = new string[0];
        public bool ApplyHarmonyPatches = true;

        [Header("Export Options")]
        public BuildItem[] BuildItems = new BuildItem[0];
        public BuildAction BuildAction = BuildAction.JustBuildFiles;

        [HideInInspector]
        public string OutputProfile = "";

        public Dictionary<string, BuildMessage> Validate()
        {
            var messages = new Dictionary<string, BuildMessage>();

            // Package name needs to match regex
            if (!Regex.IsMatch(PackageName, @"^[a-zA-Z_0-9]+$"))
                messages["PackageName"] =
                    BuildMessage.Error("Package name can only contain letters, numbers, and underscores.");

            // Author needs to match regex
            if (!Regex.IsMatch(Author, @"^[a-zA-Z_0-9]+$"))
                messages["Author"] =
                    BuildMessage.Error("Author can only contain letters, numbers, and underscores.");

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
            else if (!AssetDatabase.GetAssetPath(ReadMe).EndsWith(".md", StringComparison.InvariantCultureIgnoreCase))
                messages["ReadMe"] = BuildMessage.Warning("Are you sure this is a Markdown file? It doesn't have the .md file extension.");

            if (Changelog && !AssetDatabase.GetAssetPath(Changelog).EndsWith(".md", StringComparison.InvariantCultureIgnoreCase))
                messages["Changelog"] = BuildMessage.Warning("Are you sure this is a markdown file? It doesn't have the .md extension.");

            switch (BuildAction)
            {
                case BuildAction.JustBuildFiles:
                    messages["BuildAction"] =
                        BuildMessage.Info(
                            "This will just create the files for a Thunderstore package in your AssetBundles folder.");
                    break;
                case BuildAction.CopyToProfile:
                    messages["BuildAction"] =
                        BuildMessage.Info(
                            "This will copy the built files into the plugins folder of the selected profile.");
                    if (string.IsNullOrEmpty(OutputProfile))
                        messages["OutputProfile"] = BuildMessage.Error("Please set the output profile.");
                    else if (!Directory.Exists(OutputProfile))
                        messages["OutputProfile"] = BuildMessage.Error("Selected profile no longer exists.");
                    else if (!File.Exists(Path.Combine(OutputProfile, "mods.yml")))
                        messages["OutputProfile"] = BuildMessage.Error(
                            "Selected folder is not a r2mm profile. Please select the folder which contains the plugins folder.");
                    break;
                case BuildAction.CreateThunderstorePackage:
                    messages["BuildAction"] =
                        BuildMessage.Info(
                            "This will zip up the built files as a final build for importing into r2mm / uploading to Thunderstore.");
                    break;
            }

            return messages;
        }

        public bool EnsureValidForEditor()
        {
            BuildLog.WriteLine("Starting build profile check...");

            // Check ourselves
            bool hasErrors = false, hasWarnings = false;
            foreach (var message in Validate().Values)
            {
                // Log them
                switch (message.Type)
                {
                    case MessageType.Error:
                        Debug.LogError(AssetDatabase.GetAssetPath(this) + ": " + message.Message);
                        hasErrors = true;
                        break;
                    case MessageType.Warning:
                        Debug.LogWarning(AssetDatabase.GetAssetPath(this) + ": " + message.Message);
                        hasWarnings = true;
                        break;
                }

                BuildLog.WriteLine("  " + message.Type + ": " + this + ": " + message.Message);
            }

            // Go over each build item and check for any validation messages
            foreach (var item in BuildItems.Where(x => x != null))
            foreach (var message in item.Validate().Values)
            {
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
                }

                BuildLog.WriteLine("  " + message.Type + ": " + item + ": " + message.Message);
            }


            // If there's errors don't let anything continue
            if (hasErrors)
            {
                EditorUtility.DisplayDialog("Build errors",
                    "There were errors validating your build items. Please check the console for more info.", "Ok.");
                BuildLog.SetCompletionStatus(true, "Build profile failed one or more checks", null);
                return false;
            }

            // If there's only warnings, let the user decide if they want to continue
            if (hasWarnings)
            {
                var continueAnyway = EditorUtility.DisplayDialog("Build warnings",
                    "Some build items validated with warnings. Continue with build anyway?", "Yes", "No");

                BuildLog.WriteLine("Build profile has one or more warnings.");
                BuildLog.WriteLine("  Continue anyway? " + continueAnyway);
                BuildLog.SetCompletionStatus(true, "User canceled build", null);

                return continueAnyway;
            }
            else
            {
                // Otherwise continue
                BuildLog.WriteLine("  Build profile passed all checks!");
                return true;
            }
        }

        public string[] GetRequiredDependencies()
        {
            return BuildItems
                .Where(x => x != null)
                .SelectMany(x => x.RequiredDependencies)
                .Distinct().ToArray();
        }

        public string MainNamespace
        {
            get { return Author + "." + PackageName; }
        }

        public string[] GetRequiredNamespaces()
        {
            return new[] {MainNamespace};
        }

        public string[] GetAllAllowedNamespaces()
        {
            return GetRequiredNamespaces().Concat(AdditionalNamespaces).ToArray();
        }

        public string ExportPath
        {
            get { return Path.Combine(BaseOutputPath, Path.Combine(PackageName, Version)) + Path.DirectorySeparatorChar; }
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

            File.WriteAllText(location, JsonConvert.SerializeObject(obj,Formatting.Indented));
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
