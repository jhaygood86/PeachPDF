using PeachDrawing.Text.Compression;

namespace PeachPDF.PdfSharpCore.Pdf.Filters
{
    /// <summary>
    /// Implements the <c>/BrotliDecode</c> filter (PDF 2.0) encoder over PeachDrawing.Text's Brotli seam
    /// (<see cref="BrotliCompression"/>): .NET's own encoder by default, or the managed one a host registers.
    /// </summary>
    internal class BrotliDecode : Filter
    {
        /// <summary>The filter name written to a stream's <c>/Filter</c> entry.</summary>
        public const string Name = "/BrotliDecode";

        /// <summary>Whether Brotli compression works on this host.</summary>
        public static bool IsAvailable => BrotliCompression.IsAvailable;

        /// <inheritdoc/>
        public override byte[] Encode(byte[] data) => Encode(data, PdfFlateEncodeMode.Default) ?? data;

        /// <summary>
        /// Encodes <paramref name="data"/>, or returns <see langword="null"/> when no Brotli encoder is available so the caller
        /// can fall back to <see cref="FlateDecode"/>.
        /// </summary>
        public byte[]? Encode(byte[] data, PdfFlateEncodeMode mode)
        {
            // Same levels as MuPDF's writer (FZ_BROTLI_BEST_SPEED / DEFAULT / BEST). Measured on 13-22 MB system fonts, quality 6
            // costs 0.2-0.4 s where 9 costs 1.6-2.9 s and 11 costs 23-30 s, for 1-4% and 8-13% smaller output respectively.
            int quality = mode switch
            {
                PdfFlateEncodeMode.BestCompression => 11,
                PdfFlateEncodeMode.BestSpeed => 1,
                _ => 6
            };

            return BrotliCompression.TryCompress(data, quality, out var compressed) ? compressed : null;
        }
    }
}
