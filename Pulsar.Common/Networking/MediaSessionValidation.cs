using System;

namespace Pulsar.Common.Networking
{
    public static class MediaSessionValidation
    {
        public const int MinBitrate = 1000;
        public const int MaxBitrate = 192000;
        public const int MinQuality = 0;
        public const int MaxQuality = 100;
        public const int MinFrameBatch = 1;
        public const int MaxFrameBatch = 12;

        public static bool IsValidBitrate(int bitrate)
        {
            return bitrate >= MinBitrate && bitrate <= MaxBitrate;
        }

        public static bool IsValidQuality(int quality)
        {
            return quality >= MinQuality && quality <= MaxQuality;
        }

        public static bool IsValidFrameBatch(int frameBatch)
        {
            return frameBatch >= MinFrameBatch && frameBatch <= MaxFrameBatch;
        }

        public static int NormalizeFrameBatch(int frameBatch)
        {
            return Math.Max(MinFrameBatch, Math.Min(frameBatch, MaxFrameBatch));
        }

        public static bool IsStaleSequence(long lastSequence, long sequence)
        {
            return sequence > 0 && lastSequence >= sequence;
        }
    }
}
