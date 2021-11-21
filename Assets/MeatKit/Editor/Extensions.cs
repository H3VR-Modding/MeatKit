using System;
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
    }
}