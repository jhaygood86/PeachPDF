using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layout-level coverage for how <c>line-height</c> sizes a line box (CSS 2.1
    /// <see href="https://www.w3.org/TR/CSS21/visudet.html#line-height">§10.8</see>/§10.8.1), as opposed to
    /// <see cref="LineHeightTypedStorageTests"/>'s coverage of how the declared value is *stored*. Two rules
    /// are asserted here, both of which Acid2's face depends on and neither of which layout honoured before:
    ///
    /// <list type="number">
    /// <item>A non-replaced inline box contributes exactly its own <c>line-height</c> to the line box. Its
    /// glyph content area (the font's own ascent+descent, which is what the box's background/border area is
    /// painted from) may be <i>taller</i> and simply overflow - that is what negative leading means - so a
    /// <c>line-height</c> shorter than the font's natural height must still be the height that counts.</item>
    /// <item>Every line box that holds content is also tall enough for the <b>strut</b>: an imaginary inline
    /// box carrying the <i>block's</i> own font and <c>line-height</c>. A block whose only content is an
    /// inline declaring a shorter <c>line-height</c> keeps its own, taller line box.</item>
    /// </list>
    ///
    /// The tests deliberately assert against <b>specified</b> values (a <c>line-height</c> in <c>px</c>
    /// converted at <see cref="PeachPDF.Html.Core.Utils.Length.PointsPerPx"/>) rather than anything derived
    /// from the resolved font, so they do not depend on which fallback face the test machine resolves - but
    /// each one that needs the font to be <i>taller</i> than the declared line-height first proves that it is,
    /// by measuring the same markup under <c>line-height: normal</c>. Without that guard a machine whose
    /// fallback font happened to be short would pass these vacuously.
    /// </summary>
    public class LineHeightLineBoxExtentTests
    {
        /// <summary>1 CSS px in the internal layout unit (points) - see <c>Length.PointsPerPx</c>.</summary>
        private const double PointsPerPx = 0.75;

        [Theory]
        [InlineData("1em")]
        [InlineData("12px")]
        [InlineData("1")]
        public async Task LineHeightShorterThanTheFont_IsTheLineBoxHeight_NotTheFontsOwnHeight(string lineHeight)
        {
            // All three spellings resolve to the same 12px used value against a 12px font-size, so all three
            // must produce the same 9pt line box. Before this was fixed the line's height was
            // max(line-height, font height) - the declared value was silently ignored whenever it was the
            // smaller of the two, which is exactly the common case for a font-size-sized line-height.
            var declared = await MeasureSingleLineBlockAsync($"line-height: {lineHeight}");
            var natural = await MeasureSingleLineBlockAsync("line-height: normal");

            Assert.True(natural > 12 * PointsPerPx,
                $"this test is only meaningful when the resolved font's natural line height ({natural}pt) " +
                $"exceeds the declared 12px ({12 * PointsPerPx}pt); it does not on this machine");

            Assert.Equal(12 * PointsPerPx, declared, precision: 6);
        }

        [Fact]
        public async Task LineHeightTallerThanTheFont_StillGrowsTheLineBox()
        {
            // The other direction (positive leading) was already correct and must stay that way: this is the
            // half of the rule the pre-fix "grow only" code got right, and narrowing it is the obvious way to
            // break it.
            var height = await MeasureSingleLineBlockAsync("line-height: 40px");

            Assert.Equal(40 * PointsPerPx, height, precision: 6);
        }

        [Theory]
        // The text matters, not just the count: a line whose only word arrived by *wrapping* is the case
        // the height is easiest to lose, because the wrap moves the cursor onto a line that the growth
        // applied before the wrap decision knew nothing about. "eee fff" leaves two words on the last
        // line, so the second one re-applies the growth and hides the bug; "eee" alone does not. Both
        // shapes are pinned so this can never again pass by a coincidence of the fixture text.
        [InlineData("aaa bbb ccc ddd eee fff")]
        [InlineData("aaa bbb ccc ddd eee")]
        [InlineData("aaaa bb")]
        public async Task ShortLineHeight_StacksEveryLineByThatHeight_NotByTheFontsHeight(string text)
        {
            // The line box's height is also what positions the NEXT line, so a block of several lines must
            // be an exact multiple of it. A fix that only corrected the last line's extent (or the block's
            // closing height) rather than the per-line one would leave this over-tall.
            var html = $"""
                <!DOCTYPE html>
                <html><body style="margin: 0">
                  <div class="t" style="font: 12px sans-serif; line-height: 12px; width: 40px">{text}</div>
                </body></html>
                """;

            var (root, _) = await BuildCssBoxTree(html);
            var target = FindBoxByClass(root, "t")!;
            var lineCount = target.LineBoxes.Count;

            Assert.True(lineCount >= 2, $"the fixture should wrap to several lines, got {lineCount}");
            Assert.Equal(lineCount * 12 * PointsPerPx, target.ActualBottom - target.Location.Y, precision: 6);
        }

        [Theory]
        [InlineData("aaaa bb")]
        [InlineData("aaaa bbbb cccc")]
        public async Task WrappedBlock_UnderDefaultLineHeight_IsStillAnExactMultipleOfItsLineHeight(string text)
        {
            // The same last-line-by-wrapping shape under `line-height: normal`, i.e. at *default* styling,
            // where a lost line height is not a tight-leading edge case but the ordinary "this paragraph
            // ends with one short word" paragraph. Asserted against the block's own resolved line height
            // rather than a literal, since `normal` is font-dependent (see
            // 2026-09-08-line-height-normal-font-metrics).
            var html = $"""
                <!DOCTYPE html>
                <html><body style="margin: 0">
                  <div class="t" style="font: 12px sans-serif; line-height: normal; width: 40px">{text}</div>
                </body></html>
                """;

            var (root, _) = await BuildCssBoxTree(html);
            var target = FindBoxByClass(root, "t")!;
            var lineCount = target.LineBoxes.Count;

            Assert.True(lineCount >= 2, $"the fixture should wrap to several lines, got {lineCount}");
            Assert.Equal(lineCount * target.DerivedStyle.ActualLineHeight,
                target.ActualBottom - target.Location.Y, precision: 6);
        }

        [Fact]
        public async Task BlocksOwnLineHeight_IsTheStrut_WhenItsInlineChildDeclaresAShorterOne()
        {
            // CSS 2.1 §10.8.1's strut. The inline here declares a line-height of 4px against the block's
            // 24px; the line box must stay the block's, because the strut is always on the line. Before this
            // was fixed the block collapsed to the inline's 4px - the block's own line-height was never
            // consulted for a line whose content all came from a descendant.
            const string html = """
                <!DOCTYPE html>
                <html><body style="margin: 0">
                  <div class="t" style="font: 12px sans-serif; line-height: 24px"><span style="font: 2px/4px serif">x</span></div>
                </body></html>
                """;

            var (root, _) = await BuildCssBoxTree(html);
            var target = FindBoxByClass(root, "t")!;

            Assert.Equal(24 * PointsPerPx, target.ActualBottom - target.Location.Y, precision: 6);
        }

        [Fact]
        public async Task InlineChildsTallerLineHeight_StillRaisesTheLineAboveTheBlocksStrut()
        {
            // The mirror of the strut case: the line box is the max over the strut AND every inline box on
            // it, so a taller inline still wins. Asserting only the strut direction would be satisfied by
            // code that ignored the inline's line-height entirely.
            const string html = """
                <!DOCTYPE html>
                <html><body style="margin: 0">
                  <div class="t" style="font: 12px sans-serif; line-height: 12px"><span style="line-height: 40px">x</span></div>
                </body></html>
                """;

            var (root, _) = await BuildCssBoxTree(html);
            var target = FindBoxByClass(root, "t")!;

            Assert.Equal(40 * PointsPerPx, target.ActualBottom - target.Location.Y, precision: 6);
        }

        [Theory]
        // Block 12 / span 40 / em 6 - the middle box wins, and it holds no direct text of its own.
        [InlineData("12px", "40px", "6px", 40)]
        // No em at all: the same answer, from the box that does hold the text.
        [InlineData("12px", "40px", null, 40)]
        // The block itself is the tallest - the strut wins over both inlines.
        [InlineData("40px", "12px", "6px", 40)]
        // Every inline is shorter than the block: the strut still floors the line.
        [InlineData("12px", "6px", "8px", 12)]
        public async Task LineBoxCoversEveryInlineAncestorOnIt_NotJustTheInnermostAndTheStrut(
            string blockLineHeight, string spanLineHeight, string? emLineHeight, double expectedPx)
        {
            // CSS 2.1 §10.8.1 sizes the line box against every inline box on it, so a `line-height` declared
            // on an intermediate <span> counts even when the text lives one level further down in an <em>.
            // Reading only the innermost box and the block's strut made such a span inert. All four
            // expectations were taken from headless Chrome on this exact markup.
            var inner = emLineHeight is null ? "x" : $"""<em style="line-height: {emLineHeight}">x</em>""";
            var html = $"""
                <!DOCTYPE html>
                <html><body style="margin: 0">
                  <div class="t" style="font: 12px sans-serif; line-height: {blockLineHeight}"><span style="line-height: {spanLineHeight}">{inner}</span></div>
                </body></html>
                """;

            var (root, _) = await BuildCssBoxTree(html);
            var target = FindBoxByClass(root, "t")!;

            Assert.Equal(expectedPx * PointsPerPx, target.ActualBottom - target.Location.Y, precision: 6);
        }

        [Fact]
        public async Task WrappingWordContributesItsLineHeightOnlyToTheLineItEnters()
        {
            const string html = """
                <!DOCTYPE html>
                <html><body style="margin: 0">
                  <div class="t" style="font:10px/10px sans-serif; width:35px">aa <span style="line-height:40px"><em style="line-height:10px">bbbb</em></span></div>
                </body></html>
                """;

            var (root, _) = await BuildCssBoxTree(html);
            var target = FindBoxByClass(root, "t")!;

            Assert.Equal(2, target.LineBoxes.Count);
            Assert.Equal(50 * PointsPerPx, target.ActualBottom - target.Location.Y, precision: 6);
        }

        [Fact]
        public async Task ReplacedInlineContent_StillSizesTheLineFromItsOwnBox_NotTheLineHeight()
        {
            // §10.8 sizes a *replaced* inline element's contribution from the element's own margin box, not
            // from line-height - so the narrowing that stopped text words from growing the line must not have
            // caught images with it. A 40px-tall image on a 12px line must still make the block 40px tall.
            var html = $$"""
                <!DOCTYPE html>
                <html><body style="margin: 0">
                  <div class="t" style="font: 12px sans-serif; line-height: 12px"><img src="{{RasterPngFixture.OnePixelDataUri}}" style="width: 40px; height: 40px" alt="" /></div>
                </body></html>
                """;

            var (root, _) = await BuildCssBoxTree(html);
            var target = FindBoxByClass(root, "t")!;

            Assert.Equal(40 * PointsPerPx, target.ActualBottom - target.Location.Y, precision: 6);
        }

        [Theory]
        [InlineData("6px", 6)]
        [InlineData("12px", 12)]
        [InlineData("30px", 30)]
        public async Task VerticalWritingMode_SizesItsColumnByLineHeightToo(string lineHeight, double expectedPx)
        {
            // CreateVerticalLineBoxes is a separate line-layout engine from FlowBox, and it used to take a
            // column's cross-axis thickness straight from the word's glyph footprint, so `line-height` did
            // nothing at all there - the same document laid out under two different line-box models
            // depending on writing-mode. Both engines now go through LineBoxExtentOf. Expectations taken
            // from headless Chrome on this markup (which also gives 14px for `normal`, i.e. the font's own
            // metrics, exercised by the other tests here rather than pinned to a font-dependent literal).
            var html = $"""
                <!DOCTYPE html>
                <html><body style="margin: 0">
                  <div class="t" style="font: 12px sans-serif; writing-mode: vertical-rl; height: 200px; line-height: {lineHeight}">xx</div>
                </body></html>
                """;

            var (root, _) = await BuildCssBoxTree(html);
            var target = FindBoxByClass(root, "t")!;

            Assert.Equal(expectedPx * PointsPerPx, target.ActualRight - target.Location.X, precision: 6);
        }

        [Fact]
        public async Task EmptyBlock_GetsNoStrut_AndStaysZeroHeight()
        {
            // CSS 2.1 §9.4.2: a line box holding no content is zero-height, so the strut must be applied when
            // content is actually placed on the line rather than when the line is created. Applying it at
            // line creation is the natural-looking implementation and would give this block 24px of height
            // out of nothing.
            const string html = """
                <!DOCTYPE html>
                <html><body style="margin: 0">
                  <div class="t" style="font: 12px sans-serif; line-height: 24px"></div>
                </body></html>
                """;

            var (root, _) = await BuildCssBoxTree(html);
            var target = FindBoxByClass(root, "t")!;

            Assert.Equal(0, target.ActualBottom - target.Location.Y, precision: 6);
        }

        [Fact]
        public async Task ShortLineHeight_PaintsItsBackgroundOverTheLineBox_NotTheOverflowingGlyphs()
        {
            // Layout being right is not enough: the fragment a block paints from used to be extended down to
            // cover whatever its content actually reached, which for a short line-height is the glyph content
            // area of the anonymous inline box holding the text. The block's own border box - what its
            // background and border are painted over - must stay the line box (verified against Chrome,
            // which paints exactly the declared 24px here and lets the glyphs spill out of it).
            const string html = """
                <!DOCTYPE html>
                <html><body style="margin: 0">
                  <div class="t" style="font: 24pt Georgia, serif; line-height: 0.75; background: #eef">Aligny jpqg</div>
                </body></html>
                """;

            var (root, container) = await BuildCssBoxTree(html);
            var target = FindBoxByClass(root, "t")!;
            var expected = 0.75 * 24;

            Assert.Equal(expected, target.ActualBottom - target.Location.Y, precision: 6);

            var fragment = FindFragment(container.FragmentTree!.Fragmentainers[0].Root, target);
            Assert.NotNull(fragment);
            Assert.Equal(expected, fragment!.Rect.Height, precision: 6);
            Assert.All(fragment.Lines, line => Assert.Equal(expected, line.Rect.Height, precision: 6));
        }

        private static BoxFragment? FindFragment(BoxFragment fragment, CssBox box)
        {
            if (ReferenceEquals(fragment.Box, box)) return fragment;

            foreach (var child in fragment.Children)
            {
                var found = FindFragment(child, box);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// Lays out a single-line block carrying <paramref name="style"/> on top of a fixed 12px font and
        /// returns its used height.
        /// </summary>
        private static async Task<double> MeasureSingleLineBlockAsync(string style)
        {
            var html = $"""
                <!DOCTYPE html>
                <html><body style="margin: 0">
                  <div class="t" style="font: 12px sans-serif; {style}">x</div>
                </body></html>
                """;

            var (root, _) = await BuildCssBoxTree(html);
            var target = FindBoxByClass(root, "t")!;
            return target.ActualBottom - target.Location.Y;
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildCssBoxTree(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter)
            {
                MarginTop = 0,
                MarginLeft = 0,
                MarginRight = 0,
                MarginBottom = 0
            };

            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindBoxByClass(CssBox root, string className)
        {
            var classAttr = root.HtmlTag?.TryGetAttribute("class", "");
            if (!string.IsNullOrEmpty(classAttr))
            {
                foreach (var cls in classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (cls == className) return root;
                }
            }

            foreach (var child in root.Boxes)
            {
                var result = FindBoxByClass(child, className);
                if (result != null) return result;
            }

            return null;
        }
    }
}
