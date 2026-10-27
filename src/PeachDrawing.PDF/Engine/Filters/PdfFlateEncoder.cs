using System;
using System.IO;
using System.IO.Compression;

namespace PeachDrawing.PDF.Engine.Filters;

/// <summary>Compresses stream data for the PDF <c>/FlateDecode</c> filter (zlib).</summary>
internal static class PdfFlateEncoder
{
    /// <summary>The PDF name of the filter.</summary>
    public const string Name = "/FlateDecode";

    public static byte[] Encode(byte[] data, PdfFlateEncodeMode mode = PdfFlateEncodeMode.Default)
    {
        CompressionLevel compressionLevel = mode switch
        {
            PdfFlateEncodeMode.BestCompression => CompressionLevel.SmallestSize,
            PdfFlateEncodeMode.BestSpeed => CompressionLevel.Fastest,
            _ => CompressionLevel.Optimal
        };

        // Deflate output is rarely larger than the input and usually far smaller; half of it avoids most of the buffer's doublings.
        var output = new MemoryStream(Math.Max(64, data.Length / 2));
        using (var zlib = new ZLibStream(output, compressionLevel, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return output.ToArray();
    }
}
