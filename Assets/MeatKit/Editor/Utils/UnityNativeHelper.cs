using System;
using System.Runtime.InteropServices;

namespace MeatKit
{
    public static class UnityNativeHelper
    {
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
                var length = Marshal.ReadInt64(real, 8);
                return Marshal.PtrToStringAnsi(stringPointer, (int) length);
            }
        }

        /// <summary>
        /// Writes a managed string to the unmanaged memory location of a string structure
        /// </summary>
        /// <param name="ptr">The pointer to the structure in memory</param>
        /// <param name="ofs">Offset to the pointer</param>
        /// <param name="str">The string to write</param>
        /// <returns>The pointer to the allocated memory for the string</returns>
        /// <remarks>You must free the pointer this returns when you're done with it</remarks>
        public static IntPtr WriteNativeString(IntPtr ptr, int ofs, string str)
        {
            // Apply the offset
            IntPtr real = (IntPtr) (ptr.ToInt64() + ofs);

            // Allocate unmanaged memory for the string and copy it into there
            var stringPointer = Marshal.StringToHGlobalAnsi(str);

            // Write the values into the struct
            Marshal.WriteIntPtr(real, stringPointer);
            Marshal.WriteInt64(real, 8, str.Length);
            Marshal.WriteInt64(real, 24, str.Length);

            // Return the allocated pointer which must be freed later
            return stringPointer;
        }
    }
}
