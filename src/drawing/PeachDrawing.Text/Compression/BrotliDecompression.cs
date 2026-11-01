using PeachDrawing.Text.Internal.Text;
using System;
using System.IO;

namespace PeachDrawing.Text.Compression
{
    /// <summary>
    /// A pluggable decoder for the Brotli-compressed data PeachDrawing.Text reads: the Unicode, hyphenation and dictionary data of
    /// its <c>PeachDrawing.Text.Data</c> dependency, and WOFF2 font data (Brotli-compressed by the WOFF2 specification itself). By
    /// default, PeachDrawing.Text decompresses with .NET's own <see cref="System.IO.Compression.BrotliStream"/>. On a host where
    /// that throws <see cref="PlatformNotSupportedException"/> - WebAssembly in a browser, at the time of writing - the affected
    /// feature quietly degrades instead of the render failing: a Unicode table comes back empty, text lays out unhyphenated, and a
    /// WOFF2 font is skipped as if it could not be read. Call <see cref="SetDecompressor"/> once, before using PeachDrawing.Text, to
    /// supply a managed Brotli implementation instead and recover that data on such a host.
    /// </summary>
    public static class BrotliDecompression
    {
        /// <summary>
        /// Registers <paramref name="decompressor"/> as the decoder PeachDrawing.Text falls back to when the .NET runtime's own
        /// Brotli support is unavailable. It receives the compressed data as a <see cref="Stream"/> and must return a
        /// <see cref="Stream"/> - most simply a <see cref="MemoryStream"/> already holding the whole decompressed result - that
        /// yields the decompressed bytes when read; PeachDrawing.Text disposes both the stream it passed in and the one returned.
        /// Pass <see langword="null"/> to go back to the default (BCL-only) behavior, which is also what happens if this is never
        /// called: every default's behavior is unchanged from before this seam existed.
        /// </summary>
        /// <remarks>
        /// Thread-safe (the last call anywhere wins), and takes effect immediately for any resource not yet read: PeachDrawing.Text
        /// caches each Unicode/hyphenation/dictionary table and each loaded font the first time it is needed and never re-reads it,
        /// so call this before the first use of a feature backed by this data - any Unicode-aware line breaking, bidirectional
        /// text, hyphenation, Thai/Lao/Khmer/Burmese word segmentation, or loading a WOFF2 font - for it to take effect there.
        /// Calling it again later replaces the decoder for anything not yet cached; it never un-caches or reloads data already read
        /// under a previous one.
        /// </remarks>
        public static void SetDecompressor(Func<Stream, Stream>? decompressor) =>
            BrotliDecoderRegistry.SetDecompressor(decompressor);
    }
}
