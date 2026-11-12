using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using NStrip;
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
        public const string AssemblyName = "Assembly-CSharp";
        public const string AssemblyRename = "H3VRCode-CSharp";
        public const string AssemblyFirstpassName = "Assembly-CSharp-firstpass";
        public const string AssemblyFirstpassRename = "H3VRCode-CSharp-firstpass";

        // Array of the extra assemblies that need to come with the main Unity assemblies
        private static readonly string[] ExtraAssemblies =
        {
            "DinoFracture.dll",
            "ES2.dll"
        };

        private static void ImportAssemblies(string assembliesDirectory, string destinationDirectory)
        {
            // Remove whatever was there before and make the folder again
            if (!Directory.Exists(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);

            // Load all of our modifiers
            var editors = Extensions.GetAllInstances<AssemblyModifier>();
            foreach (var editor in editors) editor.Applied = false;

            // We need a custom assembly resolver that sometimes points to different directories.
            // IMPORTANT: The resolver must be disposed before AssetDatabase.Refresh() is called,
            // otherwise its cached AssemblyDefinitions keep FileStreams open on the DLLs and
            // Unity's AssemblyUpdater hits a sharing violation (especially on external drives).
            var resolver = new RedirectedAssemblyResolver(assembliesDirectory, destinationDirectory);
            var rParams = new ReaderParameters
            {
                AssemblyResolver = resolver
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

                // Publicize Assembly
                AssemblyStripper.MakePublic(firstpassAssembly, new string[0], false, false);

                // Remove any methods that reference UnityEditor APIs to avoid editor-only references
                RemoveEditorOnlyMethodReferences(firstpassAssembly);

                WriteSafely(firstpassAssembly, Path.Combine(destinationDirectory, AssemblyFirstpassRename + ".dll"));
                IDisposable d1 = firstpassAssembly as IDisposable;
                if (d1 != null) d1.Dispose();
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
                // Use the safe stripper to remove dependents first and validate integrity.
                try
                {
                    var stripResult = SafeAssemblyStripper.StripTypesSafely(mainAssembly, StripAssemblyTypes);
                    if (stripResult != null && stripResult.Errors.Count > 0)
                    {
                        foreach (var err in stripResult.Errors) Debug.LogWarning("SafeAssemblyStripper: " + err);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("SafeAssemblyStripper failed: " + ex.Message);
                    // Fallback: attempt original naive removal to avoid blocking import
                    foreach (var typename in StripAssemblyTypes)
                    {
                        var type = mainAssembly.MainModule.GetType(typename);
                        if (type != null) mainAssembly.MainModule.Types.Remove(type);
                    }
                }

                // Apply modifications
                foreach (var editor in editors) editor.ApplyModification(mainAssembly);

                // Publicize assembly
                AssemblyStripper.MakePublic(mainAssembly, new string[0], false, false);

                // Apply help URLs
                ApplyWikiHelpAttribute(mainAssembly);

                //  Make Alloy.EnumExtension internal and rename it to something else to prevent a conflict.
                TypeDefinition alloyEnumExtension = mainAssembly.MainModule.GetType("Alloy.EnumExtension");
                alloyEnumExtension.IsPublic = false;

                // Remove any methods that reference UnityEditor APIs to avoid editor-only references
                RemoveEditorOnlyMethodReferences(mainAssembly);

                // Write the main assembly out into the destination folder and dispose it
                WriteSafely(mainAssembly, Path.Combine(destinationDirectory, AssemblyRename + ".dll"));
                IDisposable d2 = mainAssembly as IDisposable;
                if (d2 != null) d2.Dispose();
            }

            // Dispose the resolver BEFORE copying extra assemblies or calling AssetDatabase.Refresh.
            // Cecil's DefaultAssemblyResolver caches resolved AssemblyDefinitions with open FileStreams;
            // if we don't dispose before Refresh, Unity's AssemblyUpdater gets a sharing violation.
            resolver.Dispose();

            // Then lastly copy the other assemblies to the destination folder
            foreach (var file in ExtraAssemblies)
            {
                var path = Path.Combine(assembliesDirectory, file);
                if (File.Exists(path))
                    ImportSingleAssembly(path, destinationDirectory);
            }

            ValidateNoProjectAssemblyReferences(destinationDirectory);

            // Check if anything didn't apply
            foreach (var editor in editors)
                if (!editor.Applied)
                    Debug.LogWarning(editor.name + " was not applied while importing.", editor);

            // When we're done importing assemblies, let Unity refresh the asset database
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, "H3VR_IMPORTED");
            NormalizeMetaFileGUIDs();
            EnsureMcsRsp();
        }

        internal static int ValidateNoProjectAssemblyReferences(string managedDirectory)
        {
            int problems = 0;
            foreach (var path in Directory.GetFiles(managedDirectory, "*.dll"))
            {
                ModuleDefinition module = null;
                try
                {
                    module = ModuleDefinition.ReadModule(path);
                    foreach (var reference in module.AssemblyReferences)
                    {
                        string n = reference.Name;
                        if (n == "Assembly-CSharp" || n == "Assembly-CSharp-firstpass" ||
                            n == "Assembly-CSharp-Editor" || n == "Assembly-CSharp-Editor-firstpass")
                        {
                            problems++;
                            Debug.LogError("[MeatKit] " + Path.GetFileName(path) + " references the project assembly '" + n +
                                           "'. This creates an assembly reference cycle that Mono cannot unload, which " +
                                           "duplicates assemblies after script reloads. Remove the reference from the imported DLL.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[MeatKit] Could not inspect " + Path.GetFileName(path) + " for project-assembly references: " + ex.Message);
                }
                finally
                {
                    if (module != null) module.Dispose();
                }
            }
            return problems;
        }

        // Attempts to write the Cecil assembly directly to destPath.  If the destination is
        // locked (Unity's child domain has the DLL memory-mapped), stages the output in
        // Library/PendingDllImports/ and temporarily disables the plugin so the next domain
        // reload releases the lock.  ManagedPluginDomainFix.ApplyPendingDllImports() finishes
        // the copy once the domain reload has freed the file handle.
        private static void WriteSafely(AssemblyDefinition assembly, string destPath)
        {
            try
            {
                assembly.Write(destPath);
                return;
            }
            catch (IOException)
            {
                // Fall through to staging path.
            }

            string projectDir = Path.Combine(Application.dataPath, "..");
            string pendingDir = Path.GetFullPath(Path.Combine(Path.Combine(projectDir, "Library"), "PendingDllImports"));

            if (!Directory.Exists(pendingDir))
                Directory.CreateDirectory(pendingDir);

            string pendingPath = Path.Combine(pendingDir, Path.GetFileName(destPath));
            assembly.Write(pendingPath);

            ManagedPluginDomainFix.StageForPendingImport(pendingPath, destPath);
            Debug.Log("[MeatKit] DLL locked by Unity domain; staged to " + pendingPath + ". Will apply after domain reload.");
        }

        private static void RemoveEditorOnlyMethodReferences(AssemblyDefinition asm)
        {
            var methodsToRemove = new System.Collections.Generic.List<MethodDefinition>();

            foreach (var type in asm.MainModule.Types)
            {
                foreach (var method in type.Methods.ToList())
                {
                    bool remove = false;

                    // Check method custom attributes
                    foreach (var ca in method.CustomAttributes)
                    {
                        var asmRef = ca.AttributeType.Scope as AssemblyNameReference;
                        if (asmRef != null && asmRef.Name == "UnityEditor")
                        {
                            remove = true;
                            break;
                        }
                    }

                    if (remove)
                    {
                        methodsToRemove.Add(method);
                        continue;
                    }

                    if (!method.HasBody) continue;

                    foreach (var instr in method.Body.Instructions)
                    {
                        var memRef = instr.Operand as MemberReference;
                        if (memRef == null) continue;

                        IMetadataScope scope = null;
                        var typeRef = memRef as TypeReference;
                        if (typeRef != null) scope = typeRef.Scope;
                        else if (memRef.DeclaringType != null) scope = memRef.DeclaringType.Scope;

                        var asmRef2 = scope as AssemblyNameReference;
                        if (asmRef2 != null && asmRef2.Name == "UnityEditor")
                        {
                            methodsToRemove.Add(method);
                            break;
                        }
                    }
                }
            }

            foreach (var m in methodsToRemove)
            {
                try
                {
                    m.DeclaringType.Methods.Remove(m);
                    Debug.Log("Removed editor-only method during import: " + m.FullName);
                }
                catch
                {
                    // swallow - removal is best-effort
                }
            }
        }

        private static void ImportSingleAssembly(string assemblyPath, string destinationDirectory)
        {
            var resolver = new RedirectedAssemblyResolver(Path.GetDirectoryName(assemblyPath), destinationDirectory);
            var rParams = new ReaderParameters
            {
                AssemblyResolver = resolver
            };

            try
            {
                // If this assembly uses the Assembly-CSharp name at all for any reason, replace it with H3VRCode-CSharp
                // This would probably only be done on MonoMod patches but is required to make Unity shut up
                var asm = AssemblyDefinition.ReadAssembly(assemblyPath, rParams);
                try
                {
                    string name = asm.Name.Name;
                    if (name.Contains("Assembly-CSharp"))
                    {
                        name = name.Replace("Assembly-CSharp", "H3VRCode-CSharp");
                        asm.Name = new AssemblyNameDefinition(name, asm.Name.Version);
                        asm.MainModule.Name = name + ".dll";
                    }

                    // Replace all occurrences to references of Assembly-CSharp with H3VRCode-CSharp
                    bool referencesMmhook = false;
                    foreach (var reference in asm.MainModule.AssemblyReferences)
                    {
                        string refName = reference.Name;

                        if (refName == "Assembly-CSharp" || refName == "Assembly-CSharp-firstpass")
                        {
                            reference.Name = refName.Replace("Assembly-CSharp", "H3VRCode-CSharp");
                        }
                        else if (refName.Contains("MMHOOK"))
                        {
                            referencesMmhook = true;
                        }
                    }

                    // If we detected this library references MMHOOK, confirm with the user if we should ocontinue.
                    if (referencesMmhook)
                    {
                        bool shouldContinue = EditorUtility.DisplayDialog("Warning", "The selected library appears to reference MMHOOK. If you don't know what this means, do not continue with the import as it will likely result in instability and crashes in your project. Ask the author of the library for a version that does not reference MMHOOK.", "Continue", "Cancel");
                        if (!shouldContinue) return;
                    }

                    asm.Write(Path.Combine(destinationDirectory, asm.MainModule.Name));
                }
                finally
                {
                    IDisposable d = asm as IDisposable;
                    if (d != null) d.Dispose();
                }
            }
            finally
            {
                // Must not be skipped: a leaked resolver holds FileStreams and makes
                // Unity's AssemblyUpdater hit a sharing violation.
                resolver.Dispose();
            }
        }

        private static void ApplyWikiHelpAttribute(AssemblyDefinition asm)
        {
            // For convenience, we can add the Unity HelpURL attribute to the components from the game assembly.
            // We'll point the url at the wiki and just append the full type name at the end 

            // Iterate over every type in the assembly and just stick the attribute on it
            // Probably doesn't matter if types that don't need it have it.
            foreach (var type in asm.MainModule.Types)
            {
                // If the type doesn't already have this attribute, add it.
                if (type.CustomAttributes.Any(a => a.AttributeType.Name == "HelpURLAttribute")) continue;

                // Append 'Global.' to the type name if it's in the global namespace. This is a workaround for docfx.
                string typeName = string.IsNullOrEmpty(type.Namespace) ? ("Global." + type.FullName) : type.FullName;
                string helpUrl = "https://h3vr-modding.github.io/docs/api/" + typeName + ".html";

                var str = asm.MainModule.TypeSystem.String;
                var attributeConstructor = typeof(HelpURLAttribute).GetConstructor(new[] { typeof(string) });
                var attributeRef = asm.MainModule.ImportReference(attributeConstructor);
                var attribute = new CustomAttribute(attributeRef);
                attribute.ConstructorArguments.Add(new CustomAttributeArgument(str, helpUrl));
                type.CustomAttributes.Add(attribute);
            }
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

            // Unity auto-generates .meta files with Editor: enabled: 0 (default).
            // The compiler needs these DLLs as editor references, so fix it to enabled: 1.
            // The pattern targets the "enabled: 0" line immediately after "Editor: Editor".
            var editorEnabledFix = new Regex(
                @"(Editor: Editor\s*\n\s*second:\s*\n\s*)enabled: 0",
                RegexOptions.Multiline);

            foreach (var metaFile in Directory.GetFiles(ManagedDirectory, "*.meta"))
            {
                // First we get the hash
                var assemblyName = Path.GetFileName(metaFile.Substring(0, metaFile.Length - 5));
                var hash = hashFunction.ComputeHash(Encoding.UTF8.GetBytes(assemblyName));
                var hexHash = Extensions.ByteArrayToString(hash).ToLower();

                // Then we need to replace the hash in the meta file with it.
                var metaText = File.ReadAllText(metaFile);
                metaText = replaceWith.Replace(metaText, "guid: " + hexHash);

                // Enable Editor platform so GetCompatibleWithEditorOrAnyPlatform includes the DLL.
                metaText = editorEnabledFix.Replace(metaText, "${1}enabled: 1");

                File.WriteAllText(metaFile, metaText);
            }

            // If anything was changed we need Unity to apply it immediately.
            AssetDatabase.Refresh();
        }

        /// <summary>
        ///     Creates or updates Assets/mcs.rsp so the C# compiler always receives -r: references
        ///     to H3VRCode and other managed DLLs.  This is the belt-and-suspenders fallback for
        ///     editor compilation: even if .meta platform settings are wrong, the compiler still
        ///     finds the types.  NativeHookManager's ACSC hook cannot help because Unity's
        ///     GetCompatibleWithEditorOrAnyPlatform filters DLLs before ACSC ever runs.
        /// </summary>
        private static void EnsureMcsRsp()
        {
            string rspPath = Path.Combine(Application.dataPath, "mcs.rsp");
            var sb = new StringBuilder();
            sb.AppendLine("-r:Assets/Managed/" + AssemblyFirstpassRename + ".dll");
            sb.AppendLine("-r:Assets/Managed/" + AssemblyRename + ".dll");

            // Include common modding dependencies when present.
            string[] extras = new string[] { "BepInEx.dll", "0Harmony.dll" };
            foreach (string dll in extras)
            {
                if (File.Exists(Path.Combine(ManagedDirectory, dll)))
                    sb.AppendLine("-r:Assets/Managed/" + dll);
            }

            string desired = sb.ToString();

            // Only write if content differs to avoid triggering a recompile.
            if (File.Exists(rspPath))
            {
                string existing = File.ReadAllText(rspPath);
                if (existing == desired) return;
            }

            File.WriteAllText(rspPath, desired);
            Debug.Log("[MeatKit] Created Assets/mcs.rsp with compiler references.");
        }

        private class RedirectedAssemblyResolver : BaseAssemblyResolver
        {
            private readonly DefaultAssemblyResolver _defaultResolver = new DefaultAssemblyResolver();
            private readonly string[] _redirectPaths;
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                IDisposable d = _defaultResolver as IDisposable;
                if (d != null) d.Dispose();
            }

            public RedirectedAssemblyResolver(params string[] redirectPath)
            {
                    _redirectPaths = redirectPath;
                    // Ensure the default resolver knows about our redirect search paths so it can
                    // resolve assemblies by identity (not just filename). This lets it find
                    // UnityEngine (and core modules) even when the physical DLL filename
                    // doesn't match the simple assembly name.
                    foreach (var p in _redirectPaths)
                    {
                        try
                        {
                            _defaultResolver.AddSearchDirectory(p);
                        }
                        catch
                        {
                            // Ignore any issues adding search directories; fallback logic will still try file names.
                        }
                    }
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
                                    new ReaderParameters { AssemblyResolver = this });
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
