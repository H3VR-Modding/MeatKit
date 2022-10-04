using System;
using System.Diagnostics;
using System.IO;
using Valve.VR;

namespace MeatKit
{
    public static class BuildLog
    {
        private static StreamWriter _output;
        private static StringWriter _temp;
        private static Stopwatch _sw;
        private static DateTime _startTime;
        private static bool _failed;
        private static string _completionMessage;
        private static Exception _exception;

        public static void StartNew()
        {
            // Initialize our output file and stopwatch
            _output = new StreamWriter("AssetBundles/buildlog.txt", false);
            _temp = new StringWriter();
            _sw = Stopwatch.StartNew();
            _startTime = DateTime.Now;
        }

        public static void WriteLine(string text)
        {
            if (_output == null) return;
            _temp.WriteLine(text);
        }

        public static void SetCompletionStatus(bool failed, string message, Exception exception)
        {
            _failed = failed;
            _completionMessage = message;
            _exception = exception;
        }

        public static void Finish()
        {
            _sw.Stop();

            WriteProfileInfo();
            
            _output.WriteLine();
            _output.WriteLine("--- MeatKit Build Log ---");
            _output.WriteLine("Start Time: " + _startTime.ToString("dddd, dd MMMM yyyy HH:mm:ssK"));
            _output.WriteLine("Duration  : " + _sw.Elapsed.GetReadableTimespan());
            _output.WriteLine("Status    : " + (_failed ? "FAILED" : "COMPLETED"));
            if (!string.IsNullOrEmpty(_completionMessage))
                _output.WriteLine("Message   : " + _completionMessage);
            if (_exception != null)
            {
                _output.WriteLine("Exception :\n");
                _output.WriteLine(_exception.ToString());
            }

            _output.WriteLine("\n--- Full build log ---");
            _output.Write(_temp.ToString());
            
            _output.Close();
            _output.Dispose();
            _output = null;
        }

        private static void WriteProfileInfo()
        {
            var profile = BuildWindow.SelectedProfile;
            var implicitDependencies = profile.GetRequiredDependencies();

            _output.WriteLine("--- Selected Build Profile ---");
            _output.WriteLine("Thunderstore Metadata");
            _output.WriteLine("  Package Name: " + profile.PackageName);
            _output.WriteLine("  Author      : " + profile.Author);
            _output.WriteLine("  Version     : " + profile.Version);
            _output.WriteLine("  Icon Set    : " + (profile.Icon == null ? "No" : "Yes"));
            _output.WriteLine("  Readme Set  : " + (profile.ReadMe == null ? "No" : "Yes"));
            _output.WriteLine("  Website URL : " + profile.WebsiteURL);
            _output.WriteLine("  Description : " + profile.Description);
            _output.WriteLine("  Implicit Dependencies  : (" + implicitDependencies.Length + ")");
            foreach (var dependency in implicitDependencies)
                _output.WriteLine("    " + dependency);
            _output.WriteLine("  Additional Dependencies: (" + profile.AdditionalDependencies.Length + ")");
            foreach (var dependency in profile.AdditionalDependencies)
                _output.WriteLine("    " + dependency);

            _output.WriteLine("Script Options");
            _output.WriteLine("  Strip Namespaces     : " + profile.StripNamespaces);
            _output.WriteLine("  Additional Namespaces: (" + profile.AdditionalNamespaces.Length + ")");
            foreach (var @namespace in profile.AdditionalNamespaces)
                _output.WriteLine("    " + @namespace);
            _output.WriteLine("  Apply Harmony Patches: " + profile.ApplyHarmonyPatches);

            _output.WriteLine("Export Options");
            _output.WriteLine("  Build Items: (" + profile.BuildItems.Length + ")");
            foreach (var buildItem in profile.BuildItems)
                _output.WriteLine("    " + buildItem);
            _output.WriteLine("  Build Action: " + profile.BuildAction);
            _output.WriteLine("  Export Path : " + profile.ExportPath);
        }
    }
}
