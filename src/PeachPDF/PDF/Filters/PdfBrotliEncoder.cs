using PeachDrawing.Text.Compression;

namespace PeachPDF.PDF.Filters;

/// <summary>Compresses stream data for the PDF 2.0 <c>/BrotliDecode</c> filter.</summary>
internal static class PdfBrotliEncoder
{
    /// <summary>The PDF name of the filter.</summary>
    public const string Name = "/BrotliDecode";

    public static bool IsAvailable => BrotliCompression.IsAvailable;

    /// <summary>Compresses the data, or returns <see langword="null"/> when no Brotli encoder is available.</summary>
    public static byte[]? TryEncode(byte[] data, PdfFlateEncodeMode mode = PdfFlateEncodeMode.Default)
    {
        int quality = mode switch
        {
            PdfFlateEncodeMode.BestCompression => 11,
            PdfFlateEncodeMode.BestSpeed => 1,
            _ => 6
        };

        return BrotliCompression.TryCompress(data, quality, out var compressed) ? compressed : null;
    }
}
