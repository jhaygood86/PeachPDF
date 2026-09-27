using PeachDrawing.Text.Shaping;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using Xunit;
using PeachDrawing.Text;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Coverage for <see cref="GraphicsAdapter.GetTextOutline"/>: decoding a text run into a
    /// fillable/strokeable vector path (the enabling seam for gradient/pattern fill, stroke,
    /// <c>&lt;textPath&gt;</c> on SVG text, and <c>background-clip: text</c>). Uses the bundled Source
    /// Sans 3 (TrueType/glyf, via the engine's glyf outline decoder) and Source Code Pro
    /// (CFF/OTTO, no glyf - via the engine's Type2 charstring interpreter) fonts.
    /// </summary>
    public class GetTextOutlineTests
    {
        private const byte StartOfSubpath = 0; // CoreGraphicsPath.PathPointTypeStart

        private static async Task<(GraphicsAdapter Graphics, RFont Font)> Setup(string fontPath, double size)
        {
            var family = TypefaceFixtures.FamilyNameOf(fontPath);
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
        public async Task CffFont_ProducesFilledOutline_WithOneSubpathPerContour()
        {
            // Source Code Pro is CFF/OTTO (no `glyf` table at all) - its outline comes entirely from
            // Type2CharstringInterpreter (issue #1117). 'l' is a single stroke (one contour), 'o' is a
            // ring plus its counter (two contours) - so the run's outline has exactly three disjoint
            // subpaths, same shape of assertion as the glyf-backed run above.
            var (g, font) = await Setup(BundledFonts.Otf, 100);

            var outline = g.GetTextOutline("lo", font, new RPoint(0, 100));

            Assert.NotNull(outline);
            Assert.Equal(RFillMode.Nonzero, outline!.FillMode);
            Assert.Equal(3, SubpathCount(outline));
            outline.Dispose();
        }

        [Fact]
        public async Task CffFont_FlipsYAndScales_GlyphSitsAboveBaseline()
        {
            var (g, font) = await Setup(BundledFonts.Otf, 100);

            var outline = g.GetTextOutline("l", font, new RPoint(0, 100))!;
            var ys = Points(outline).Select(p => p.Y).ToArray();

            Assert.True(ys.Min() < 40, $"expected a tall ascender well above the baseline, top y={ys.Min()}");
            Assert.True(ys.Max() < 103, $"'l' has no descender, so nothing should sit well below the baseline; got bottom y={ys.Max()}");
            outline.Dispose();
        }

        [Fact]
        public async Task CffFont_AdvancesPenPerGlyph_AndHonorsLetterSpacing()
        {
            var (g, font) = await Setup(BundledFonts.Otf, 100);

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

            // A second glyph advances the pen (proof that callsubr/callgsubr's shared pen state and
            // the width-operand disambiguation on the *first* glyph's own charstring didn't desync the
            // interpreter's (x, y) for the glyphs after it) ...
            Assert.True(two > one + 10, $"two-glyph run ({two}) should extend past one glyph ({one})");
            // ...and extra letter-spacing pushes the second glyph further right still.
            Assert.True(twoSpaced > two + 30, $"letter-spacing should widen the run (got {twoSpaced} vs {two})");
        }

        [Fact]
        public async Task CffFont_SpaceOnlyRun_ReturnsNull()
        {
            var (g, font) = await Setup(BundledFonts.Otf, 100);

            Assert.Null(g.GetTextOutline("   ", font, new RPoint(0, 100)));
        }

        [Fact]
        public async Task Ligature_MergesTwoGlyphsIntoOneSubpath()
        {
            // Source Sans 3's "f_f" ligature glyph (confirmed via fontTools) has exactly 1 contour -
            // same as a single "f" - so shaping "ff" into one glyph halves the subpath count from
            // what drawing two separate 'f' outlines would produce.
            var (g, font) = await Setup(BundledFonts.Ttf, 100);

            var unligated = g.GetTextOutline("ff", font, new RPoint(0, 100), letterSpacing: 0, new ShapeSettings(LigatureSet.None))!;
            var ligated = g.GetTextOutline("ff", font, new RPoint(0, 100), letterSpacing: 0, new ShapeSettings(LigatureSet.Default))!;

            Assert.Equal(2, SubpathCount(unligated));
            Assert.Equal(1, SubpathCount(ligated));
            unligated.Dispose();
            ligated.Dispose();
        }

        [Fact]
        public async Task Ligature_AdvancesPenByLigatureGlyphWidth_NotSumOfComponents()
        {
            // The f_f ligature glyph's own hmtx advance (577 design units) is narrower than two
            // separate 'f' advances (2 x 292 = 584) - a real, measurable width difference that only
            // appears if the glyph run was actually merged rather than measured/drawn per codepoint.
            var (g, font) = await Setup(BundledFonts.Ttf, 100);

            double RightEdge(LigatureSet features)
            {
                var outline = g.GetTextOutline("ff", font, new RPoint(0, 100), letterSpacing: 0, new ShapeSettings(features))!;
                var maxX = Points(outline).Max(p => p.X);
                outline.Dispose();
                return maxX;
            }

            var unligated = RightEdge(LigatureSet.None);
            var ligated = RightEdge(LigatureSet.Default);

            Assert.True(ligated < unligated, $"expected the merged ligature glyph to advance less than two separate 'f's; ligated={ligated}, unligated={unligated}");
        }
    }
}
