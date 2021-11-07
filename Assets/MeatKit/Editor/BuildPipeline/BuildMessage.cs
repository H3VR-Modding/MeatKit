using UnityEditor;

namespace MeatKit
{
    public class BuildMessage
    {
        private BuildMessage(MessageType type, string message)
        {
            Type = type;
            Message = message;
        }

        public MessageType Type { get; private set; }
        public string Message { get; private set; }

        public static BuildMessage Info(string message)
        {
            return new BuildMessage(MessageType.Info, message);
        }

        public static BuildMessage Warning(string message)
        {
            return new BuildMessage(MessageType.Warning, message);
        }

        public static BuildMessage Error(string message)
        {
            return new BuildMessage(MessageType.Error, message);
        }
    }
}
