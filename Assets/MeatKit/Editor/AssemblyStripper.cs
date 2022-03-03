/*
 * This code is from BepInEx's NStrip library, licensed under MIT
 * https://github.com/BepInEx/NStrip/blob/f1e9887b3eb77c0e02acb5919b5e26a6e7c2c342/NStrip/AssemblyStripper.cs
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace NStrip
{
	public static class AssemblyStripper
	{
		static IEnumerable<TypeDefinition> GetAllTypeDefinitions(AssemblyDefinition assembly)
		{
			var typeQueue = new Queue<TypeDefinition>(assembly.MainModule.Types);

			while (typeQueue.Count > 0)
			{
				var type = typeQueue.Dequeue();

				yield return type;

				foreach (var nestedType in type.NestedTypes)
					typeQueue.Enqueue(nestedType);
			}
		}

		private static bool CheckCompilerGeneratedAttribute(IMemberDefinition member)
		{
			return member.CustomAttributes.Any(x =>
				x.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
		}
		
		public static void MakePublic(AssemblyDefinition assembly, IList<string> typeNameBlacklist, bool includeCompilerGenerated, bool excludeCgEvents)
		{

			foreach (var type in GetAllTypeDefinitions(assembly))
			{
				if (typeNameBlacklist.Contains(type.Name))
					continue;

				if (!includeCompilerGenerated && CheckCompilerGeneratedAttribute(type))
					continue;

				if (type.IsNested)
					type.IsNestedPublic = true;
				else
					type.IsPublic = true;

				foreach (var method in type.Methods)
				{
					if (!includeCompilerGenerated &&
					    (CheckCompilerGeneratedAttribute(method) || method.IsCompilerControlled))
						continue;

					method.IsPublic = true;
				}

				foreach (var field in type.Fields)
				{
					if (!includeCompilerGenerated &&
					    (CheckCompilerGeneratedAttribute(field) || field.IsCompilerControlled))
						continue;

					if (includeCompilerGenerated && excludeCgEvents)
					{
						if (type.Events.Any(x => x.Name == field.Name))
							continue;
					}

					if (field.IsPublic) continue;
					
					field.IsPublic = true;
					var attributes = field.CustomAttributes;
					CustomAttribute isExplicitlySerialized = attributes.FirstOrDefault(x => x.AttributeType.Name == "SerializeField");

					if (isExplicitlySerialized == null)
					{
						var constructor = typeof(NonSerializedAttribute).GetConstructor(Type.EmptyTypes);
						var reference = assembly.MainModule.ImportReference(constructor);
						field.CustomAttributes.Add(new CustomAttribute(reference));
					}
				}
			}
		}
	}
}