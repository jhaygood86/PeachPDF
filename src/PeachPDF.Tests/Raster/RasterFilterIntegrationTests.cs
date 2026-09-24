using PeachPDF.Tests.TestSupport;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Raster
{
    /// <summary>
    /// End-to-end checks of the raster path: a CSS <c>filter</c> PDF cannot express as vector content is rendered into
    /// a bitmap and embedded. The properties that matter to a reader of the PDF are asserted structurally on the
    /// written file: which images exist, how many pixels they have, and exactly how large they are placed.
    /// </summary>
    public class RasterFilterIntegrationTests
    {
        private sealed record Placed(double WidthPt, double HeightPt);

        private static string Html(string filter) =>
            $"<html><body style=\"margin:0\"><div style=\"width:100px;height:60px;background:#e33;{filter}\"></div></body></html>";

        private static PdfGenerateConfig Config(double pixelsPerInch = 72, double? dpi = null, bool downscale = true) => new()
        {
            PageSize = PageSize.A4,
            CompressContentStreams = false,
            PixelsPerInch = pixelsPerInch,
            DownscaleImages = downscale,
            RasterizationDpi = dpi ?? 300,
            MarginLeft = 0,
            MarginTop = 0,
            MarginRight = 0,
            MarginBottom = 0,
        };

        /// <summary>The pixel widths of every image XObject, one entry per object (an alpha bitmap has a colour object and a mask object).</summary>
        private static List<(int Width, int Height)> ImageSizes(string pdf)
        {
            var sizes = new List<(int, int)>();
            foreach (Match obj in Regex.Matches(pdf, @"(?:^|[\r\n])\d+ 0 obj(.*?)endobj", RegexOptions.Singleline))
            {
                // The dictionary is everything before the stream data; its keys come in no fixed order.
                var text = obj.Groups[1].Value;
                var streamStart = text.IndexOf("stream", StringComparison.Ordinal);
                var dictionary = streamStart >= 0 ? text[..streamStart] : text;
                if (!Regex.IsMatch(dictionary, @"/Subtype\s*/Image"))
                    continue;

                var width = Regex.Match(dictionary, @"/Width\s+(\d+)");
                var height = Regex.Match(dictionary, @"/Height\s+(\d+)");
                if (width.Success && height.Success)
                    sizes.Add((int.Parse(width.Groups[1].Value), int.Parse(height.Groups[1].Value)));
            }

            return sizes;
        }

        private static List<Placed> Placements(string pdf) =>
            Regex.Matches(pdf, @"q\s+([-\d.]+)\s+0\s+0\s+([-\d.]+)\s+[-\d.]+\s+[-\d.]+\s+cm\s+/I\d+\s+Do\s+Q")
                .Select(m => new Placed(
                    double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    Math.Abs(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))))
                .ToList();

        [Theory]
        [InlineData(72.0, 300.0)]
        [InlineData(96.0, 288.0)]
        [InlineData(96.0, 150.0)]
        [InlineData(150.0, 600.0)]
        public async Task BlurredBox_IsAnExactPhysicalSize_AtTheConfiguredDpi(double pixelsPerInch, double dpi)
        {
            var pdf = await PdfObjectReader.GeneratePdf(Html("filter: blur(3px);"), Config(pixelsPerInch, dpi));

            var placements = Placements(pdf);
            var placed = Assert.Single(placements);
            var sizes = ImageSizes(pdf);
            Assert.NotEmpty(sizes);
            var (pixelsWide, pixelsHigh) = sizes[0];

            // One inch of paper holds exactly `dpi` pixels of the bitmap.
            Assert.Equal(pixelsWide * 72.0 / dpi, placed.WidthPt, 2);
            Assert.Equal(pixelsHigh * 72.0 / dpi, placed.HeightPt, 2);

            // The box is 100 x 60 CSS px (75 x 45 pt) plus three standard deviations of blur (3 x 2.25 pt) on
            // every side; the bitmap is that, snapped outwards by less than one pixel per edge.
            var margin = 3 * 3 * 0.75;
            Assert.InRange(placed.WidthPt, 75 + 2 * margin, 75 + 2 * margin + 2 * 72.0 / dpi + 0.01);
            Assert.InRange(placed.HeightPt, 45 + 2 * margin, 45 + 2 * margin + 2 * 72.0 / dpi + 0.01);
        }

        [Fact]
        public async Task HigherDpi_GivesMorePixelsForTheSamePlacedSize()
        {
            var low = await PdfObjectReader.GeneratePdf(Html("filter: blur(3px);"), Config(dpi: 150));
            var high = await PdfObjectReader.GeneratePdf(Html("filter: blur(3px);"), Config(dpi: 600));

            var lowPixels = ImageSizes(low)[0].Width;
            var highPixels = ImageSizes(high)[0].Width;

            Assert.InRange((double)highPixels / lowPixels, 3.9, 4.1);
            Assert.Equal(Placements(low)[0].WidthPt, Placements(high)[0].WidthPt, 0);
        }

        [Theory]
        [InlineData(ImageCompression.Auto)]
        [InlineData(ImageCompression.Lossless)]
        [InlineData(ImageCompression.Lossy)]
        public async Task RasterOutput_IsNeverDownscaled_ByTheImageDownscaler(ImageCompression compression)
        {
            var downscaled = Config(dpi: 300, downscale: true);
            downscaled.ImageCompression = compression;
            var full = Config(dpi: 300, downscale: false);
            full.ImageCompression = compression;

            var withDownscaling = await PdfObjectReader.GeneratePdf(Html("filter: blur(3px);"), downscaled);
            var without = await PdfObjectReader.GeneratePdf(Html("filter: blur(3px);"), full);

            Assert.Equal(ImageSizes(without), ImageSizes(withDownscaling));
        }

        [Theory]
        [InlineData("filter: blur(3px);")]
        [InlineData("filter: grayscale(1);")]
        [InlineData("filter: sepia(0.5);")]
        [InlineData("filter: saturate(2);")]
        [InlineData("filter: hue-rotate(90deg);")]
        [InlineData("filter: brightness(1.2) blur(1px);")]
        public async Task FunctionsPdfCannotExpress_AreEmbeddedAsABitmap(string filter)
        {
            var pdf = await PdfObjectReader.GeneratePdf(Html(filter), Config());

            Assert.Single(Placements(pdf));
            Assert.NotEmpty(ImageSizes(pdf));
        }

        [Theory]
        [InlineData("")]
        [InlineData("filter: brightness(1.2);")]
        [InlineData("filter: contrast(0.8) invert(0.3);")]
        [InlineData("filter: blur(0);")]
        [InlineData("filter: blur(0px);")]
        [InlineData("filter: grayscale(0);")]
        [InlineData("filter: saturate(1);")]
        [InlineData("filter: hue-rotate(0deg);")]
        [InlineData("filter: opacity(0.5);")]
        public async Task ContentPdfCanExpress_StaysVector(string filter)
        {
            var pdf = await PdfObjectReader.GeneratePdf(Html(filter), Config());

            Assert.Empty(ImageSizes(pdf));
            Assert.Empty(Placements(pdf));
        }

        [Fact]
        public async Task BlurWithChannelIndependentFunctions_RunsTheWholeListInTheRasterPath()
        {
            // brightness() alone would stay vector; next to blur() the whole list is one bitmap, in order.
            var pdf = await PdfObjectReader.GeneratePdf(Html("filter: brightness(1.5) blur(2px);"), Config());

            Assert.Single(Placements(pdf));
            Assert.DoesNotContain("/TR", pdf);
        }

        [Theory]
        [InlineData(50.0)]
        [InlineData(1201.0)]
        [InlineData(0.0)]
        [InlineData(double.NaN)]
        public async Task RasterizationDpi_OutOfRange_IsRejected(double dpi)
        {
            var config = Config();
            config.RasterizationDpi = dpi;

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => PdfObjectReader.GeneratePdf(Html("filter: blur(3px);"), config));
        }

        [Fact]
        public async Task OversizedRegion_LowersTheResolution_ButKeepsThePlacedSize()
        {
            var config = Config(dpi: 300);
            config.MaxRasterPixels = 20_000;

            var pdf = await PdfObjectReader.GeneratePdf(Html("filter: blur(3px);"), config);

            var (w, h) = ImageSizes(pdf)[0];
            Assert.True((long)w * h <= 20_000);
            var placed = Placements(pdf).Single();
            Assert.InRange(placed.WidthPt, 75 + 2 * 6.75, 75 + 2 * 6.75 + 3);
        }

        [Fact]
        public async Task ManyFilteredBoxes_EachGetTheirOwnBitmap()
        {
            const string html = """
                <html><body style="margin:0">
                <div style="width:40px;height:40px;background:red;filter:blur(2px)"></div>
                <div style="width:40px;height:40px;background:green;filter:grayscale(1)"></div>
                <div style="width:40px;height:40px;background:blue;"></div>
                </body></html>
                """;

            var pdf = await PdfObjectReader.GeneratePdf(html, Config());

            Assert.Equal(2, Placements(pdf).Count);
        }
    }
}
