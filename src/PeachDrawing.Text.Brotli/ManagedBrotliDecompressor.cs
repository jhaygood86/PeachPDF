using System;
using System.IO;
using PeachDrawing.Text.Brotli.Internal;
using PeachDrawing.Text.Compression;

namespace PeachDrawing.Text.Brotli
{
    /// <summary>
    /// A pure-managed Brotli decoder for hosts where <see cref="System.IO.Compression.BrotliStream"/> throws
    /// <see cref="PlatformNotSupportedException"/> - WebAssembly in a browser, at the time of writing. Register it once, before
    /// using PeachDrawing.Text, with <see cref="Register"/>.
    /// </summary>
    /// <remarks>
    /// Ported from google/brotli's own C# decoder (<c>csharp/org/brotli/dec/</c>), MIT-licensed; see this project's
    /// <c>THIRD-PARTY-LICENSES.md</c> and <c>PORTING-NOTES.md</c> for the exact source, retrieval date and what changed (only the
    /// namespace and nullability annotations, plus two bug fixes - the decoding logic is otherwise unmodified). It implements the general Brotli format
    /// (RFC 7932) the reference decoder does, not a cut-down subset: quality level and window size are choices an *encoder*
    /// makes, and this repository's own generator scripts compress at quality 11 (fontTools' WOFF2 writer does too), which uses
    /// the format's full feature set - a decoder has to handle whatever a compliant encoder produced, so there was no smaller
    /// "subset" to scope this to. See <c>PORTING-NOTES.md</c> for what was verified and how.
    /// </remarks>
    public static class ManagedBrotliDecompressor
    {
        /// <summary>
        /// Decompresses <paramref name="compressed"/> as a raw Brotli stream. The returned <see cref="Stream"/> decodes lazily as
        /// it is read; disposing it also disposes <paramref name="compressed"/>. Throws if the data is not valid Brotli - never
        /// silently returns truncated or wrong output.
        /// </summary>
        public static Stream Decompress(Stream compressed)
        {
            ArgumentNullException.ThrowIfNull(compressed);
            return new BrotliInputStream(compressed);
        }

        /// <summary>
        /// Registers this decoder with <see cref="BrotliDecompression.SetDecompressor"/>, so PeachDrawing.Text falls back to it
        /// wherever the .NET runtime's own Brotli support is unavailable. Safe to call unconditionally on any host - it only ever
        /// takes effect where the BCL's own decoder would otherwise throw <see cref="PlatformNotSupportedException"/> (see that
        /// method's own remarks for exactly when).
        /// </summary>
        public static void Register() => BrotliDecompression.SetDecompressor(Decompress);

        /// <summary>Reverts <see cref="Register"/>, going back to the default (BCL-only) behavior.</summary>
        public static void Unregister() => BrotliDecompression.SetDecompressor(null);
    }
}
