using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using System.Text;

namespace PeachPDF.Tests.Adapters
{
    /// <summary>
    /// A font size the layout asks for is in layout units (points × <see cref="PdfSharpAdapter.PixelsPerPoint"/>),
    /// and the font behind it is built at <c>size / PixelsPerPoint</c> points. The font caches were keyed by
    /// size alone, so a <see cref="PeachPDF.PdfGenerator"/> reused across renders - a <c>ShrinkToFit</c> render
    /// leaves the fonts it rescaled to in the cache - served the next render a font built at the previous
    /// render's scale: 10pt text came out as 9.819pt, which re-wrapped and re-sized whatever was laid out next
    /// (the TestHarness showed it as <c>css_grid_intrinsic</c>'s items dropping from 79.5pt to 69.3pt, depending
    /// on which showcase happened to be rendered before it).
    /// <para>
    /// A test that only renders once cannot see this - the leak needs two scales against one adapter - so these
    /// drive the adapter directly, flipping <see cref="PdfSharpAdapter.PixelsPerPoint"/> between requests.
    /// </para>
    /// </summary>
    public class FontCacheScaleTests
    {
        private static readonly string Family = DefaultFontResolver.DefaultFont;

        private static FontAdapter Font(PdfSharpAdapter adapter, double size) =>
            (FontAdapter)adapter.GetFont(Family, size, RFontStyle.Regular)!;

        [Fact]
        public void TheSameSizeAtADifferentScaleIsADifferentFont()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var atOne = Font(adapter, 10);

            adapter.PixelsPerPoint = 1.25;
            var atOneAndAQuarter = Font(adapter, 10);

            // 10 layout units at 1.25 layout units per point is an 8pt font, not the 10pt one cached at scale 1.
            Assert.Equal(10, atOne.Font.Size, 6);
            Assert.Equal(8, atOneAndAQuarter.Font.Size, 6);
            Assert.NotSame(atOne, atOneAndAQuarter);
        }

        [Fact]
        public void ReturningToAScaleFindsItsOwnCachedFontAgain()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var first = Font(adapter, 10);

            adapter.PixelsPerPoint = 1.25;
            _ = Font(adapter, 10);
            adapter.PixelsPerPoint = 1.0;

            // Keying by scale must not turn the cache off: the same (size, scale) is still one instance.
            Assert.Same(first, Font(adapter, 10));
        }

        [Fact]
        public void TheScaleStillAppliesAfterAFontFamilyMappingResolvesTheRequest()
        {
            // "monospace" is a mapped generic family: GetCachedFont's mapped-family branch stores the font
            // under the mapped name as well, and that path has its own cache write.
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var atOne = (FontAdapter)adapter.GetFont("monospace", 12, RFontStyle.Regular)!;

            adapter.PixelsPerPoint = 1.5;
            var atOneAndAHalf = (FontAdapter)adapter.GetFont("monospace", 12, RFontStyle.Regular)!;

            Assert.Equal(12, atOne.Font.Size, 6);
            Assert.Equal(8, atOneAndAHalf.Font.Size, 6);
        }

        [Fact]
        public void APerCodepointFontIsKeyedByScaleToo()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var atOne = adapter.GetFontForCodepoint(Family, 10, RFontStyle.Regular, new Rune('A')) as FontAdapter;

            adapter.PixelsPerPoint = 1.25;
            var atOneAndAQuarter = adapter.GetFontForCodepoint(Family, 10, RFontStyle.Regular, new Rune('A')) as FontAdapter;

            Assert.NotNull(atOne);
            Assert.NotNull(atOneAndAQuarter);
            Assert.Equal(10, atOne!.Font.Size, 6);
            Assert.Equal(8, atOneAndAQuarter!.Font.Size, 6);
        }

        [Fact]
        public void ASystemFallbackFontIsKeyedByScaleToo()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var atOne = adapter.GetSystemFallbackFontForCodepoint(10, RFontStyle.Regular, new Rune('A')) as FontAdapter;

            adapter.PixelsPerPoint = 1.25;
            var atOneAndAQuarter = adapter.GetSystemFallbackFontForCodepoint(10, RFontStyle.Regular, new Rune('A')) as FontAdapter;

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
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var before = Font(adapter, 10);

            adapter.ClearFontCache();

            Assert.NotSame(before, Font(adapter, 10));
        }
    }
}
