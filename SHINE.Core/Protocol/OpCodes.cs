using System.Dynamic;

namespace SHINE.Core
{

    public static class OpCodes
    {
        public const int Ping = 1;
        public const int Connect = 2;
        public const int Disconnect = 3;
        public const int ChatMessage = 4;
        public const int MovePlayer = 5;
        public const int VoiceData = 6;
        public const int VideoFrame = 7;

        public const int CustomCodes = 1000;

    }

}
