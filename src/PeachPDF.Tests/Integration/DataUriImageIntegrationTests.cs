using PeachPDF.Tests.TestSupport;
using PeachPDF;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class DataUriImageIntegrationTests
    {
        // Random, incompressible pixel data so the encoded PNG - and therefore the base64 data URI -
        // stays comfortably past System.Uri's pre-.NET-10 65,519-character ceiling regardless of the
        // codec's compression ratio on a given run.
        private static string LargeDataUri()
        {
            const int width = 200;
            const int height = 200;
            var noise = new byte[width * height * 3];
            new Random(42).NextBytes(noise);

            var pngBytes = RasterPngFixture.MakeRgbaPngBytes(width, height, (x, y) =>
            {
                var i = (y * width + x) * 3;
                return (noise[i], noise[i + 1], noise[i + 2], (byte)255);
            });

            return "data:image/png;base64," + Convert.ToBase64String(pngBytes);
        }

        [Fact]
        public async Task Img_WithDataUriPastNet8UriLengthLimit_RendersAsImageXObject()
        {
            var dataUri = LargeDataUri();

            // Guards the test's own premise: this is the exact ceiling RUri's data: bypass exists to
            // avoid (see RUri.cs and RUriTests.cs) - a payload under it wouldn't reproduce issue #980.
            Assert.True(dataUri.Length > 65_519, $"Fixture data URI was only {dataUri.Length} characters.");

            var html = $"<!DOCTYPE html><html><body><img src=\"{dataUri}\"></body></html>";

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());

            Assert.Contains("/Subtype /Image", pdfText);
        }
    }
}
