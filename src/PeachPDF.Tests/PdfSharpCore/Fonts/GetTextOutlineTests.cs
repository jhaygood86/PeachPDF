using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Adapters;
using PeachPDF.Fonts;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Coverage for <see cref="GraphicsAdapter.GetTextOutline"/>: decoding a text run into a
    /// fillable/strokeable vector path (the enabling seam for gradient/pattern fill, stroke, and
    /// <c>&lt;textPath&gt;</c> on SVG text). Uses the bundled Source Sans 3 (TrueType/glyf) and
    /// Source Code Pro (CFF, no glyf) fonts.
    /// </summary>
    public class GetTextOutlineTests
    {
        private const byte StartOfSubpath = 0; // CoreGraphicsPath.PathPointTypeStart

        private static async Task<(GraphicsAdapter Graphics, RFont Font)> Setup(string fontPath, double size)
        {
            var family = TtfFontDescription.LoadDescription(fontPath).FontFamilyInvariantCulture;
            // Keep the adapter's PixelsPerPoint equal to the GraphicsAdapter's (as the real pipeline
            // always does) so font size and outline scale stay in the same 1:1 unit space.
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            await using (var stream = File.OpenRead(fontPath))
                await adapter.AddFont(stream, family);

            var measure = XGraphics.CreateMeasureContext(new XSize(600, 600), XGraphicsUnit.Point, XPageDirection.Downwards);
            var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            var font = adapter.GetFont(family, size, RFontStyle.Regular)!;
            return (graphics, font);
        }

        private static XPoint[] Points(RGraphicsPath path) => ((GraphicsPathAdapter)path).GraphicsPath._corePath.PathPoints;

        private static int SubpathCount(RGraphicsPath path)
            => ((GraphicsPathAdapter)path).GraphicsPath._corePath.PathTypes.Count(t => t == StartOfSubpath);

        [Fact]
        public async Task GlyfFont_ProducesFilledOutline_WithOneSubpathPerContour()
        {
            var (g, font) = await Setup(BundledFonts.Ttf, 100);

            // 'l' is a single stroke (one contour), 'o' is a ring plus its counter (two contours) - so
            // the run's outline has exactly three disjoint subpaths.
            var outline = g.GetTextOutline("lo", font, new RPoint(0, 100));

            Assert.NotNull(outline);
            Assert.Equal(RFillMode.Nonzero, outline!.FillMode);
            Assert.Equal(3, SubpathCount(outline));
            outline.Dispose();
        }

        [Fact]
        public async Task GlyfFont_FlipsYAndScales_GlyphSitsAboveBaseline()
        {
            var (g, font) = await Setup(BundledFonts.Ttf, 100);

            // Baseline at y=100 (y-down user space): 'l' rises well above it (a tall ascender at
            // font-size 100 -> top near y=30) and, having no descender, never drops meaningfully
            // below it. If the design-unit Y weren't flipped, the glyph would instead extend far
            // *below* the baseline (toward y=170).
            var outline = g.GetTextOutline("l", font, new RPoint(0, 100))!;
            var ys = Points(outline).Select(p => p.Y).ToArray();

            Assert.True(ys.Min() < 40, $"expected a tall ascender well above the baseline, top y={ys.Min()}");
            Assert.True(ys.Max() < 103, $"'l' has no descender, so nothing should sit well below the baseline; got bottom y={ys.Max()}");
            outline.Dispose();
        }

        [Fact]
        public async Task GlyfFont_AdvancesPenPerGlyph_AndHonorsLetterSpacing()
        {
            var (g, font) = await Setup(BundledFonts.Ttf, 100);

            double RightEdge(string text, double letterSpacing = 0)
            {
                var outline = g.GetTextOutline(text, font, new RPoint(0, 100), letterSpacing)!;
                var maxX = Points(outline).Max(p => p.X);
                outline.Dispose();
                return maxX;
            }

            double one = RightEdge("l");
            double two = RightEdge("ll");
            double twoSpaced = RightEdge("ll", letterSpacing: 40);

            // A second glyph advances the pen, so "ll" extends past "l"...
            Assert.True(two > one + 10, $"two-glyph run ({two}) should extend past one glyph ({one})");
            // ...and extra letter-spacing pushes the second glyph further right still.
            Assert.True(twoSpaced > two + 30, $"letter-spacing should widen the run (got {twoSpaced} vs {two})");
        }

        [Fact]
        public async Task GlyfFont_SpaceOnlyRun_ReturnsNull()
        {
            var (g, font) = await Setup(BundledFonts.Ttf, 100);

            // The space glyph has an advance but no contours: no geometry is produced, so the run
            // outlines to null (there is nothing to fill or stroke).
            Assert.Null(g.GetTextOutline("   ", font, new RPoint(0, 100)));
        }

        [Fact]
        public async Task CffFont_ReturnsNull_AsFallbackCue()
        {
            // Source Code Pro is CFF/OTTO: no `glyf` table, so no outline can be decoded and the
            // adapter signals the caller to fall back to DrawString.
            var (g, font) = await Setup(BundledFonts.Otf, 100);

            Assert.Null(g.GetTextOutline("lo", font, new RPoint(0, 100)));
        }
    }
}
