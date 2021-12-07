using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MeatKit
{
    public static class Extensions
    {
        public static Type[] GetTypesSafe(this Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null).ToArray();
            }
        }

        public static string ByteArrayToString(byte[] ba)
        {
            var hex = new StringBuilder(ba.Length * 2);
            foreach (var b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        // Modified version of http://answers.unity.com/answers/1425776/view.html
        public static T[] GetAllInstances<T>() where T : ScriptableObject
        {
            return AssetDatabase.FindAssets("t:" + typeof(T).FullName)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .ToArray();
        }

        public static Object[] GetAllInstances(Type t)
        {
            return AssetDatabase.FindAssets("t:" + t.FullName)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(p => AssetDatabase.LoadAssetAtPath(p, t))
                .ToArray();
        }

        // https://stackoverflow.com/a/25223884
        public static string MakeValidFileName(string text, char? replacement = '_')
        {
            if (string.IsNullOrEmpty(text)) return "";

            var invalids = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(text.Length);
            var changed = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (invalids.Contains(c))
                {
                    changed = true;
                    var repl = replacement ?? '\0';
                    if (repl != '\0')
                        sb.Append(repl);
                }
                else
                    sb.Append(c);
            }

            if (sb.Length == 0)
                return "_";
            return changed ? sb.ToString() : text;
        }

        // https://answers.unity.com/questions/150942/texture-scale.html
        public static Texture2D ScaleTexture(this Texture2D source, int targetWidth, int targetHeight)
        {
            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.ARGB32, false);
            for (int i = 0; i < result.height; ++i)
            {
                for (int j = 0; j < result.width; ++j)
                {
                    Color newColor = source.GetPixelBilinear(j / (float) result.width, i / (float) result.height);
                    result.SetPixel(j, i, newColor);
                }
            }

            result.Apply();
            return result;
        }
        
        public static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target) {
            foreach (DirectoryInfo dir in source.GetDirectories())
                CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name));
            foreach (FileInfo file in source.GetFiles())
                file.CopyTo(Path.Combine(target.FullName, file.Name));
        }

        public static void CopyFilesRecursively(string source, string target)
        {
            CopyFilesRecursively(new DirectoryInfo(source), new DirectoryInfo(target));
        }
        
        #region TimeSpan formatting https://stackoverflow.com/a/21649465/8809017
        
        public static string GetReadableTimespan(this TimeSpan ts)
        {
            // formats and its cutoffs based on totalseconds
            var cutoff = new SortedList<long, string>
            {
                {59, "{3:S}"},
                {60, "{2:M}"},
                {60 * 60 - 1, "{2:M}, {3:S}"},
                {60 * 60, "{1:H}"},
                {24 * 60 * 60 - 1, "{1:H}, {2:M}"},
                {24 * 60 * 60, "{0:D}"},
                {long.MaxValue, "{0:D}, {1:H}"}
            };

            // find nearest best match
            var find = cutoff.Keys.ToList()
                .BinarySearch((long) ts.TotalSeconds);
            // negative values indicate a nearest match
            var near = find < 0 ? Math.Abs(find) - 1 : find;
            // use custom formatter to get the string
            return String.Format(
                new HMSFormatter(),
                cutoff[cutoff.Keys[near]],
                ts.Days,
                ts.Hours,
                ts.Minutes,
                ts.Seconds);
        }

        // formatter for forms of
        // seconds/hours/day
        private class HMSFormatter : ICustomFormatter, IFormatProvider
        {
            // list of Formats, with a P customformat for pluralization
            static Dictionary<string, string> timeformats = new Dictionary<string, string>
            {
                {"S", "{0:P:s:s}"},
                {"M", "{0:P:m:m}"},
                {"H", "{0:P:h:h}"},
                {"D", "{0:P:d:d}"}
            };

            public string Format(string format, object arg, IFormatProvider formatProvider)
            {
                return String.Format(new PluralFormatter(), timeformats[format], arg);
            }

            public object GetFormat(Type formatType)
            {
                return formatType == typeof(ICustomFormatter) ? this : null;
            }
        }

        // formats a numeric value based on a format P:Plural:Singular
        private class PluralFormatter : ICustomFormatter, IFormatProvider
        {
            public string Format(string format, object arg, IFormatProvider formatProvider)
            {
                if (arg != null)
                {
                    var parts = format.Split(':'); // ["P", "Plural", "Singular"]

                    if (parts[0] == "P") // correct format?
                    {
                        // which index postion to use
                        int partIndex = (arg.ToString() == "1") ? 2 : 1;
                        // pick string (safe guard for array bounds) and format
                        return String.Format("{0}{1}", arg, (parts.Length > partIndex ? parts[partIndex] : ""));
                    }
                }

                return String.Format(format, arg);
            }

            public object GetFormat(Type formatType)
            {
                return formatType == typeof(ICustomFormatter) ? this : null;
            }
        }
        
        #endregion
    }
}