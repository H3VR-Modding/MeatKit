using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace MeatKit
{
    public static partial class MeatKit
    {
        private const string EditorAssemblyPath = "Library/ScriptAssemblies/";

        private static void ExportEditorAssembly(string folder, string tempFile = null,
            Dictionary<string, List<string>> requiredScripts = null)
        {
            // Make a copy of the file if we aren't already given one
            string editorAssembly = EditorAssemblyPath + AssemblyName + ".dll";
            if (!File.Exists(editorAssembly) && !File.Exists(tempFile))
            {
                throw new MeatKitBuildException("Editor assembly missing! Can't export scripts.");
            }
            if (string.IsNullOrEmpty(tempFile))
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
                    new RedirectedAssemblyResolver(Path.GetDirectoryName(typeof(UnityEngine.Object).Assembly.Location), ManagedDirectory)
            };

            // Get the MeatKitPlugin class and rename it
            using (var asm = AssemblyDefinition.ReadAssembly(tempFile, rParams))
            {
                // Locate the plugin class for this profile and set it's name and namespace
                string mainNamespace = BuildWindow.SelectedProfile.MainNamespace;
                var plugin = FindPluginClass(asm.MainModule, mainNamespace);
                BuildLog.WriteLine("Using plugin class " + plugin.FullName);
                plugin.Namespace = mainNamespace;
                plugin.Name = settings.PackageName + "Plugin";

                // Watermark the plugin just in case it's useful to someone
                BuildLog.WriteLine("Watermarking plugin class");
                var str = asm.MainModule.TypeSystem.String;
                var descriptionAttributeConstructor = typeof(DescriptionAttribute).GetConstructor(new[] {typeof(string)});
                var descriptionAttributeRef = asm.MainModule.ImportReference(descriptionAttributeConstructor);
                var descriptionAttribute = new CustomAttribute(descriptionAttributeRef);
                descriptionAttribute.ConstructorArguments.Add(new CustomAttributeArgument(str, "Built with MeatKit"));
                plugin.CustomAttributes.Add(descriptionAttribute);

                // This is some quantum bullshit.
                // If you don't enumerate the constructor arguments for attributes their values aren't updated correctly. 
                BuildLog.WriteLine("Performing quantum bullshit");
                foreach (var x in GetAllCustomAttributes(asm).SelectMany(a => a.ConstructorArguments))
                {
                }

                // Get the BepInPlugin attribute and replace the values in it with our own
                BuildLog.WriteLine("Applying BepInPlugin attribute params");
                var guid = settings.Author + "." + settings.PackageName;
                var pluginAttribute = plugin.CustomAttributes.First(a => a.AttributeType.Name == "BepInPlugin");
                pluginAttribute.ConstructorArguments[0] = new CustomAttributeArgument(str, guid);
                pluginAttribute.ConstructorArguments[1] = new CustomAttributeArgument(str, settings.PackageName);
                pluginAttribute.ConstructorArguments[2] = new CustomAttributeArgument(str, settings.Version);

                // Get the LoadAssets method and make a new body for it
                BuildLog.WriteLine("Generating LoadAssets()");
                var loadAssetsMethod = plugin.Methods.First(m => m.Name == "LoadAssets");
                loadAssetsMethod.Body = new MethodBody(loadAssetsMethod);
                var il = loadAssetsMethod.Body.GetILProcessor();

                // If we're automatically applying Harmony patches, do that now
                // This IL translates to Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), "PluginGuid");
                if (settings.ApplyHarmonyPatches)
                {
                    BuildLog.WriteLine("  Code to apply harmony patches");
                    var assemblyGetExecutingAssembly = typeof(Assembly).GetMethod("GetExecutingAssembly");
                    var harmonyCreateAndPatchALl = typeof(Harmony).GetMethod("CreateAndPatchAll", new[] {typeof(Assembly), typeof(string)});
                    il.Emit(OpCodes.Call, plugin.Module.ImportReference(assemblyGetExecutingAssembly));
                    il.Emit(OpCodes.Ldstr, guid);
                    il.Emit(OpCodes.Call, plugin.Module.ImportReference(harmonyCreateAndPatchALl));
                    il.Emit(OpCodes.Pop);
                }
                
                // Let any build items insert their own code in here
                foreach (var item in settings.BuildItems)
                {
                    BuildLog.WriteLine("  " + item);
                    item.GenerateLoadAssets(plugin, il);
                }

                // Insert a ret at the end so it's valid
                il.Emit(OpCodes.Ret);

                // Module name needs to be changed away from Assembly-CSharp.dll because it is a reserved name.
                string newAssemblyName = settings.PackageName + ".dll";
                BuildLog.WriteLine("Renaming assembly (" + newAssemblyName + ")");
                asm.Name = new AssemblyNameDefinition(settings.PackageName, asm.Name.Version);
                asm.MainModule.Name = newAssemblyName;

                // References to renamed unity code must be swapped out.
                BuildLog.WriteLine("Renaming assembly references");
                foreach (var ii in asm.MainModule.AssemblyReferences)
                {
                    // Rename any references to the game's code
                    if (ii.Name.Contains("H3VRCode-CSharp"))
                    {
                        var newReference = ii.Name.Replace("H3VRCode-CSharp", "Assembly-CSharp");
                        BuildLog.WriteLine("  " + ii.Name + " -> " + newReference);
                        ii.Name = newReference;
                    }

                    // And also if we're referencing a MonoMod DLL, we need to fix reference too
                    if (ii.Name.EndsWith(".mm"))
                    {
                        // What the name currently is:
                        //    Assembly-CSharp.PatchName.mm
                        // What we want:
                        //    Assembly-CSharp
                        // So just lop off anything past the second to last dot
                        int idx = ii.Name.LastIndexOf('.', ii.Name.Length - 4);
                        var newReference = ii.Name.Substring(0, idx);
                        BuildLog.WriteLine("  " + ii.Name + " -> " + newReference);
                        ii.Name = newReference;
                    }
                }

                if (BuildWindow.SelectedProfile.StripNamespaces)
                {
                    // Remove types not in an allowed namespace or the global namespace
                    BuildLog.WriteLine("Stripping namespaces");
                    string[] allowedNamespaces = BuildWindow.SelectedProfile.GetAllAllowedNamespaces();
                    List<TypeDefinition> typesToRemove = new List<TypeDefinition>();
                    foreach (var type in asm.MainModule.Types)
                    {
                        if (type.Namespace == "" || allowedNamespaces.Any(x => type.Namespace.Contains(x)))
                            continue;
                        BuildLog.WriteLine("  " + type.FullName);
                        typesToRemove.Add(type);
                    }

                    foreach (var type in typesToRemove) asm.MainModule.Types.Remove(type);
                }

                // Remove the same types we didn't want to import. This cannot be skipped.
                foreach (var type in StripAssemblyTypes
                             .Select(x => asm.MainModule.GetType(x))
                             .Where(x => x != null)) asm.MainModule.Types.Remove(type);

                // Check if we're now missing any scripts from the export
                BuildLog.WriteLine("Checking for missing types");
                List<string> missing = new List<string>();
                string originalAssemblyName = AssemblyName + ".dll";
                if (requiredScripts != null && requiredScripts.ContainsKey(originalAssemblyName))
                {
                    missing.AddRange(requiredScripts[originalAssemblyName]
                        .Where(typeName => !StripAssemblyTypes.Contains(typeName) && asm.MainModule.GetType(typeName) == null));
                }

                // If we're missing anything, fail the build.
                if (missing.Count > 0)
                {
                    string missingTypes = string.Join("\n", missing.ToArray());
                    throw new MeatKitBuildException(
                        "Exported objects reference scripts which do not exist in the exported assembly... Did you forget to allow a namespace?\n\nMissing types:\n" +
                        missingTypes, null);
                }

                try
                {
                    // Save it
                    asm.Write(exportPath);
                }
                catch (ArgumentException e)
                {
                    throw new MeatKitBuildException(
                        "Unable to write exported scripts file. This is likely due to namespace stripping being enabled and a required namespace is not whitelisted.",
                        e);
                }
            }

            // Delete temp file now that we're done.
            File.Delete(tempFile);
        }

        private static TypeDefinition FindPluginClass(ModuleDefinition module, string mainNamespace)
        {
            // Get the default MeatKitPlugin class from the module
            var pluginClass = module.GetType("MeatKitPlugin");

            // Try and locate any alternative plugin classes
            foreach (var type in module.Types)
            {
                // We're looking for types that extend the BaseUnityPlugin class and is in the main namespace of our mod
                if (type.IsSubtypeOf(typeof(BaseUnityPlugin)) && type.Namespace == mainNamespace)
                {
                    pluginClass = type;
                    break;
                }
            }
            
            return pluginClass;
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
