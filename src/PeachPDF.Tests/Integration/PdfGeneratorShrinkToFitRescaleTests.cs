using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see cref="PdfGenerator.AddPdfPages"/>'s <c>ScaleToPageSize</c>/<c>ShrinkToFit</c> arm used to
    /// unconditionally clear the font cache, re-parse the whole document, and lay it out a second time -
    /// even when the measuring pass already found the content fits the page at the current
    /// <c>PixelsPerPoint</c>, which is the common case for ordinary block-flow content (auto-width block
    /// boxes already stretch to their container's available width; a rescale is only needed when content
    /// genuinely overflows, e.g. a wide table). These pin both directions of the fix: the fast path is
    /// skipped when nothing needs to change, and still runs - and still shrinks - when it does.
    /// </summary>
    public class PdfGeneratorShrinkToFitRescaleTests
    {
        // Ordinary text in a plain block: it never grows wider than the page, so ShrinkToFit's measuring
        // pass already used the correct PixelsPerPoint and the second SetContent/layout pass is pure
        // waste - this is the fast path the fix adds.
        [Fact]
        public async Task ContentAlreadyFitsThePage_DoesNotRunTheSecondSetContentPass()
        {
            const string html = "<html><body><p>Ordinary text that never grows wider than the page.</p></body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, ShrinkToFit = true };

            var (_, _, rescaled, _) = await PdfGeneratorLayoutHarness.LayoutWithRescaleAsync(html, config);

            Assert.False(rescaled, "content that already fits the page must not pay for a second parse+layout pass");
        }

        // Same content, without ShrinkToFit at all, is the control: proves the fast path produces the
        // same geometry a plain, un-shrunk render would - i.e. skipping the second pass didn't leave the
        // box tree in some different state than an equivalent non-ShrinkToFit render.
        [Fact]
        public async Task ContentAlreadyFitsThePage_MatchesGeometryOfANonShrinkToFitRender()
        {
            const string html = "<html><body><p id='p'>Ordinary text that never grows wider than the page.</p></body></html>";

            var (shrinkRoot, _, rescaled, _) = await PdfGeneratorLayoutHarness.LayoutWithRescaleAsync(
                html, new PdfGenerateConfig { PageSize = PageSize.A4, ShrinkToFit = true });
            var (plainRoot, _) = await PdfGeneratorLayoutHarness.LayoutAsync(
                html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.False(rescaled);

            var shrinkParagraph = LayoutHarness.FindById(shrinkRoot, "p")!;
            var plainParagraph = LayoutHarness.FindById(plainRoot, "p")!;

            Assert.Equal(plainParagraph.Location.X, shrinkParagraph.Location.X, 3);
            Assert.Equal(plainParagraph.Location.Y, shrinkParagraph.Location.Y, 3);
            Assert.Equal(plainParagraph.ActualRight, shrinkParagraph.ActualRight, 3);
            Assert.Equal(plainParagraph.ActualBottom, shrinkParagraph.ActualBottom, 3);
        }

        // The case the fast path must not swallow: content that genuinely overflows the page still needs
        // the measured-then-rescaled second pass. Mirrors the fixture in
        // ShrinkToFitPaginationIntegrationTests.cs, which relies on this same shrink actually happening.
        [Fact]
        public async Task OverflowingContent_StillRunsTheSecondSetContentPassAndShrinks()
        {
            const string html = @"<html><body style='margin:0'>
<div style='width: 700pt; height: 10pt'>wide</div>
<p>text</p>
</body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, ShrinkToFit = true };

            var (_, _, rescaled, pixelsPerPoint) = await PdfGeneratorLayoutHarness.LayoutWithRescaleAsync(html, config);

            Assert.True(rescaled, "content wider than the page must still pay for the rescale pass");
            Assert.True(pixelsPerPoint > 1,
                $"PixelsPerPoint should reflect an actual shrink, was {pixelsPerPoint}");
        }

        [Fact]
        public async Task ScaleToPageSize_WithMinContentWidthBelowPageWidth_StillRescalesToGrow()
        {
            // Without MinContentWidth, minPixelsPerPoint floors at the default PixelsPerPoint (1.0), so
            // ScaleToPageSize alone can only ever shrink, never grow - MinContentWidth is what lowers
            // that floor below 1.0. A single short line measures far narrower than the 300pt floor this
            // sets, so ScaleToPageSize must still detect the resulting change and run the second pass.
            const string html = "<html><body style='margin:0'><p>hi</p></body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, ScaleToPageSize = true, MinContentWidth = 300 };

            var (_, _, rescaled, pixelsPerPoint) = await PdfGeneratorLayoutHarness.LayoutWithRescaleAsync(html, config);

            Assert.True(rescaled, "a MinContentWidth floor below the default PixelsPerPoint must still rescale");
            Assert.True(pixelsPerPoint < 1,
                $"PixelsPerPoint should reflect an actual grow (values <1 mean the content is upscaled to fill the page), was {pixelsPerPoint}");
        }

        // Exercises the real PdfGenerator.AddPdfPages (not the LayoutWithRescaleAsync harness, which
        // mirrors the same decision but is a separate method as far as coverage is concerned) for
        // ScaleToPageSize combined with MinContentWidth through the actual public API, as a smoke test
        // that this configuration combination renders without throwing - the fitting case (needsRescale
        // stays false) is covered end-to-end by ContentAlreadyFitsThePage_DoesNotRunTheSecondSetContentPass,
        // and the un-clamped shrink case by OverflowingContent_StillRunsTheSecondSetContentPassAndShrinks.
        [Fact]
        public async Task ScaleToPageSize_WithMinContentWidth_RendersThroughTheRealApi()
        {
            const string html = "<html><body style='margin:0; width: 50pt'>hi</body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, ScaleToPageSize = true, MinContentWidth = 300 };

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, config);

            Assert.Equal(1, doc.PdfDocument.Pages.Count);
        }

        // Direct unit tests for the pure decision PdfGenerator.AddPdfPages now gates the expensive path
        // on - equal values (nothing to do), a genuine shrink, and a genuine grow.
        [Fact]
        public void NeedsRescale_SameValue_ReturnsFalse()
        {
            Assert.False(PdfGenerator.NeedsRescale(1.0, 1.0));
        }

        [Fact]
        public void NeedsRescale_SmallerEffectiveValue_ReturnsTrue()
        {
            Assert.True(PdfGenerator.NeedsRescale(1.0, 1.26));
        }

        [Fact]
        public void NeedsRescale_LargerEffectiveValue_ReturnsTrue()
        {
            Assert.True(PdfGenerator.NeedsRescale(1.0, 0.5));
        }
    }
}
