using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    [InitializeOnLoad]
    public static class AssemblyResolverFix
    {
        static AssemblyResolverFix()
        {
            // This is required because Unity ships its own Mono.Cecil with the editor that is older
            // If Unity's older one is loaded first it will not automatically load our newer one, and that is going to cause problems
            // So here we _also_ load the newer one. Yes, there will be two of them loaded. That should be OK since Unity doesn't expose any of its
            // Mono.Cecil to user code so we should never be interacting with it.
            string asmPath = Path.Combine(Application.dataPath, "Managed/Mono.Cecil.dll");
            Assembly.LoadFile(asmPath);
        }
    }
}
