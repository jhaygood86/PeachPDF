using PeachDrawing.Core;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using PeachDrawing.Text.Compression;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Filters;
using PeachPDF.Tests.Integration;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfEngineTests.Filters
{
    /// <summary>
    /// The <c>/BrotliDecode</c> filter and the <see cref="StreamCompression"/> chokepoint every general stream goes through,
    /// including the call sites the HTML pipeline never reaches (a hand-built content stream, <c>PdfStream.Zip</c>, a non-Unicode
    /// TrueType font). Serialized with the other tests that swap the process-wide Brotli compressor.
    /// </summary>
    [Collection(BrotliCompressorCollection.Name)]
    public class StreamCompressionTests
    {
        private static readonly byte[] Sample = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("BT /F1 12 Tf (Hello) Tj ET\n", 100)));

        private static byte[] Unbrotli(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            return output.ToArray();
        }

        private static PdfDocument Pdf20Document(bool brotli = true)
        {
            var document = new PdfDocument();
            document.Options.PdfVersion = PeachDrawing.PDF.Engine.PdfVersion.Pdf20;
            document.Options.BrotliCompression = brotli;
            return document;
        }

        [Fact]
        public void BrotliEncoder_EncodesEveryModeToDecodableData()
        {
            Assert.True(PdfBrotliEncoder.IsAvailable);
            Assert.Equal("/BrotliDecode", PdfBrotliEncoder.Name);
            Assert.Equal(Sample, Unbrotli(PdfBrotliEncoder.TryEncode(Sample)!));
            foreach (var mode in Enum.GetValues<PdfFlateEncodeMode>())
                Assert.Equal(Sample, Unbrotli(PdfBrotliEncoder.TryEncode(Sample, mode)!));
        }

        [Fact]
        public void BrotliEncoder_ReturnsNull_WhenNoEncoderIsAvailable()
        {
            try
            {
                BrotliCompression.SetCompressor((_, _) => throw new PlatformNotSupportedException());

                Assert.Null(PdfBrotliEncoder.TryEncode(Sample, PdfFlateEncodeMode.Default));
            }
            finally
            {
                BrotliCompression.SetCompressor(null);
            }
        }

        [Fact]
        public void StreamCompression_UsesBrotliOnlyForPdf20WithTheOptionOn()
        {
            Assert.True(Pdf20Document().Options.BrotliCompression);
            Assert.False(Pdf20Document(brotli: false).Options.BrotliCompression);

            StreamCompression.Encode(Pdf20Document().Options, Sample, out var brotli);
            StreamCompression.Encode(Pdf20Document(brotli: false).Options, Sample, out var optedOut);
            StreamCompression.Encode(new PdfDocument().Options, Sample, out var legacy);

            Assert.Equal("/BrotliDecode", brotli);
            Assert.Equal("/FlateDecode", optedOut);
            Assert.Equal("/FlateDecode", legacy);
        }

        [Fact]
        public void PdfContent_Compressed_UsesTheDocumentsFilter()
        {
            var document = Pdf20Document();
            var content = document.AddPage().Contents.AppendContent();
            content.CreateStream(Sample);

            content.Compressed = true;

            Assert.Equal("/BrotliDecode", content.Elements.GetName("/Filter"));
            Assert.Equal(Sample, Unbrotli(content.Stream.Value));
        }

        [Fact]
        public void PdfStream_Zip_UsesTheDocumentsFilter_AndLeavesAFilteredStreamAlone()
        {
            var document = Pdf20Document();
            var dictionary = new PdfDictionary(document);
            dictionary.CreateStream(Sample);

            dictionary.Stream.Zip();
            var compressed = dictionary.Stream.Value;
            dictionary.Stream.Zip();

            Assert.Equal("/BrotliDecode", dictionary.Elements.GetName("/Filter"));
            Assert.Equal(Sample, Unbrotli(compressed));
            Assert.Same(compressed, dictionary.Stream.Value);
        }

    }
}
