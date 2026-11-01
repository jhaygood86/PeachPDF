using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The experimental <c>/JXLDecode</c> pass-through (<see cref="PdfGenerateConfig.JxlPassthrough"/>): which JPEG XL
    /// images embed as their original bytes, and every switch that turns it off.
    /// </summary>
    public class JxlPassthroughTests
    {
        private static async Task<(string Text, byte[] Bytes)> RenderAsync(string fixture, PdfGenerateConfig config, string style = "")
        {
            var html = $"<html><body><img src='{JxlFixtures.DataUri(fixture)}' style='{style}'></body></html>";
            var doc = await new PdfGenerator().GeneratePdf(html, config);
            using var ms = new MemoryStream();
            doc.Save(ms);
            return (Encoding.Latin1.GetString(ms.ToArray()), ms.ToArray());
        }

        private static PdfGenerateConfig Config(bool passthrough = true, PdfVersion version = PdfVersion.Pdf20,
            ImageCompression compression = ImageCompression.Auto) => new()
        {
            PageSize = PageSize.A4,
            PdfVersion = version,
            JxlPassthrough = passthrough,
            ImageCompression = compression,
            CompressContentStreams = false,
        };

        private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

        [Theory]
        [InlineData("rgb_lossy")]
        [InlineData("rgb_lossless")]
        [InlineData("icc_lossless")]
        public async Task EligibleRgb_EmbedsTheOriginalBytesAsJxlDecode(string fixture)
        {
            var (text, bytes) = await RenderAsync(fixture, Config());

            Assert.Contains("/Filter /JXLDecode", text);
            Assert.DoesNotContain("/DCTDecode", text);
            Assert.True(Contains(bytes, JxlFixtures.Load(fixture)), "the PDF should contain the .jxl bytes verbatim");
        }

        [Fact]
        public async Task Gray_UsesDeviceGray()
        {
            var (text, bytes) = await RenderAsync("gray", Config());

            Assert.Contains("/JXLDecode", text);
            Assert.Contains("/DeviceGray", text);
            Assert.True(Contains(bytes, JxlFixtures.Load("gray")));
        }

        [Fact]
        public async Task SixteenBit_StatesBitsPerComponent16()
        {
            var (text, _) = await RenderAsync("rgb16", Config());

            Assert.Contains("/JXLDecode", text);
            Assert.Contains("/BitsPerComponent 16", text);
        }

        [Fact]
        public async Task Passthrough_IsNotResizedWhenDownscalingIsRequested()
        {
            var (text, bytes) = await RenderAsync("rgb_lossless", Config(), "width:12pt");

            Assert.Contains("/JXLDecode", text);
            Assert.True(Contains(bytes, JxlFixtures.Load("rgb_lossless")));
        }

        [Fact]
        public async Task OffByDefault()
        {
            var (text, _) = await RenderAsync("rgb_lossy", new PdfGenerateConfig { PageSize = PageSize.A4, PdfVersion = PdfVersion.Pdf20 });

            Assert.DoesNotContain("/JXLDecode", text);
        }

        [Fact]
        public async Task ForcedOffUnlessPdf20()
        {
            var (text, _) = await RenderAsync("rgb_lossy", Config(version: PdfVersion.Pdf17));

            Assert.DoesNotContain("/JXLDecode", text);
        }

        [Fact]
        public async Task LossyImageCompression_ReEncodesInstead()
        {
            var (text, _) = await RenderAsync("rgb_lossy", Config(compression: ImageCompression.Lossy));

            Assert.DoesNotContain("/JXLDecode", text);
            Assert.Contains("/DCTDecode", text);
        }

        [Theory]
        [InlineData("rgba")]
        [InlineData("conformance_animation_spline")]
        public async Task AlphaAndAnimatedSources_KeepTheRasterPath(string fixture)
        {
            var (text, _) = await RenderAsync(fixture, Config());

            Assert.DoesNotContain("/JXLDecode", text);
            Assert.Contains("/Subtype /Image", text);
        }

        [Fact]
        public async Task RecompressedJpeg_EmbedsTheJxlBytesWhenRequested()
        {
            var (text, bytes) = await RenderAsync("recompressed_generated_ycc420", Config());

            Assert.Contains("/JXLDecode", text);
            Assert.True(Contains(bytes, JxlFixtures.Load("recompressed_generated_ycc420")));
        }
    }
}
