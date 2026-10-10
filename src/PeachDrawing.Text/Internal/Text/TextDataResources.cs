using System;
using System.IO;
using System.IO.Compression;

namespace PeachDrawing.Text.Internal.Text
{
    /// <summary>
    /// Opens a Brotli-compressed embedded resource of <see cref="Data.TextData"/> (this engine's own data package -
    /// <c>PeachDrawing.Text.Data</c>), decompressed. Every table PeachDrawing.Text reads at first use of a feature that needs it
    /// (Bidi/Script/Use/VerticalOrientation/ArabicJoining, hyphenation patterns, language tags, and the Thai/Lao/Khmer/Burmese word
    /// lists) opens its resource through <see cref="OpenBrotli"/>, so there is exactly one place that knows how a resource is found
    /// and decompressed.
    /// </summary>
    internal static class TextDataResources
    {
        /// <summary>
        /// Opens the Brotli-compressed embedded resource whose manifest name ends with <paramref name="suffix"/> (typically
        /// <c>".SomeFile.txt.br"</c> or <c>".script.dict.br"</c>), already decompressed. Returns <see langword="null"/> if no such
        /// resource exists, or if the data cannot be decompressed on this host and no custom decoder was registered (see
        /// <see cref="BrotliDecoderRegistry"/>) - a <see cref="PlatformNotSupportedException"/> the .NET runtime's own Brotli
        /// support throws on some platforms (WebAssembly, at the time of writing). The caller disposes the returned stream.
        /// </summary>
        internal static Stream? OpenBrotli(string suffix)
        {
            var raw = Data.TextData.OpenRaw(suffix);
            if (raw is null)
            {
                return null;
            }

            var custom = BrotliDecoderRegistry.Custom;
            if (custom is not null)
            {
                return custom(raw);
            }

            try
            {
                return new BrotliStream(raw, CompressionMode.Decompress);
            }
            catch (PlatformNotSupportedException)
            {
                raw.Dispose();
                return null;
            }
        }
    }

    /// <summary>
    /// The one custom Brotli decoder a host may register, and what every resource read in this project consults instead of the
    /// .NET runtime's own <see cref="BrotliStream"/> once one is set. See <see cref="PeachDrawing.Text.Compression.BrotliDecompression"/>
    /// (the public seam this backs) for the semantics a caller sees.
    /// </summary>
    internal static class BrotliDecoderRegistry
    {
        private static volatile Func<Stream, Stream>? _custom;

        /// <summary>The registered decoder, or <see langword="null"/> for the default (the .NET runtime's own Brotli support).</summary>
        internal static Func<Stream, Stream>? Custom => _custom;

        /// <summary>Registers or clears the custom decoder. Thread-safe; see the public seam's remarks for when it takes effect.</summary>
        internal static void SetDecompressor(Func<Stream, Stream>? decompressor) => _custom = decompressor;
    }

    /// <summary>
    /// The one custom Brotli encoder a host may register. See <see cref="PeachDrawing.Text.Compression.BrotliCompression"/>
    /// (the public seam this backs) for the semantics a caller sees.
    /// </summary>
    internal static class BrotliEncoderRegistry
    {
        private static volatile Func<Stream, int, Stream>? _custom;

        /// <summary>The registered encoder, or <see langword="null"/> for the default (the .NET runtime's own Brotli support).</summary>
        internal static Func<Stream, int, Stream>? Custom => _custom;

        /// <summary>Registers or clears the custom encoder. Thread-safe.</summary>
        internal static void SetCompressor(Func<Stream, int, Stream>? compressor) => _custom = compressor;
    }
}
