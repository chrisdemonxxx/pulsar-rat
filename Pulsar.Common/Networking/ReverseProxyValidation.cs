using System;

namespace Pulsar.Common.Networking
{
    public static class ReverseProxyValidation
    {
        public const int MaxTargetLength = 1024;
        public const int MaxPayloadLength = 16 * 1024 * 1024;
        public const int MaxHandshakeLength = 16 * 1024;
        public const int MinPort = 1;
        public const int MaxPort = 65535;

        public static bool IsValidId(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Length <= 128 &&
                   value.IndexOf('\0') < 0;
        }

        public static bool IsValidTarget(string target)
        {
            return !string.IsNullOrWhiteSpace(target) &&
                   target.Length <= MaxTargetLength &&
                   target.IndexOf('\0') < 0;
        }

        public static bool IsValidPort(int port)
        {
            return port >= MinPort && port <= MaxPort;
        }

        public static bool IsValidPayloadLength(int length)
        {
            return length > 0 && length <= MaxPayloadLength;
        }

        public static bool IsStaleSequence(long lastSequence, long sequence)
        {
            return sequence > 0 && lastSequence >= sequence;
        }

        public static string CreateSessionId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
