using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MeatKit
{
    public static partial class MeatKit
    {
        private const string EditorAssemblyPath = "Library/ScriptAssemblies/";
        public const string BundleOutputPath = "AssetBundles/";

        private static void ExportEditorAssembly(string folder)
        {
            if (!File.Exists(EditorAssemblyPath + AssemblyName + ".dll")) return;

            // Delete the old file
            var settings = BuildWindow.SelectedProfile;
            var exportPath = folder + settings.PackageName + ".dll";
            if (File.Exists(exportPath)) File.Delete(exportPath);

            // Get the MeatKitPlugin class and rename it
            string tempFile = Path.GetTempFileName();
            File.Copy(EditorAssemblyPath + AssemblyName + ".dll", tempFile, true);
            using (var asm = AssemblyDefinition.ReadAssembly(tempFile))
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
                asm.Name = new AssemblyNameDefinition(settings.PackageName, asm.Name.Version);
                asm.MainModule.Name = settings.PackageName + ".dll";

                // References to renamed unity code must be swapped out.
                foreach (var ii in asm.MainModule.AssemblyReferences)
                    switch (ii.Name)
                    {
                        case AssemblyRename:
                            ii.Name = AssemblyName;
                            break;
                        case AssemblyFirstpassRename:
                            ii.Name = AssemblyFirstpassName;
                            break;
                    }

                // Remove the same types we didn't want to import
                foreach (var type in StripAssemblyTypes
                             .Select(x => asm.MainModule.GetType(x))
                             .Where(x => x != null))
                    asm.MainModule.Types.Remove(type);

                // Save it
                asm.Write(exportPath);
            }

            // Delete temp file now that we're done.
            File.Delete(tempFile);
        }

        private static IEnumerable<CustomAttribute> GetAllCustomAttributes(AssemblyDefinition asm)
        {
            foreach (var type in asm.MainModule.Types)
            {
                foreach (var attrib in type.CustomAttributes) yield return attrib;
                foreach (CustomAttribute attrib in type.Methods.SelectMany(method => method.CustomAttributes)) yield return attrib;
            }
        }
    }
}