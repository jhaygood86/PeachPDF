using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PeachDrawing.Text.Compression;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// PDF 2.0 <c>/BrotliDecode</c> stream compression and its force-off rules. Serialized with the other tests that swap the
    /// process-wide Brotli compressor, since a registered compressor is visible to every render in the process.
    /// </summary>
    [Collection(BrotliCompressorCollection.Name)]
    public class BrotliStreamCompressionTests
    {
        const string Html = "<html><body><p>Hello Brotli, hello Brotli, hello Brotli, hello Brotli.</p></body></html>";

        static async Task<byte[]> Render(PdfGenerateConfig config)
        {
            var document = await new PdfGenerator().GeneratePdf(Html, config);
            using var stream = new MemoryStream();
            document.Save(stream);
            return stream.ToArray();
        }

        static PdfGenerateConfig Config(PdfVersion version, bool brotli = true) => new()
        {
            PageSize = PageSize.A4,
            PdfVersion = version,
            BrotliCompression = brotli,
        };

        [Fact]
        public async Task Pdf20_CompressesStreamsWithBrotli_AndTheyDecodeToTheContent()
        {
            var bytes = await Render(Config(PdfVersion.Pdf20));
            var text = Encoding.Latin1.GetString(bytes);

            Assert.Contains("/BrotliDecode", text);

            var match = Regex.Match(text, @"/Filter\s*/BrotliDecode.*?stream\r?\n", RegexOptions.Singleline);
            Assert.True(match.Success);
            var start = match.Index + match.Length;
            var end = text.IndexOf("endstream", start, StringComparison.Ordinal);
            var payload = bytes.AsSpan(start, end - start).ToArray();

            // Trailing EOL before endstream is tolerated by the decoder.
            using var decoder = new BrotliStream(new MemoryStream(payload), CompressionMode.Decompress);
            using var decoded = new MemoryStream();
            decoder.CopyTo(decoded);
            Assert.True(decoded.Length > 0);
        }

        [Fact]
        public async Task Pdf20_WithBrotliDisabled_UsesFlate()
        {
            var text = Encoding.Latin1.GetString(await Render(Config(PdfVersion.Pdf20, brotli: false)));

            Assert.DoesNotContain("/BrotliDecode", text);
            Assert.Contains("/FlateDecode", text);
        }

        [Fact]
        public async Task Pdf17_ForcesBrotliOff_EvenWhenRequested()
        {
            var text = Encoding.Latin1.GetString(await Render(Config(PdfVersion.Pdf17, brotli: true)));

            Assert.DoesNotContain("/BrotliDecode", text);
            Assert.Contains("/FlateDecode", text);
        }

        [Fact]
        public async Task Pdf20_WhenNoEncoderIsAvailable_FallsBackToFlate()
        {
            try
            {
                BrotliCompression.SetCompressor((_, _) => throw new PlatformNotSupportedException());
                var text = Encoding.Latin1.GetString(await Render(Config(PdfVersion.Pdf20)));

                Assert.DoesNotContain("/BrotliDecode", text);
                Assert.Contains("/FlateDecode", text);
            }
            finally
            {
                BrotliCompression.SetCompressor(null);
            }
        }

        [Fact]
        public async Task Pdf20_UsesARegisteredCompressor()
        {
            var calls = 0;
            try
            {
                BrotliCompression.SetCompressor((destination, _) =>
                {
                    calls++;
                    return new BrotliStream(destination, CompressionLevel.Fastest, leaveOpen: true);
                });
                var text = Encoding.Latin1.GetString(await Render(Config(PdfVersion.Pdf20)));

                Assert.Contains("/BrotliDecode", text);
                Assert.True(calls > 0);
            }
            finally
            {
                BrotliCompression.SetCompressor(null);
            }
        }
    }

    /// <summary>Holds every PeachPDF test that registers a process-wide Brotli compressor; never runs alongside other test classes.</summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class BrotliCompressorCollection
    {
        public const string Name = "Brotli compressor (process-wide state)";
    }
}
