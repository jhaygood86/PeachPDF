using System;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PeachDrawing.Text.Internal.Fonts
{
    internal static class FontFormatConverter
    {
        // A page's @font-face rules often name the same file again and again (one rule per unicode-range or weight of a variable font), so the same bytes
        // arrive for conversion once per rule. What a WOFF or WOFF2 file converts to depends only on its bytes, so the result is kept, by content, for the next
        // rule that names it. Bounded: when the kept results pass a budget they are dropped and start again.
        private const long CacheBudget = 64L * 1024 * 1024;
        private static readonly Lock Gate = new();
        private static readonly Dictionary<(int Length, ulong Hash), (byte[] Input, byte[] Output)> Converted = [];
        private static long _convertedBytes;

        public static byte[] ToOpenType(byte[] bytes)
        {
            var isWoff = WoffConverter.IsWoff(bytes);
            if (!isWoff && !Woff2Converter.IsWoff2(bytes)) return bytes;

            var key = (bytes.Length, Hash(bytes));
            lock (Gate)
            {
                // The hash only finds the candidate; the bytes themselves decide, so a collision can never hand back another font.
                if (Converted.TryGetValue(key, out var known) && known.Input.AsSpan().SequenceEqual(bytes)) return known.Output;
            }

            var converted = isWoff ? WoffConverter.Convert(bytes) : Woff2Converter.Convert(bytes);

            lock (Gate)
            {
                // Another thread may have converted the same file while this one did: the first result stays and both callers share it.
                if (Converted.TryGetValue(key, out var raced) && raced.Input.AsSpan().SequenceEqual(bytes)) return raced.Output;

                if (_convertedBytes + converted.Length + bytes.Length > CacheBudget)
                {
                    Converted.Clear();
                    _convertedBytes = 0;
                }

                if (Converted.TryAdd(key, (bytes, converted)))
                    _convertedBytes += converted.Length + bytes.Length;
            }

            return converted;
        }

        // A 64-bit multiply-and-rotate hash over eight bytes at a time: the key only has to tell font files apart, and it is cheap enough to read every file.
        private static ulong Hash(byte[] bytes)
        {
            const ulong prime = 0x9E3779B97F4A7C15UL;
            var hash = 0xCBF29CE484222325UL ^ (ulong)bytes.Length;
            var words = MemoryMarshal.Cast<byte, ulong>(bytes.AsSpan());
            foreach (var word in words)
                hash = (((hash ^ word) * prime) << 27 | ((hash ^ word) * prime) >> 37) * 0xC2B2AE3D27D4EB4FUL;

            for (var i = words.Length * 8; i < bytes.Length; i++)
                hash = (hash ^ bytes[i]) * 0x100000001B3UL;

            return hash;
        }
    }
}
