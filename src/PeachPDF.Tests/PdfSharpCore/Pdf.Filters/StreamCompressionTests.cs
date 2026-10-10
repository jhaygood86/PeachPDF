using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using PeachDrawing.Text.Compression;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Filters;
using PeachPDF.Tests.Integration;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf.Filters
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
            document.Options.PdfVersion = PeachPDF.PdfVersion.Pdf20;
            document.Options.BrotliCompression = brotli;
            return document;
        }

        [Fact]
        public void BrotliDecode_EncodesEveryModeToDecodableData()
        {
            var filter = new BrotliDecode();

            Assert.True(BrotliDecode.IsAvailable);
            Assert.Equal("/BrotliDecode", BrotliDecode.Name);
            Assert.Equal(Sample, Unbrotli(filter.Encode(Sample)));
            foreach (var mode in Enum.GetValues<PdfFlateEncodeMode>())
                Assert.Equal(Sample, Unbrotli(filter.Encode(Sample, mode)!));
        }

        [Fact]
        public void BrotliDecode_ReturnsNull_WhenNoEncoderIsAvailable()
        {
            try
            {
                BrotliCompression.SetCompressor((_, _) => throw new PlatformNotSupportedException());

                Assert.Null(new BrotliDecode().Encode(Sample, PdfFlateEncodeMode.Default));
                Assert.Equal(Sample, new BrotliDecode().Encode(Sample));
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

        [Fact]
        public void NonUnicodeTrueTypeFont_IsEmbeddedWithTheDocumentsFilter()
        {
            var document = Pdf20Document();
            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);
            var font = TestFonts.Create("Times New Roman", 12, pdfOptions: new XPdfFontOptions(PdfFontEncoding.WinAnsi));
            gfx.DrawString("Hello", font, XBrushes.Black, new XRect(0, 0, 200, 50), XStringFormats.TopLeft);

            using var stream = new MemoryStream();
            document.Save(stream);
            var text = Encoding.Latin1.GetString(stream.ToArray());

            Assert.Contains("/FontFile2", text);
            Assert.Contains("/BrotliDecode", text);
        }
    }
}
