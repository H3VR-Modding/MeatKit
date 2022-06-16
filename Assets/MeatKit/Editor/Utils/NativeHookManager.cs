using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using UnityEditor;

namespace MeatKit
{
    /// <summary>
    /// This class helps manage detours into the native code of the Editor.
    /// Any detours into native code should be registered using this as it will automatically dispose of them
    /// right before the Editor reloads the mono domain, preventing editor crashes.
    /// </summary>
    [InitializeOnLoad]
    public static class NativeHookManager
    {
        // Actual name: ShutdownPlatformSupportModulesInManaged(void)
        private delegate void ShutdownManaged();

        private static readonly ShutdownManaged OrigShutdownManaged;

        // Keep track of all the applied detours so we can quickly undo them before the mono domain is reloaded
        private static readonly List<NativeDetour> Detours = new List<NativeDetour>();

        static NativeHookManager()
        {
            // Apply our detours here and save the trampoline to call the original function
            OrigShutdownManaged = ApplyEditorDetour<ShutdownManaged>(0x17542D0, new ShutdownManaged(OnShutdownManaged));
        }

        public static T ApplyEditorDetour<T>(long from, Delegate to) where T : class
        {
            // Get the base address of the Unity module and the address in memory of the function
            IntPtr editorBase = DynDll.OpenLibrary("Unity.exe");
            IntPtr fromPtr = (IntPtr) (editorBase.ToInt64() + from);

            // Get a function pointer for the managed callback
            var toPtr = Marshal.GetFunctionPointerForDelegate(to);

            // Make a detour and add it to the list
            var detour = new NativeDetour(fromPtr, toPtr, new NativeDetourConfig {ManualApply = true});
            Detours.Add(detour);

            // Apply the detour and generate a trampoline for it, which we return
            var original = detour.GenerateTrampoline(to.GetType().GetMethod("Invoke")).CreateDelegate(typeof(T)) as T;
            detour.Apply();
            return original;
        }

        private static void OnShutdownManaged()
        {
            // Unity is about to shutdown the mono runtime! Quickly dispose of our detours!
            OrigShutdownManaged();
            foreach (var detour in Detours) detour.Dispose();
        }
    }
}
