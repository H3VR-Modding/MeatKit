using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    /// <summary>
    ///     Assembly importer class to get the managed assemblies from the game into the Unity editor without
    ///     the editor wanting to crash itself. Original implementation by Nolenz.
    ///     https://github.com/WurstModders/WurstMod-Reloaded/blob/2e33e83284b3a9f39c8df210ad907925d1d7d9d8/WMRWorkbench/Assets/Editor/Manglers/AssemblyMangler.cs
    /// </summary>
    public static partial class MeatKit
    {
        private const string AssemblyName = "Assembly-CSharp";
        private const string AssemblyRename = "H3VRCode-CSharp";
        private const string AssemblyFirstpassName = "Assembly-CSharp-firstpass";
        private const string AssemblyFirstpassRename = "H3VRCode-CSharp-passfirst";

        // Types we want to strip from the main Unity assembly
        private static readonly string[] StripAssemblyTypes =
        {
            // Alloy classes
            "MaterialMapChannelPackerDefinition",
            "Alloy.PackedMapDefinition",
            "Alloy.BaseTextureChannelMapping",
            "Alloy.MapChannel",
            "Alloy.TextureValueChannelMode",
            "Alloy.NormalMapChannelTextureChannelMapping",
            "Alloy.TextureImportConfig",
            "Alloy.MapTextureChannelMapping",
            "AlloyUtils",
            "Alloy.EnumExtension",
            "MinValueAttribute",
            "MaxValueAttribute",
            "AlloyEffectsManager",
            "Alloy.EnumFlagsAttribute",

            // Bakery MonoBehaviours
            "BakeryAlwaysRender",
            "BakeryDirectLight",
            "BakeryLightmapGroup",
            "BakeryLightmapGroupSelector",
            "BakeryLightmappedPrefab",
            "BakeryLightMesh",
            "BakeryPointLight",
            "BakerySkyLight",
            "ftGlobalStorage",
            "ftLightmaps",
            "ftLightmapsStorage",
            "ftLocalStorage",

            // Bakery supporting types
            "ftUniqueIDRegistry",
            "BakeryLightmapGroupPlain"
        };

        // Array of the extra assemblies that need to come with the main Unity assemblies
        private static readonly string[] ExtraAssemblies =
        {
            "DinoFracture.dll",
            "ES2.dll",
            "Valve.Newtonsoft.Json.dll"
        };


        private static void ImportAssemblies(string assembliesDirectory, string destinationDirectory)
        {
            // Remove whatever was there before and make the folder again
            if (!Directory.Exists(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);

            // Load all of our modifiers
            var editors = Extensions.GetAllInstances<AssemblyModifier>();
            foreach (var editor in editors) editor.Applied = false;

            // We need a custom assembly resolver that sometimes points to different directories.
            var rParams = new ReaderParameters
            {
                AssemblyResolver = new RedirectedAssemblyResolver(assembliesDirectory, destinationDirectory)
            };

            // Rename the game's firstpass assembly
            {
                var firstpassAssembly =
                    AssemblyDefinition.ReadAssembly(Path.Combine(assembliesDirectory, AssemblyFirstpassName + ".dll"));
                firstpassAssembly.Name =
                    new AssemblyNameDefinition(AssemblyFirstpassRename, firstpassAssembly.Name.Version);
                firstpassAssembly.MainModule.Name = AssemblyFirstpassRename + ".dll";

                // Apply modifications
                foreach (var editor in editors) editor.ApplyModification(firstpassAssembly);

                firstpassAssembly.Write(Path.Combine(destinationDirectory, AssemblyFirstpassRename + ".dll"));
                firstpassAssembly.Dispose();
            }

            // Main assembly
            {
                // Rename the main assembly
                var mainAssembly =
                    AssemblyDefinition.ReadAssembly(Path.Combine(assembliesDirectory, AssemblyName + ".dll"), rParams);
                mainAssembly.Name = new AssemblyNameDefinition(AssemblyRename, mainAssembly.Name.Version);
                mainAssembly.MainModule.Name = AssemblyRename + ".dll";

                // Change the firstpass reference in this assembly
                mainAssembly.MainModule.AssemblyReferences
                    .First(x => x.Name == AssemblyFirstpassName)
                    .Name = AssemblyFirstpassRename;

                // Strip some types from the assembly to prevent doubles in the editor
                foreach (var typename in StripAssemblyTypes)
                {
                    var type = mainAssembly.MainModule.GetType(typename);
                    if (type != null) mainAssembly.MainModule.Types.Remove(type);
                    else Debug.LogWarning("Type " + typename + " was not found in assembly.");
                }

                // Apply modifications
                foreach (var editor in editors) editor.ApplyModification(mainAssembly);

                // Write the main assembly out into the destination folder and dispose it
                mainAssembly.Write(Path.Combine(destinationDirectory, AssemblyRename + ".dll"));
            }

            // Then lastly copy the other assemblies to the destination folder
            foreach (var file in ExtraAssemblies)
            {
                var path = Path.Combine(assembliesDirectory, file);
                if (File.Exists(path))
                    ImportSingleAssembly(path, destinationDirectory);
            }

            // Check if anything didn't apply
            foreach (var editor in editors)
                if (!editor.Applied)
                    Debug.LogWarning(editor.name + " was not applied while importing.", editor);

            // When we're done importing assemblies, let Unity refresh the asset database
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, "H3VR_IMPORTED");
            NormalizeMetaFileGUIDs();
        }

        private static void ImportSingleAssembly(string assemblyPath, string destinationDirectory)
        {
            var rParams = new ReaderParameters
            {
                AssemblyResolver =
                    new RedirectedAssemblyResolver(Path.GetDirectoryName(assemblyPath), destinationDirectory)
            };

            // We just want to rename the references to the game's assemblies here
            var asm = AssemblyDefinition.ReadAssembly(assemblyPath, rParams);
            foreach (var reference in asm.MainModule.AssemblyReferences)
                switch (reference.Name)
                {
                    case AssemblyName:
                        reference.Name = AssemblyRename;
                        break;
                    case AssemblyFirstpassName:
                        reference.Name = AssemblyFirstpassRename;
                        break;
                }

            asm.Write(Path.Combine(destinationDirectory, Path.GetFileName(assemblyPath)));
            NormalizeMetaFileGUIDs();
        }

        private static void NormalizeMetaFileGUIDs()
        {
            // This is a really important step. We need to make sure that the meta files for the assemblies are generated
            // WITH THE SAME GUIDs each time. Otherwise, if you lose one and didn't have a backup, all your scripts will be missing
            // and that is of course no bueno. Unity expects 32 hexadecimal digits for the guid so we'll use md5.

            // We need every meta file to exist already.
            AssetDatabase.Refresh();

            var hashFunction = MD5.Create();
            var replaceWith = new Regex(@"^guid: [0-9a-f]{32}$", RegexOptions.Multiline);

            foreach (var metaFile in Directory.GetFiles(ManagedDirectory, "*.meta"))
            {
                // First we get the hash
                var assemblyName = Path.GetFileName(metaFile.Substring(0, metaFile.Length - 5));
                var hash = hashFunction.ComputeHash(Encoding.UTF8.GetBytes(assemblyName));
                var hexHash = Extensions.ByteArrayToString(hash).ToLower();

                // Then we need to replace the hash in the meta file with it.
                var metaText = File.ReadAllText(metaFile);
                metaText = replaceWith.Replace(metaText, "guid: " + hexHash);
                File.WriteAllText(metaFile, metaText);
            }

            // If anything was changed we need Unity to apply it immediately.
            AssetDatabase.Refresh();
        }

        /// <summary>
        ///     Assembly resolver that redirects references to another path if not found.
        /// </summary>
        private class RedirectedAssemblyResolver : BaseAssemblyResolver
        {
            private readonly DefaultAssemblyResolver _defaultResolver = new DefaultAssemblyResolver();
            private readonly string[] _redirectPaths;

            public RedirectedAssemblyResolver(params string[] redirectPath)
            {
                _redirectPaths = redirectPath;
            }

            public override AssemblyDefinition Resolve(AssemblyNameReference name)
            {
                AssemblyDefinition asm = null;
                try
                {
                    asm = _defaultResolver.Resolve(name);
                }
                catch (AssemblyResolutionException)
                {
                    foreach (var path in _redirectPaths)
                        try
                        {
                            var asmPath = Path.Combine(path, name.Name + ".dll");
                            if (File.Exists(asmPath))
                                asm = AssemblyDefinition.ReadAssembly(asmPath,
                                    new ReaderParameters {AssemblyResolver = this});
                        }
                        catch (AssemblyResolutionException)
                        {
                            // Ignored
                        }
                }

                if (asm != null) return asm;
                throw new AssemblyResolutionException(name);
            }
        }
    }
}
