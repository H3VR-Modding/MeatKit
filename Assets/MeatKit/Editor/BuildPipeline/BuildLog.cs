using System;
using System.Diagnostics;
using System.IO;

namespace MeatKit
{
    public static class BuildLog
    {
        private static StringWriter _temp;
        private static Stopwatch _sw;
        private static DateTime _startTime;
        private static bool _failed;
        private static string _completionMessage;
        private static Exception _exception;

        public static void StartNew()
        {
            _temp = new StringWriter();
            _sw = Stopwatch.StartNew();
            _startTime = DateTime.Now;
        }

        public static void WriteLine(string text)
        {
            if (_temp == null) return;
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
            
            StreamWriter output = new StreamWriter("AssetBundles/buildlog.txt", false);

            WriteProfileInfo(output);
            output.WriteLine();
            output.WriteLine("--- MeatKit Build Log ---");
            output.WriteLine("Start Time: " + _startTime.ToString("dddd, dd MMMM yyyy HH:mm:ssK"));
            output.WriteLine("Duration  : " + _sw.Elapsed.GetReadableTimespan());
            output.WriteLine("Status    : " + (_failed ? "FAILED" : "COMPLETED"));
            if (!string.IsNullOrEmpty(_completionMessage))
                output.WriteLine("Message   : " + _completionMessage);
            if (_exception != null)
            {
                output.WriteLine("Exception :");
                output.WriteLine(_exception.ToString());
            }

            output.WriteLine("\n--- Full build log ---");
            output.Write(_temp.ToString());
            
            output.Close();
            output.Dispose();
            _temp = null;
        }

        private static void WriteProfileInfo(StreamWriter output)
        {
            var profile = BuildWindow.SelectedProfile;
            var implicitDependencies = profile.GetRequiredDependencies();

            output.WriteLine("--- Selected Build Profile ---");
            output.WriteLine("Thunderstore Metadata");
            output.WriteLine("  Package Name: " + profile.PackageName);
            output.WriteLine("  Author      : " + profile.Author);
            output.WriteLine("  Version     : " + profile.Version);
            output.WriteLine("  Icon Set    : " + (profile.Icon == null ? "No" : "Yes"));
            output.WriteLine("  Readme Set  : " + (profile.ReadMe == null ? "No" : "Yes"));
            output.WriteLine("  Website URL : " + profile.WebsiteURL);
            output.WriteLine("  Description : " + profile.Description);
            output.WriteLine("  Implicit Dependencies  : (" + implicitDependencies.Length + ")");
            foreach (var dependency in implicitDependencies)
                output.WriteLine("    " + dependency);
            output.WriteLine("  Additional Dependencies: (" + profile.AdditionalDependencies.Length + ")");
            foreach (var dependency in profile.AdditionalDependencies)
                output.WriteLine("    " + dependency);

            output.WriteLine("Script Options");
            output.WriteLine("  Strip Namespaces     : " + profile.StripNamespaces);
            output.WriteLine("  Additional Namespaces: (" + profile.AdditionalNamespaces.Length + ")");
            foreach (var @namespace in profile.AdditionalNamespaces)
                output.WriteLine("    " + @namespace);
            output.WriteLine("  Apply Harmony Patches: " + profile.ApplyHarmonyPatches);

            output.WriteLine("Export Options");
            output.WriteLine("  Build Items: (" + profile.BuildItems.Length + ")");
            foreach (var buildItem in profile.BuildItems)
                output.WriteLine("    " + buildItem);
            output.WriteLine("  Build Action: " + profile.BuildAction);
            output.WriteLine("  Export Path : " + profile.ExportPath);
        }
    }
}
