using System;
using Mono.Cecil;
using UnityEngine;

namespace MeatKit
{
    public abstract class AssemblyModifier : ScriptableObject
    {
        [NonSerialized]
        public bool Applied = false;

        public abstract void ApplyModification(AssemblyDefinition assembly);
    }
}
