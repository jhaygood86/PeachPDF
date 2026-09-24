using PeachPDF.Adapters;
using PeachPDF.Raster;
using PeachPDF.Tests.TestSupport;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Html.Core.Paint
{
    /// <summary>
    /// <c>backdrop-filter</c>: the filter applies to what was painted behind the element, inside its border box. Pages are laid out for real
    /// and painted into a raster page (no PDF), so the pixels behind and inside the element can be read back.
    /// </summary>
    public class BackdropFilterPaintTests
    {
        private static RasterGraphics NewPage(int width, int height) =>
            new(new PdfSharpAdapter(), new RasterSurface(width, height, 0, 0, 1, 1), 1);

        private static byte[] Pixel(RasterGraphics g, int x, int y) => g.Surface.Row(y).Slice(x * 4, 4).ToArray();

        private static async Task<RasterGraphics> Paint(string body, int width = 120, int height = 100)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), margin: 0);
            var page = NewPage(width, height);
            FragmentPaintHarness.PaintPage(container, page);
            return page;
        }

        /// <summary>Red left half and blue right half of a 100 x 60 stage, with a glass panel over the seam.</summary>
        private static string Stage(string glassStyle, string after = "") => $$"""
            <div style="position:relative;margin:0;width:100pt;height:60pt">
              <div style="position:absolute;left:0;top:0;width:50pt;height:60pt;background:#ff0000"></div>
              <div style="position:absolute;left:50pt;top:0;width:50pt;height:60pt;background:#0000ff"></div>
              <div style="position:absolute;left:30pt;top:10pt;width:40pt;height:40pt;{{glassStyle}}"></div>
              {{after}}
            </div>
            """;

        [Fact]
        public async Task Blur_MixesTheColoursBehindTheElement_AndLeavesTheRestSharp()
        {
            var page = await Paint(Stage("backdrop-filter:blur(6pt)"));

            // Just inside the glass at the seam the backdrop is a red/blue mix, not either pure colour.
            var seam = Pixel(page, 50, 30);
            Assert.InRange((int)seam[0], 60, 200);
            Assert.InRange((int)seam[2], 60, 200);

            // Outside the glass the seam is as sharp as the artwork.
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(page, 45, 5));
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(page, 55, 5));
        }

        [Fact]
        public async Task AHugeBlurRadius_StaysWithinThePixelBudget_AndAveragesTheBackdrop()
        {
            // Padding by three deviations would need a surface of hundreds of megapixels; the pad is bounded instead, and the blur reaches
            // across the whole box, so the pure red behind this pixel is mixed with the blue on the far side of the seam.
            var page = await Paint(Stage("backdrop-filter:blur(100000pt)"));

            var inside = Pixel(page, 40, 30);
            Assert.Equal(255, inside[3]);
            Assert.True(inside[2] > 20, $"blue was {inside[2]}");
            Assert.True(inside[0] < 250, $"red was {inside[0]}");
        }

        [Fact]
        public async Task TheWebkitPrefixedSpelling_IsTheSameProperty()
        {
            var page = await Paint(Stage("-webkit-backdrop-filter:blur(6pt)"));

            var seam = Pixel(page, 50, 30);
            Assert.InRange((int)seam[0], 60, 200);
            Assert.InRange((int)seam[2], 60, 200);
        }

        [Fact]
        public async Task Grayscale_TurnsTheBackdropGrey_ButNotAnythingOutsideTheBox()
        {
            var page = await Paint(Stage("backdrop-filter:grayscale(1)"));

            var inside = Pixel(page, 40, 30);
            Assert.Equal(inside[0], inside[1]);
            Assert.Equal(inside[1], inside[2]);
            Assert.InRange((int)inside[0], 50, 58);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(page, 25, 30));
        }

        [Fact]
        public async Task TheBackdrop_IsDrawnBehindTheElementsOwnBackground()
        {
            var page = await Paint(Stage("backdrop-filter:grayscale(1);background:rgba(0,255,0,0.5)"));

            // Grey backdrop (about 54) under half-opaque green: green rises, red and blue fall to about half of the grey.
            var p = Pixel(page, 40, 30);
            Assert.InRange((int)p[1], 140, 160);
            Assert.InRange((int)p[0], 22, 32);
        }

        [Fact]
        public async Task ContentPaintedAfterTheElement_IsNotPartOfItsBackdrop()
        {
            // A white page, the glass, then a green square painted over the middle of it. Blurring a backdrop that included the green would
            // tint the pixels beside the square; they must stay white.
            const string body = """
                <div style="position:relative;margin:0;width:100pt;height:60pt">
                  <div style="position:absolute;left:20pt;top:10pt;width:60pt;height:40pt;backdrop-filter:blur(4pt)"></div>
                  <div style="position:absolute;left:45pt;top:25pt;width:10pt;height:10pt;background:#00ff00"></div>
                </div>
                """;
            var page = await Paint(body);

            Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(page, 50, 30));
            var beside = Pixel(page, 40, 30);
            Assert.Equal(new byte[] { 255, 255, 255, 255 }, beside);
        }

        [Fact]
        public async Task RoundedCorners_ClipTheFilteredBackdrop()
        {
            var page = await Paint(Stage("backdrop-filter:grayscale(1);border-radius:20pt"));

            // The corner of the box is outside the rounding: the artwork shows through unfiltered there.
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(page, 31, 11));
            var centre = Pixel(page, 40, 30);
            Assert.Equal(centre[0], centre[1]);
        }

        [Fact]
        public async Task ABackdropRoot_IsolatesTheBackdrop_FromWhatLiesOutsideIt()
        {
            // The glass is inside a group with opacity, which is a backdrop root: the red page behind the group is not in its backdrop, so
            // where the group has painted nothing the filter has nothing to turn grey.
            const string body = """
                <div style="position:relative;margin:0;width:100pt;height:60pt;background:#ff0000">
                  <div style="position:absolute;left:0;top:0;width:100pt;height:60pt;opacity:0.99">
                    <div style="position:absolute;left:30pt;top:10pt;width:40pt;height:40pt;backdrop-filter:grayscale(1)"></div>
                  </div>
                </div>
                """;
            var page = await Paint(body);

            var p = Pixel(page, 50, 30);
            Assert.True(p[0] > 200 && p[1] < 60, $"expected the red page, got {p[0]},{p[1]},{p[2]}");
        }

        [Fact]
        public async Task ATransformedAncestor_LeavesTheFilterUnapplied()
        {
            // The backdrop lives in another coordinate space than the element under a transformed ancestor; nothing is filtered.
            const string body = """
                <div style="position:relative;margin:0;width:100pt;height:60pt;background:#ff0000">
                  <div style="position:absolute;left:0;top:0;width:100pt;height:60pt;transform:translate(1pt,0)">
                    <div style="position:absolute;left:30pt;top:10pt;width:40pt;height:40pt;backdrop-filter:grayscale(1)"></div>
                  </div>
                </div>
                """;
            var page = await Paint(body);

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(page, 50, 30));
        }

        [Fact]
        public async Task ANestedBackdropFilter_SeesTheEarlierOnesResult()
        {
            // The second panel's backdrop includes the first panel's already-filtered pixels.
            const string body = """
                <div style="position:relative;margin:0;width:100pt;height:60pt;background:#ff0000">
                  <div style="position:absolute;left:10pt;top:10pt;width:60pt;height:40pt;backdrop-filter:grayscale(1)"></div>
                  <div style="position:absolute;left:30pt;top:20pt;width:20pt;height:20pt;backdrop-filter:invert(1)"></div>
                </div>
                """;
            var page = await Paint(body);

            // Grey ~54 inverted is ~201: the inner panel is light grey, not the inverse of red (cyan).
            var p = Pixel(page, 40, 30);
            Assert.InRange(Math.Abs(p[0] - p[1]), 0, 2);
            Assert.InRange((int)p[0], 195, 207);
        }

        [Fact]
        public async Task ANonRasterGraphics_PaintsTheElementNormally()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(Stage("backdrop-filter:blur(6pt);background:#00ff00")), margin: 0);
            var recording = new RecordingGraphics(new PdfSharpAdapter());

            FragmentPaintHarness.PaintPage(container, recording);

            Assert.Contains(recording.Log, op => op.Kind == PaintOpKind.FillRect);
        }

        [Fact]
        public async Task TheProperty_CreatesAStackingContext_LikeFilterDoes()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<div id=\"a\" style=\"backdrop-filter:blur(2pt)\"></div>"), margin: 0);

            var box = LayoutHarness.FindById(container.Root!, "a");
            Assert.NotNull(box);
            Assert.True(PeachPDF.Html.Core.Utils.DomUtils.IsStackingContextBox(box!));
        }

        // ---- through the PDF pipeline ---------------------------------------------------------------------

        private static PdfGenerateConfig Config() => new()
        {
            PageSize = PageSize.A4,
            CompressContentStreams = false,
            MarginLeft = 0,
            MarginTop = 0,
            MarginRight = 0,
            MarginBottom = 0,
        };

        [Fact]
        public async Task InAPdf_TheFilteredBackdropIsOneBitmapPlacedAtTheBorderBox()
        {
            var pdf = await PdfObjectReader.GeneratePdf(Stage("backdrop-filter:blur(3pt)"), Config());

            var placements = Regex.Matches(pdf, @"q\s+([-\d.]+)\s+0\s+0\s+([-\d.]+)\s+[-\d.]+\s+[-\d.]+\s+cm\s+/I\d+\s+Do\s+Q");
            var placed = Assert.Single(placements);
            // 40pt x 40pt border box, snapped outward to the 300 dpi pixel grid.
            Assert.InRange(double.Parse(placed.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 40, 41);
            Assert.InRange(Math.Abs(double.Parse(placed.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)), 40, 41);
        }

        [Fact]
        public async Task WithoutBackdropFilter_NoBitmapIsEmbedded()
        {
            var pdf = await PdfObjectReader.GeneratePdf(Stage("background:#00ff00"), Config());

            Assert.DoesNotMatch(@"/Subtype\s*/Image", pdf);
        }

        [Fact]
        public async Task UnderPdfA1_TheBitmapIsRejected_LikeAnyOtherTransparencyConstruct()
        {
            var config = Config();
            config.PdfAConformance = PdfAConformance.PdfA1B;

            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => PdfObjectReader.GeneratePdf(Stage("backdrop-filter:blur(3pt)"), config));
        }
    }
}
