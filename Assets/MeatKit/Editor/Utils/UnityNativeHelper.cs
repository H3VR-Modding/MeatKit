using System;
using System.Runtime.InteropServices;

namespace MeatKit
{
    public static class UnityNativeHelper
    {
        private delegate IntPtr StringAssignType(IntPtr ptr, string str, ulong len, IntPtr nul);

        private static readonly StringAssignType AssignNativeString;
        
        static UnityNativeHelper()
        {
            if (!EditorVersion.IsSupportedVersion) return;
            
            AssignNativeString = (StringAssignType) NativeHookManager.GetDelegateForFunctionPointer<StringAssignType>(EditorVersion.Current.FunctionOffsets.StringAssign);
        }
        
        /// <summary>
        /// Reads a native string structure from unmanaged memory
        /// </summary>
        /// <param name="ptr">The pointer to the structure</param>
        /// <param name="ofs">Offset to the pointer</param>
        /// <returns>A copy of the structure in managed memory</returns>
        public static string ReadNativeString(IntPtr ptr, int ofs)
        {
            // Apply the offset 
            var real = (IntPtr) (ptr.ToInt64() + ofs);

            // Get the pointer to the string in memory
            var stringPointer = Marshal.ReadIntPtr(real);
            if (stringPointer == IntPtr.Zero)
            {
                // If the pointer is null, that means it's stored in the struct directly.
                // In this format, the string is 16 chars or less.
                var length = Marshal.ReadInt64(real, 24);
                return Marshal.PtrToStringAnsi((IntPtr) (real.ToInt64() + 8), (int) length);
            }
            else
            {
                // If it isn't null, we can just go out into that memory location and read it.
                var length = Marshal.ReadInt64(real, 24);
                return Marshal.PtrToStringAnsi(stringPointer, (int) length);
            }
        }

        /// <summary>
        /// Writes a managed string to the unmanaged memory location of a string structure
        /// </summary>
        /// <param name="ptr">The pointer to the structure in memory</param>
        /// <param name="ofs">Offset to the pointer</param>
        /// <param name="str">The string to write</param>
        public static void WriteNativeString(IntPtr ptr, int ofs, string str)
        {
            // Apply the offset and call the assign function of native Unity code
            var real = (IntPtr) (ptr.ToInt64() + ofs);
            AssignNativeString(real, str, (ulong) str.Length, IntPtr.Zero);
        }
    }
}
