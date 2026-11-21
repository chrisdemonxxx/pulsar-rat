using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Pulsar.Common.Messages.Other;

namespace Pulsar.Common.Networking
{
    public static class MessageInputLimits
    {
        public const int DefaultMaxFrameBytes = 16 * 1024 * 1024;
        public const int DefaultMaxDecompressedBytes = 32 * 1024 * 1024;
        public const int DefaultMaxCollectionItems = 65536;
        public const int DefaultMaxStringLength = 2 * 1024 * 1024;
        public const int MaxTypeNameLength = 512;
        public const int MaxObjectDepth = 64;

        public static int MaxFrameBytes { get; } = ReadPositiveInt("PULSAR_MAX_FRAME_BYTES", DefaultMaxFrameBytes, 64 * 1024, 256 * 1024 * 1024);
        public static int MaxDecompressedBytes { get; } = ReadPositiveInt("PULSAR_MAX_DECOMPRESSED_BYTES", DefaultMaxDecompressedBytes, 64 * 1024, 256 * 1024 * 1024);
        public static int MaxCollectionItems { get; } = ReadPositiveInt("PULSAR_MAX_COLLECTION_ITEMS", DefaultMaxCollectionItems, 1, 1000000);
        public static int MaxStringLength { get; } = ReadPositiveInt("PULSAR_MAX_STRING_LENGTH", DefaultMaxStringLength, 256, 16 * 1024 * 1024);

        public static void ValidateFrameLength(int length)
        {
            if (length <= 0 || length > MaxFrameBytes)
                throw new InvalidOperationException("Invalid message frame length.");
        }

        public static bool IsValidClientId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
                return false;

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                    return false;
            }

            return true;
        }

        public static void ValidateMessage(IMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            ValidateValue(message, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        private static void ValidateValue(object value, int depth, HashSet<object> visited)
        {
            if (value == null || value.GetType().IsEnum || value is DateTime || value is DateTimeOffset ||
                value is TimeSpan || value is Guid || value is decimal || value.GetType().IsPrimitive)
                return;

            if (depth > MaxObjectDepth)
                throw new InvalidOperationException("Message object graph exceeds the maximum depth.");

            var text = value as string;
            if (text != null)
            {
                if (text.Length > MaxStringLength)
                    throw new InvalidOperationException("Message string exceeds the maximum length.");
                return;
            }

            var bytes = value as byte[];
            if (bytes != null)
            {
                if (bytes.Length > MaxFrameBytes)
                    throw new InvalidOperationException("Message byte array exceeds the maximum length.");
                return;
            }

            if (!value.GetType().IsValueType && !visited.Add(value))
                return;

            var dictionary = value as IDictionary;
            if (dictionary != null)
            {
                if (dictionary.Count > MaxCollectionItems)
                    throw new InvalidOperationException("Message dictionary exceeds the maximum item count.");
                foreach (DictionaryEntry item in dictionary)
                {
                    ValidateValue(item.Key, depth + 1, visited);
                    ValidateValue(item.Value, depth + 1, visited);
                }
                return;
            }

            var list = value as IList;
            if (list != null)
            {
                if (list.Count > MaxCollectionItems)
                    throw new InvalidOperationException("Message collection exceeds the maximum item count.");
                foreach (var item in list)
                    ValidateValue(item, depth + 1, visited);
                return;
            }

            var collection = value as IEnumerable;
            if (collection != null)
            {
                var count = 0;
                foreach (var item in collection)
                {
                    if (++count > MaxCollectionItems)
                        throw new InvalidOperationException("Message collection exceeds the maximum item count.");
                    ValidateValue(item, depth + 1, visited);
                }
                return;
            }

            foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length != 0 || !property.CanRead)
                    continue;
                ValidateValue(property.GetValue(value, null), depth + 1, visited);
            }
        }

        private static int ReadPositiveInt(string name, int fallback, int minimum, int maximum)
        {
            var text = Environment.GetEnvironmentVariable(name);
            return int.TryParse(text, out var value) && value >= minimum && value <= maximum ? value : fallback;
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
