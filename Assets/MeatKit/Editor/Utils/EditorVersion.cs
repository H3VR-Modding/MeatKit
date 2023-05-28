using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MeatKit
{
    public class NativeHookFunctionOffsets
    {
        public long MonoScriptTransferWrite { get; set; }
        public long MonoScriptTransferRead { get; set; }
        public long ShutdownManaged { get; set; }
        public long StringAssign { get; set; }
    }

    public class EditorVersion
    {
        public NativeHookFunctionOffsets FunctionOffsets { get; set; }

        private static bool _hasShownPopup = false;
        
        public static bool IsSupportedVersion
        {
            get
            {
                bool supported = SupportedVersions.ContainsKey(Application.unityVersion);
                
                if (!supported && !_hasShownPopup)
                {
                    // Show the warning popup about the wrong version if is hasn't come up already.
                    string validVersion = string.Join(", ", SupportedVersions.Keys.ToArray());
                    EditorUtility.DisplayDialog("Wrong editor version",
                        "You are using Unity version " + Application.unityVersion + ", MeatKit requires one of the following: " + validVersion,
                        "I'll go install that.");
                    _hasShownPopup = true;
                }

                return supported;
            }
        }

        public static EditorVersion Current
        {
            get
            {
                EditorVersion currentVersion;
                if (SupportedVersions.TryGetValue(Application.unityVersion, out currentVersion))
                    return currentVersion;
                throw new NotSupportedException("The current editor version is not in the list of supported versions.");
            }
        }

        private static readonly Dictionary<string, EditorVersion> SupportedVersions = new Dictionary<string, EditorVersion>()
        {
            {
                "5.6.3p4", new EditorVersion
                {
                    FunctionOffsets = new NativeHookFunctionOffsets
                    {
                        MonoScriptTransferWrite = 0xE321E0,
                        MonoScriptTransferRead = 0xE34000,
                        ShutdownManaged = 0x17542D0,
                        StringAssign = 0x1480
                    }
                }
            },
            {
                "5.6.7f1", new EditorVersion
                {
                    FunctionOffsets = new NativeHookFunctionOffsets
                    {
                        MonoScriptTransferWrite = 0xE39BF0,
                        MonoScriptTransferRead = 0xE3BA10,
                        ShutdownManaged = 0x175D2C0,
                        StringAssign = 0x1480
                    }
                }
            },
        };
    }
}