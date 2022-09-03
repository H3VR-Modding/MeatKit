using System;

namespace MeatKit
{
    public class MeatKitBuildException : Exception
    {
        public MeatKitBuildException(string message) : base(message)
        {
        }

        public MeatKitBuildException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}