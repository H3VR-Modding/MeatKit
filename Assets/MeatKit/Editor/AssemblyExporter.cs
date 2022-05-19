using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine;

namespace MeatKit
{
    public static partial class MeatKit
    {
        private const string EditorAssemblyPath = "Library/ScriptAssemblies/";

        private static void ExportEditorAssembly(string folder, string tempFile = null, Dictionary<string, List<string>> requiredScripts = null)
        {
            // Make a copy of the file if we aren't already given one
            string editorAssembly = EditorAssemblyPath + AssemblyName + ".dll";
            if (!File.Exists(editorAssembly) && !File.Exists(tempFile))
            {
                Debug.LogError("Editor assembly missing! Can't export scripts.");
            }
            else if (string.IsNullOrEmpty(tempFile))
            {
                tempFile = Path.GetTempFileName();
                File.Copy(editorAssembly, tempFile, true);
            }

            // Delete the old file
            var settings = BuildWindow.SelectedProfile;
            var exportPath = folder + settings.PackageName + ".dll";
            if (File.Exists(exportPath)) File.Delete(exportPath);

            var rParams = new ReaderParameters
            {
                AssemblyResolver =
                    new RedirectedAssemblyResolver(Path.GetDirectoryName(typeof(UnityEngine.Object).Assembly.Location), Application.dataPath + "/MeatKit/Managed/")
            };

            // Get the MeatKitPlugin class and rename it
            using (var asm = AssemblyDefinition.ReadAssembly(tempFile, rParams))
            {
                var plugin = asm.MainModule.GetType("MeatKitPlugin");
                plugin.Name = settings.PackageName + "Plugin";

                // This is some quantum bullshit.
                // If you don't enumerate the constructor arguments for attributes their values aren't updated correctly. 
                foreach (var x in GetAllCustomAttributes(asm).SelectMany(a => a.ConstructorArguments))
                {
                }

                // Get the BepInPlugin attribute and replace the values in it with our own
                var str = asm.MainModule.TypeSystem.String;
                var guid = settings.Author + "." + settings.PackageName;
                var pluginAttribute = plugin.CustomAttributes.First(a => a.AttributeType.Name == "BepInPlugin");
                pluginAttribute.ConstructorArguments[0] = new CustomAttributeArgument(str, guid);
                pluginAttribute.ConstructorArguments[1] = new CustomAttributeArgument(str, settings.PackageName);
                pluginAttribute.ConstructorArguments[2] = new CustomAttributeArgument(str, settings.Version);

                // Get the LoadAssets method and make a new body for it
                var loadAssetsMethod = plugin.Methods.First(m => m.Name == "LoadAssets");
                loadAssetsMethod.Body = new MethodBody(loadAssetsMethod);
                var il = loadAssetsMethod.Body.GetILProcessor();

                // Let any build items insert their own code in here
                foreach (var item in settings.BuildItems)
                    item.GenerateLoadAssets(plugin, il);

                // Insert a ret at the end so it's valid
                il.Emit(OpCodes.Ret);

                // Module name needs to be changed away from Assembly-CSharp.dll because it is a reserved name.
                string newAssemblyName = settings.PackageName + ".dll";
                asm.Name = new AssemblyNameDefinition(settings.PackageName, asm.Name.Version);
                asm.MainModule.Name = newAssemblyName;

                // References to renamed unity code must be swapped out.
                foreach (var ii in asm.MainModule.AssemblyReferences)
                {
                    // Rename any references to the game's code
                    if (ii.Name.Contains("H3VRCode-CSharp"))
                    {
                        ii.Name = ii.Name.Replace("H3VRCode-CSharp", "Assembly-CSharp");
                    }
                    
                    // And also if we're referencing a MonoMod DLL, we need to fix reference too
                    if (ii.Name.EndsWith(".mm"))
                    {
                        // What the name currently is:
                        //    Assembly-CSharp.PatchName.mm
                        // What we want:
                        //    Assembly-CSharp
                        // So just lop off anything past the second to last dot
                        int idx = ii.Name.LastIndexOf('.', ii.Name.Length -4);
                        ii.Name = ii.Name.Substring(0, idx);
                    }
                }

                if (BuildWindow.SelectedProfile.StripNamespaces)
                {
                    // Remove types not in an allowed namespace or the global namespace
                    string[] allowedNamespaces = BuildWindow.SelectedProfile.GetAllAllowedNamespaces();
                    List<TypeDefinition> typesToRemove = new List<TypeDefinition>();
                    foreach (var type in asm.MainModule.Types)
                    {
                        if (type.Namespace == "" || allowedNamespaces.Any(x => type.Namespace.Contains(x)))
                            continue;
                        typesToRemove.Add(type);
                    }

                    foreach (var type in typesToRemove) asm.MainModule.Types.Remove(type);
                }

                // Remove the same types we didn't want to import. This cannot be skipped.
                foreach (var type in StripAssemblyTypes
                             .Select(x => asm.MainModule.GetType(x))
                             .Where(x => x != null))
                    asm.MainModule.Types.Remove(type);

                // Check if we're now missing any scripts from the export
                List<string> missing = new List<string>();
                if (requiredScripts != null && requiredScripts.ContainsKey(newAssemblyName))
                {
                    missing.AddRange(requiredScripts[newAssemblyName]
                        .Where(typeName => asm.MainModule.GetType(typeName) == null));
                }

                // If we're missing anything, fail the build.
                if (missing.Count > 0)
                {
                    string missingTypes = string.Join("\n", missing.ToArray());
                    throw new MeatKitBuildException(
                        "Exported objects reference scripts which do not exist in the exported assembly... Did you forget to allow a namespace?\n\nMissing types:\n" + missingTypes, null);
                }
                
                try
                {
                    // Save it
                    asm.Write(exportPath);
                }
                catch (ArgumentException e)
                {
                    throw new MeatKitBuildException("Unable to write exported scripts file. This is likely due to namespace stripping being enabled and a required namespace is not whitelisted.", e);
                }
            }

            // Delete temp file now that we're done.
            File.Delete(tempFile);
        }

        private static IEnumerable<CustomAttribute> GetAllCustomAttributes(AssemblyDefinition asm)
        {
            foreach (var type in asm.MainModule.Types)
            {
                foreach (var attrib in type.CustomAttributes) yield return attrib;
                foreach (CustomAttribute attrib in type.Methods.SelectMany(method => method.CustomAttributes))
                    yield return attrib;
            }
        }
    }
}