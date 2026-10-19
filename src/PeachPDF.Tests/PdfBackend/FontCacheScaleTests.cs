using PeachPDF.Rendering;
using PeachDrawing.PDF;
using PeachDrawing.Core;
using PeachPDF.Html.Core.Utils;
using System.Text;

namespace PeachPDF.Tests.PdfBackendTests
{
    /// <summary>
    /// A font size the layout asks for is in layout units (points × <see cref="HtmlRenderContext.PixelsPerPoint"/>),
    /// and the font behind it is built at <c>size / PixelsPerPoint</c> points. The font caches were keyed by
    /// size alone, so a <see cref="PeachPDF.PdfGenerator"/> reused across renders - a <c>ShrinkToFit</c> render
    /// leaves the fonts it rescaled to in the cache - served the next render a font built at the previous
    /// render's scale: 10pt text came out as 9.819pt, which re-wrapped and re-sized whatever was laid out next
    /// (the TestHarness showed it as <c>css_grid_intrinsic</c>'s items dropping from 79.5pt to 69.3pt, depending
    /// on which showcase happened to be rendered before it).
    /// <para>
    /// A test that only renders once cannot see this - the leak needs two scales against one adapter - so these
    /// drive the adapter directly, flipping <see cref="HtmlRenderContext.PixelsPerPoint"/> between requests.
    /// </para>
    /// </summary>
    public class FontCacheScaleTests
    {
        private static readonly string Family = DefaultFontResolver.DefaultFont;

        private static PdfCanvasFont Font(HtmlRenderContext renderContext, double size) =>
            (PdfCanvasFont)renderContext.GetFont(Family, size, PaintFontStyle.Regular)!;

        [Fact]
        public void TheSameSizeAtADifferentScaleIsADifferentFont()
        {
            var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var atOne = Font(renderContext, 10);

            renderContext.PixelsPerPoint = 1.25;
            var atOneAndAQuarter = Font(renderContext, 10);

            // 10 layout units at 1.25 layout units per point is an 8pt font, not the 10pt one cached at scale 1.
            Assert.Equal(10, atOne.Font.Size, 6);
            Assert.Equal(8, atOneAndAQuarter.Font.Size, 6);
            Assert.NotSame(atOne, atOneAndAQuarter);
        }

        [Fact]
        public void ReturningToAScaleFindsItsOwnCachedFontAgain()
        {
            var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var first = Font(renderContext, 10);

            renderContext.PixelsPerPoint = 1.25;
            _ = Font(renderContext, 10);
            renderContext.PixelsPerPoint = 1.0;

            // Keying by scale must not turn the cache off: the same (size, scale) is still one instance.
            Assert.Same(first, Font(renderContext, 10));
        }

        [Fact]
        public void TheScaleStillAppliesAfterAFontFamilyMappingResolvesTheRequest()
        {
            // "monospace" is a mapped generic family: GetCachedFont's mapped-family branch stores the font
            // under the mapped name as well, and that path has its own cache write.
            var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var atOne = (PdfCanvasFont)renderContext.GetFont("monospace", 12, PaintFontStyle.Regular)!;

            renderContext.PixelsPerPoint = 1.5;
            var atOneAndAHalf = (PdfCanvasFont)renderContext.GetFont("monospace", 12, PaintFontStyle.Regular)!;

            Assert.Equal(12, atOne.Font.Size, 6);
            Assert.Equal(8, atOneAndAHalf.Font.Size, 6);
        }

        [Fact]
        public void APerCodepointFontIsKeyedByScaleToo()
        {
            var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var atOne = renderContext.GetFontForCodepoint(Family, 10, PaintFontStyle.Regular, new Rune('A')) as PdfCanvasFont;

            renderContext.PixelsPerPoint = 1.25;
            var atOneAndAQuarter = renderContext.GetFontForCodepoint(Family, 10, PaintFontStyle.Regular, new Rune('A')) as PdfCanvasFont;

            Assert.NotNull(atOne);
            Assert.NotNull(atOneAndAQuarter);
            Assert.Equal(10, atOne!.Font.Size, 6);
            Assert.Equal(8, atOneAndAQuarter!.Font.Size, 6);
        }

        [Fact]
        public void ASystemFallbackFontIsKeyedByScaleToo()
        {
            var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var atOne = renderContext.GetSystemFallbackFontForCodepoint(10, PaintFontStyle.Regular, new Rune('A')) as PdfCanvasFont;

            renderContext.PixelsPerPoint = 1.25;
            var atOneAndAQuarter = renderContext.GetSystemFallbackFontForCodepoint(10, PaintFontStyle.Regular, new Rune('A')) as PdfCanvasFont;

            // A host with no installed family covering 'A' answers null at both scales, which says nothing about
            // the key - only a real answer at each scale can be compared. Whether a font is found must not
            // depend on the scale, though, so that much is asserted on every host.
            Assert.Equal(atOne is null, atOneAndAQuarter is null);
            if (atOne is null || atOneAndAQuarter is null) return;

            Assert.Equal(10, atOne.Font.Size, 6);
            Assert.Equal(8, atOneAndAQuarter.Font.Size, 6);
        }

        [Fact]
        public void ClearingTheCacheStillDropsEveryScale()
        {
            var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var before = Font(renderContext, 10);

            renderContext.ClearFontCache();

            Assert.NotSame(before, Font(renderContext, 10));
        }
    }
}
