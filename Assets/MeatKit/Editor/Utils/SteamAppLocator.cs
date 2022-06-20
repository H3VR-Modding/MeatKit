using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MeatKit
{
    public static class SteamAppLocator
    {
        private const int AppId = 450540;
        private const string AppFolderName = "H3VR";

        public static string LocateGame()
        {
            // Get the main steam installation location via registry.
            var steamDir = (
                    Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) ??
                    Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam", "InstallPath", null))
                as string;

            // If we can't find it, return. This should really only happen if Steam isn't installed.
            if (string.IsNullOrEmpty(steamDir)) return null;

            // Check main steamapps library folder for h3 manifest.
            var manifestFile = @"steamapps\appmanifest_" + AppId + ".acf";
            var gameFolder = @"steamapps\common\" + AppFolderName + @"\";
            if (File.Exists(Path.Combine(steamDir, manifestFile)))
            {
                return Path.Combine(steamDir, gameFolder);
            }

            // We didn't find it, look at other library folders by lazily parsing libraryfolders.
            var libraryFolders = Path.Combine(steamDir, @"steamapps\libraryfolders.vdf");
            foreach (Match match in Regex.Matches(File.ReadAllText(libraryFolders), @"^\s+\""path\""\s+\""(.+)\""$",
                         RegexOptions.Multiline))
            {
                var folder = match.Groups[1].Value;
                if (!File.Exists(Path.Combine(folder, manifestFile))) continue;
                return Path.Combine(folder, gameFolder);
            }

            // Nope. Still can't find it.
            return null;
        }
    }
}