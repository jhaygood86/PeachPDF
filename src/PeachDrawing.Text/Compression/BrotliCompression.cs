using PeachDrawing.Text.Internal.Text;
using System;
using System.IO;
using System.IO.Compression;

namespace PeachDrawing.Text.Compression
{
    /// <summary>
    /// A pluggable Brotli encoder, the counterpart of <see cref="BrotliDecompression"/>. By default PeachDrawing.Text compresses
    /// with .NET's own <see cref="BrotliEncoder"/>. On a host where that throws <see cref="PlatformNotSupportedException"/> -
    /// WebAssembly in a browser, at the time of writing - <see cref="TryCompress"/> reports failure (and <see cref="IsAvailable"/>
    /// is <see langword="false"/>) so a caller can fall back to another compression; call <see cref="SetCompressor"/> once to
    /// supply a managed Brotli encoder instead and recover Brotli output on such a host.
    /// </summary>
    public static class BrotliCompression
    {
        /// <summary>The lowest Brotli quality (fastest).</summary>
        private const int MinQuality = 0;

        /// <summary>The highest Brotli quality (smallest).</summary>
        private const int MaxQuality = 11;

        /// <summary>
        /// Registers <paramref name="compressor"/> as the encoder used in place of the .NET runtime's own Brotli support. It
        /// receives the destination <see cref="Stream"/> and the Brotli quality (0-11) and returns a writable <see cref="Stream"/>:
        /// bytes written to it are Brotli-compressed into the destination, and disposing it completes the Brotli data. It must
        /// leave the destination open; PeachDrawing.Text disposes the destination itself. Pass <see langword="null"/> to go back
        /// to the default.
        /// </summary>
        /// <remarks>Thread-safe; the last call anywhere wins, and it takes effect for the next compression.</remarks>
        public static void SetCompressor(Func<Stream, int, Stream>? compressor) =>
            BrotliEncoderRegistry.SetCompressor(compressor);

        /// <summary>
        /// Whether Brotli compression works on this host: a custom compressor is registered, or the .NET runtime's own encoder
        /// is supported.
        /// </summary>
        public static bool IsAvailable => BrotliEncoderRegistry.Custom is not null || BrotliEncoderIsSupported();

        /// <summary>
        /// Compresses <paramref name="data"/> as one Brotli stream. Returns <see langword="false"/> (and an empty array) when no
        /// encoder is available or compression failed; it does not throw for either.
        /// </summary>
        /// <param name="data">the bytes to compress</param>
        /// <param name="quality">the Brotli quality, clamped to 0-11 (11 is smallest and slowest)</param>
        /// <param name="compressed">the Brotli-compressed bytes on success</param>
        public static bool TryCompress(ReadOnlySpan<byte> data, int quality, out byte[] compressed)
        {
            quality = Math.Clamp(quality, MinQuality, MaxQuality);
            compressed = [];

            try
            {
                var custom = BrotliEncoderRegistry.Custom;
                if (custom is not null)
                {
                    using var destination = new MemoryStream();
                    using (var encoder = custom(destination, quality))
                    {
                        encoder.Write(data);
                    }

                    compressed = destination.ToArray();
                    return true;
                }

                var buffer = new byte[BrotliEncoder.GetMaxCompressedLength(data.Length)];
                if (!BrotliEncoder.TryCompress(data, buffer, out var written, quality, 22))
                {
                    return false;
                }

                compressed = buffer.AsSpan(0, written).ToArray();
                return true;
            }
            catch (PlatformNotSupportedException)
            {
                return false;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or ArgumentException)
            {
                return false;
            }
        }

        private static bool BrotliEncoderIsSupported()
        {
            try
            {
                return BrotliEncoder.TryCompress([0], new byte[BrotliEncoder.GetMaxCompressedLength(1)], out _, 0, 22);
            }
            catch (PlatformNotSupportedException)
            {
                return false;
            }
        }
    }
}
