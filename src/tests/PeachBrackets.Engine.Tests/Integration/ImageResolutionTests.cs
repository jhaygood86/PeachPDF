using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachPDF.Rendering;
using PeachBrackets.Html.Dom;
using PeachImage;
using PeachPDF;
using PeachPDF.Tests.TestSupport;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// CSS <c>image-resolution</c> (CSS Images 4 §6.1): the grammar, the cascade, the density PeachImage reports for each file kind, and the
    /// natural size an image takes from it. Rasters are 100 x 100 pixels, so at the default 96 dpi they are 75pt wide.
    /// </summary>
    public class ImageResolutionTests
    {
        private const double PerInch = 100.0 / 2.54;

        private static string DataUri(byte[] bytes, string mime) => $"data:{mime};base64,{Convert.ToBase64String(bytes)}";

        private static Image Raster() => Image.Create(100, 100, PixelFormat.Rgb24);

        /// <summary>A PNG that records <paramref name="dpi"/> (a <c>pHYs</c> chunk), embedded by pass-through; none when null.</summary>
        private static string Png(double? dpi, double? dpiY = null)
        {
            using var image = Raster();
            if (dpi is not null)
            {
                image.Metadata.HorizontalResolution = dpi * PerInch;
                image.Metadata.VerticalResolution = (dpiY ?? dpi) * PerInch;
            }

            using var ms = new MemoryStream();
            image.Save(ms, "png");
            return DataUri(ms.ToArray(), "image/png");
        }

        private static async Task<(double W, double H)> ImgSizeAsync(string src, string style = "", string wrapperStyle = "")
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style='{wrapperStyle}'><img id='i' style='display:block;{style}' src='{src}'></div>"));
            var word = LayoutHarness.FindById(root, "i")!.Words[0];
            return (word.Width, word.Height);
        }

        // ----------------------------------------------------------------- grammar

        [Theory]
        [InlineData("from-image", true, null, false)]
        [InlineData("FROM-IMAGE", true, null, false)]
        [InlineData("300dpi", false, 300 / 96.0, false)]
        [InlineData("2dppx", false, 2.0, false)]
        [InlineData("2x", false, 2.0, false)]
        [InlineData("118.11dpcm", false, 118.11 * 2.54 / 96.0, false)]
        [InlineData("from-image 300dpi", true, 300 / 96.0, false)]
        [InlineData("300dpi from-image", true, 300 / 96.0, false)]
        [InlineData("300dpi snap", false, 300 / 96.0, true)]
        [InlineData("snap from-image", true, null, true)]
        [InlineData("snap from-image 150dpi", true, 150 / 96.0, true)]
        public void Grammar_Accepts(string text, bool fromImage, double? dppx, bool snap)
        {
            var parsed = ImageResolution.TryParse(text);

            Assert.NotNull(parsed);
            Assert.Equal(fromImage, parsed!.Value.FromImage);
            Assert.Equal(snap, parsed.Value.Snap);
            if (dppx is null) Assert.Null(parsed.Value.Dppx);
            else Assert.Equal(dppx.Value, parsed.Value.Dppx!.Value, 4);
        }

        [Theory]
        [InlineData("")]
        [InlineData("snap")]
        [InlineData("0dpi")]
        [InlineData("-300dpi")]
        [InlineData("300")]
        [InlineData("300px")]
        [InlineData("from-image from-image")]
        [InlineData("300dpi 150dpi")]
        [InlineData("snap snap 300dpi")]
        [InlineData("bogus")]
        [InlineData("from-image 300dpi snap extra")]
        public void Grammar_Rejects(string text) => Assert.Null(ImageResolution.TryParse(text));

        [Theory]
        [InlineData(300 / 96.0, 3)]
        [InlineData(0.75, 1)]
        [InlineData(1, 1)]
        [InlineData(1.4, 1)]
        [InlineData(1.6, 2)]
        [InlineData(0.5, 0.5)]
        [InlineData(0.35, 1 / 3.0)]
        public void Snap_RoundsToAWholeNumberOfDevicePixelsPerImagePixel(double dppx, double expected) =>
            Assert.Equal(expected, ImageResolution.Snapped(dppx), 6);

        [Fact]
        public void Resolve_FromImageUsesTheFileThenTheFallbackThenOneDppx()
        {
            Assert.Equal(300 / 96.0, new ImageResolution(true, null, false).Resolve(300), 6);
            Assert.Equal(150 / 96.0, new ImageResolution(true, 150 / 96.0, false).Resolve(null), 6);
            Assert.Equal(1.0, new ImageResolution(true, null, false).Resolve(null), 6);
            // An explicit resolution without from-image ignores the file.
            Assert.Equal(2.0, new ImageResolution(false, 2.0, false).Resolve(300), 6);
        }

        // ----------------------------------------------------------------- cascade

        private static async Task<CssBox> BoxAsync(string html, string id)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            return LayoutHarness.FindById(root, id)!;
        }

        [Fact]
        public async Task Cascade_InitialValueIsOneDppx() =>
            Assert.Equal("1dppx", (await BoxAsync("<div id='a'></div>", "a")).ImageResolution);

        [Fact]
        public async Task Cascade_IsInheritedAndOverridable()
        {
            Assert.Equal("from-image", (await BoxAsync("<div style='image-resolution:from-image'><div><span id='a'>x</span></div></div>", "a")).ImageResolution);
            Assert.Equal("2dppx", (await BoxAsync("<div style='image-resolution:from-image'><p id='a' style='image-resolution:2dppx'></p></div>", "a")).ImageResolution);
            Assert.Equal("1dppx", (await BoxAsync("<div style='image-resolution:from-image'><p id='a' style='image-resolution:initial'></p></div>", "a")).ImageResolution);
        }

        [Fact]
        public async Task Cascade_InvalidDeclarationIsDropped() =>
            Assert.Equal("from-image", (await BoxAsync("<div style='image-resolution:from-image'><p id='a' style='image-resolution:snap'></p></div>", "a")).ImageResolution);

        // ----------------------------------------------------------------- sizing

        [Fact]
        public async Task Sizing_DefaultIgnoresTheDensityInTheFile()
        {
            var (w, h) = await ImgSizeAsync(Png(300));

            Assert.Equal(75, w, 3);
            Assert.Equal(75, h, 3);
        }

        [Theory]
        [InlineData(300, 24)]
        [InlineData(96, 75)]
        [InlineData(72, 100)]
        [InlineData(150, 48)]
        public async Task Sizing_FromImageUsesThePngDensity(double dpi, double expectedPt)
        {
            var (w, h) = await ImgSizeAsync(Png(dpi), "image-resolution: from-image");

            Assert.Equal(expectedPt, w, 0.2);
            Assert.Equal(expectedPt, h, 0.2);
        }

        [Fact]
        public async Task Sizing_FromImageUsesADensityThatDiffersPerAxis()
        {
            var (w, h) = await ImgSizeAsync(Png(300, 150), "image-resolution: from-image");

            Assert.Equal(24, w, 0.2);
            Assert.Equal(48, h, 0.2);
        }

        [Fact]
        public async Task Sizing_FromImageWithNoDensityFallsBackToTheGivenResolutionThenToOneDppx()
        {
            Assert.Equal(48, (await ImgSizeAsync(Png(null), "image-resolution: from-image 150dpi")).W, 0.2);
            Assert.Equal(75, (await ImgSizeAsync(Png(null), "image-resolution: from-image")).W, 0.2);
        }

        [Fact]
        public async Task Sizing_AnExplicitResolutionIgnoresTheFile()
        {
            Assert.Equal(24, (await ImgSizeAsync(Png(null), "image-resolution: 300dpi")).W, 0.2);
            Assert.Equal(24, (await ImgSizeAsync(Png(72), "image-resolution: 300dpi")).W, 0.2);
            Assert.Equal(37.5, (await ImgSizeAsync(Png(null), "image-resolution: 2dppx")).W, 0.01);
        }

        [Fact]
        public async Task Sizing_SnapRoundsTheResolution()
        {
            // 300dpi is 3.125dppx: snapped to 3, so 100px / 3 = 33.33px = 25pt rather than 24pt.
            Assert.Equal(25, (await ImgSizeAsync(Png(null), "image-resolution: 300dpi snap")).W, 0.01);
            Assert.Equal(25, (await ImgSizeAsync(Png(300), "image-resolution: from-image snap")).W, 0.01);
        }

        [Fact]
        public async Task Sizing_IsInheritedFromAnAncestor()
        {
            var (w, _) = await ImgSizeAsync(Png(300), wrapperStyle: "image-resolution: from-image");

            Assert.Equal(24, w, 0.2);
        }

        [Fact]
        public async Task Sizing_OneAxisSetKeepsTheNaturalAspectRatio()
        {
            var (w, h) = await ImgSizeAsync(Png(300, 150), "image-resolution: from-image; width: 48pt");

            // The natural size is 24pt x 48pt, a 1:2 box.
            Assert.Equal(48, w, 0.2);
            Assert.Equal(96, h, 0.4);
        }

        [Fact]
        public async Task Sizing_ExplicitWidthAndHeightAreNotAffected()
        {
            var (w, h) = await ImgSizeAsync(Png(300), "image-resolution: from-image; width: 40pt; height: 30pt");

            Assert.Equal(40, w, 0.01);
            Assert.Equal(30, h, 0.01);
        }

        [Fact]
        public async Task Sizing_AJpegWithExifDensityIsRead()
        {
            using var image = Raster();
            image.Metadata.Profiles.Add(new RawMetadataProfile { Kind = MetadataProfileKind.Exif, Data = ExifDensity(300) });
            using var ms = new MemoryStream();
            image.Save(ms, "jpeg");

            var (w, _) = await ImgSizeAsync(DataUri(ms.ToArray(), "image/jpeg"), "image-resolution: from-image");

            Assert.Equal(24, w, 0.2);
        }

        // ----------------------------------------------------------------- paint

        private static async Task<TestRecordingGraphics> PaintAsync(string body)
        {
            var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml($"<!DOCTYPE html><html><head></head><body style='margin:0'>{body}</body></html>", null);

            var size = new PeachDrawing.Core.Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PeachDrawing.PDF.PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var recorder = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, container.Root!, recorder);
            return recorder;
        }

        [Theory]
        [InlineData("", 75)]
        [InlineData("image-resolution: from-image;", 24)]
        [InlineData("image-resolution: 2dppx;", 37.5)]
        public async Task Img_IsDrawnAtTheNaturalSizeTheResolutionGives(string style, double expectedPt)
        {
            var g = await PaintAsync($"<img style='{style}' src='{Png(300)}'>");

            var draw = Assert.Single(g.DrawImageCalls);
            Assert.Equal(expectedPt, draw.DestRect.Width, 0.2);
            Assert.Equal(expectedPt, draw.DestRect.Height, 0.2);
        }

        [Fact]
        public async Task BackgroundImage_TakesItsAutoSizeFromTheResolution()
        {
            var g = await PaintAsync($"<div style='width:200pt;height:200pt;background:url({Png(300)}) no-repeat;image-resolution:from-image'></div>");

            var draw = g.DrawImageCalls.First();
            Assert.Equal(24, draw.DestRect.Width, 0.2);
            Assert.Equal(24, draw.DestRect.Height, 0.2);
        }

        [Fact]
        public async Task BackgroundImage_DefaultIsStillNinetySixDpi()
        {
            var g = await PaintAsync($"<div style='width:200pt;height:200pt;background:url({Png(300)}) no-repeat'></div>");

            Assert.Equal(75, g.DrawImageCalls.First().DestRect.Width, 0.2);
        }

        /// <summary>A little-endian TIFF block whose IFD0 holds XResolution, YResolution and a ResolutionUnit of inches.</summary>
        private static byte[] ExifDensity(uint dpi)
        {
            using var ms = new MemoryStream();
            void U16(int v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)v); ms.Write(b); }
            void U32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); ms.Write(b); }

            ms.Write("II"u8);
            U16(42);
            U32(8);
            U16(3);
            const uint data = 8 + 2 + (3 * 12) + 4;
            U16(0x011A); U16(5); U32(1); U32(data);
            U16(0x011B); U16(5); U32(1); U32(data + 8);
            U16(0x0128); U16(3); U32(1); U16(2); U16(0);
            U32(0);
            U32(dpi); U32(1);
            U32(dpi); U32(1);
            return ms.ToArray();
        }
    }
}
