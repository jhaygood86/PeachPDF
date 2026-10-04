using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies <c>text-decoration</c> actually draws a stroke at a plausible baseline-relative
    /// position with the right color - <c>CssBox.PaintDecoration</c> was already fully implemented but
    /// had no test confirming the paint call itself (only parse-level tests existed for
    /// text-decoration/-line/-color/-style in CSS/PropertyTests/TextProperty.cs). Uses
    /// <see cref="TestRecordingGraphics"/>'s <c>DrawLine</c> logging (added in the border-style
    /// workstream) to assert the real draw-call geometry/color, not just that painting completes.
    /// </summary>
    public class TextDecorationPaintIntegrationTests
    {
        [Fact]
        public async Task Underline_DrawsLineBelowRectangleTop()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:underline; color:rgb(0,0,255)'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.Equal(PaintColor.FromArgb(0, 0, 255), line.PaintColor);

            var rect = s.Rectangles.Values.Single();
            Assert.True(line.Y1 > rect.Top, "underline should sit below the rectangle's top edge");
            Assert.True(line.Y1 <= rect.Bottom, "underline should still sit within the rectangle");
        }

        [Fact]
        public async Task Overline_DrawsLineAtRectangleTop()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:overline'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var rect = s.Rectangles.Values.Single();

            Assert.Equal(rect.Top, line.Y1, 1);
        }

        [Fact]
        public async Task LineThrough_DrawsLineNearRectangleMiddle()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:line-through'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var rect = s.Rectangles.Values.Single();
            var expectedMiddle = rect.Top + rect.Height / 2;

            Assert.Equal(expectedMiddle, line.Y1, 1);
        }

        [Fact]
        public async Task Underline_OverlineLineThrough_ProduceDifferentYPositions()
        {
            var underlineY = await GetDecorationYAsync("underline");
            var overlineY = await GetDecorationYAsync("overline");
            var lineThroughY = await GetDecorationYAsync("line-through");

            Assert.True(overlineY < lineThroughY, "overline should sit above line-through");
            Assert.True(lineThroughY < underlineY, "line-through should sit above underline");
        }

        [Fact]
        public async Task CombinedLines_UnderlineOverline_DrawBothAtTheirOwnPositions()
        {
            // text-decoration-line: underline overline is a single longhand value carrying two keywords
            // (CSS Text Decoration 3 §2.2). PaintDecoration must draw a line for EACH keyword. (The prior
            // implementation switched on the whole string, so a combined value matched no case and drew a
            // single stray line at y=0 — this asserts both lines now paint at their proper positions.)
            var (root, container) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:underline overline'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.Equal(2, lines.Count);

            var rect = s.Rectangles.Values.Single();
            // Overline sits at the rectangle top; underline sits below it — the two must differ, and neither
            // may be the bogus y=0 the old whole-string switch produced.
            Assert.Contains(lines, l => System.Math.Abs(l.Y1 - rect.Top) <= 1);
            Assert.Contains(lines, l => l.Y1 > rect.Top && l.Y1 <= rect.Bottom);
            Assert.All(lines, l => Assert.True(l.Y1 > 0, "no decoration line should be drawn at y=0"));
            Assert.Equal(2, lines.Select(l => System.Math.Round(l.Y1, 2)).Distinct().Count());
        }

        [Fact]
        public async Task TextDecorationColor_OverridesElementColor()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:underline; text-decoration-color:rgb(255,0,0); color:rgb(0,0,255)'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.Equal(PaintColor.FromArgb(255, 0, 0), line.PaintColor);
        }

        [Fact]
        public async Task TextDecorationNone_DrawsNoLine()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:none'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        [Fact]
        public async Task TextDecorationThickness_Auto_PreservesPreExistingFixedWidth()
        {
            // The initial value (auto) must paint identically to a declaration that never set
            // text-decoration-thickness at all - CssBox.PaintDecoration used a hardcoded pen width of 1
            // before this property existed, and auto is defined to preserve that exactly rather than
            // derive a thickness from the font (see the property's own css-properties.json comment).
            var (root, container) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:underline; text-decoration-thickness:auto'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.Equal(1, line.Width);
        }

        [Fact]
        public async Task TextDecorationThickness_ExplicitLength_UsesResolvedThickness()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:underline; text-decoration-thickness:3pt'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            // PixelsPerPoint is 1.0 in BuildAndLayout, so 3pt resolves directly to 3 internal units.
            Assert.Equal(3, line.Width, 1);
        }

        [Fact]
        public async Task TextDecorationThickness_ThickUnderline_KeepsItsTopEdgeBelowTheBaseline()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<span id='s' style='font-size:20pt; text-decoration:underline; text-decoration-thickness:6px'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var rect = s.Rectangles.Values.Single();
            var cssPixel = PeachPDF.CSS.Length.PointsPerPx;
            var expectedGap = System.Math.Ceiling(line.Width / (2 * cssPixel)) * cssPixel;
            var expectedCenter = rect.Top + s.ActualFont.TextBaselineOffset + expectedGap + line.Width / 2;

            Assert.Equal(expectedCenter, line.Y1, 3);
            Assert.True(line.Y1 - line.Width / 2 >= rect.Top + s.ActualFont.TextBaselineOffset + cssPixel,
                "the thick stroke's top edge must not grow upward into the glyphs");
        }

        [Fact]
        public async Task TextDecorationThickness_ExplicitPercentage_ResolvesAgainstFontSize()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:underline; font-size:20pt; text-decoration-thickness:10%'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            // CSS Text Decoration 4 §3.3: a percentage resolves against the element's own font-size, not
            // line-height (unlike vertical-align's superficially similar grammar) - 10% of 20pt = 2pt.
            Assert.Equal(2, line.Width, 1);
        }

        [Fact]
        public async Task TextDecorationThickness_FromFont_UsesRealFontMetricNotHardcodedDefault()
        {
            // Deliberately a loose bound, not a pinned exact value - the resolved font-face (and so its
            // real OpenType post.underlineThickness) is system/platform-dependent, and this repo's own
            // test conventions (see the existing UnderlineOffset-based Y-position assertions above) avoid
            // asserting an exact font-metric-derived number for exactly that reason.
            var (root, container) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:underline; text-decoration-thickness:from-font'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.True(line.Width > 0 && line.Width < 10, $"expected a plausible font-derived thickness, got {line.Width}");
        }

        // ─── Propagation to a block container's inline content (css-text-decor-3 §2.4) ────────────

        [Fact]
        public async Task BlockUnderline_SpansItsInlineContent_NotTheBlocksOwnWidth()
        {
            // A block container's decoration propagates to the anonymous inline box wrapping its in-flow
            // inline content, so the line covers that content. Painting it over the block's own decoration
            // area instead ran a display:block link's underline all the way to the right page margin.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline'>text</div>"));
            var d = FindById(root, "d")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var content = d.Boxes.Single().Rectangles.Values.Single();

            Assert.Equal(content.Left, line.X1, 1);
            Assert.Equal(content.Right, line.X2, 1);
            Assert.True(line.X2 < d.Location.X + 400 - 100,
                $"underline should stop at the text ({line.X2}), not run the block's 400pt width");
        }

        [Fact]
        public async Task BlockUnderline_DrawsOneSpanPerLineBox()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='d' style='width:60pt; font-size:10pt; text-decoration:underline'>aaa bbb ccc ddd eee</div>"));
            var d = FindById(root, "d")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            var content = d.Boxes.Single().Rectangles;

            Assert.True(content.Count > 1, "the fixture must wrap for this test to mean anything");
            Assert.Equal(content.Count, lines.Count);
            Assert.Equal(lines.Count, lines.Select(l => System.Math.Round(l.Y1, 2)).Distinct().Count());
            Assert.All(lines, l => Assert.True(l.X2 - l.X1 > 0 && l.X2 - l.X1 <= 60,
                $"a span of {l.X2 - l.X1} is not one line's worth of a 60pt-wide block"));
        }

        [Fact]
        public async Task BlockUnderline_UnionsEveryInlineChildOnTheSameLineIntoOneSpan()
        {
            // Several inline-level children share one line box, so the line drawn for that line box has
            // to cover all of them - one span per child would leave a gap between each pair.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline'>text <span id='s'>and more</span></div>"));
            var d = FindById(root, "d")!;
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var leading = d.Boxes.First().Rectangles.Values.Single();
            var trailing = s.Rectangles.Values.Single();

            Assert.Equal(leading.Left, line.X1, 1);
            Assert.Equal(trailing.Right, line.X2, 1);
        }

        [Fact]
        public async Task BlockUnderline_IsNotInsetByTheBlocksOwnPadding()
        {
            // The span is inline content geometry, already inside the block's padding - insetting it by
            // that padding a second time (what the box's own decoration area needs) would displace it.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='d' style='width:400pt; padding-left:30pt; text-decoration:underline'>text</div>"));
            var d = FindById(root, "d")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());

            Assert.Equal(d.Boxes.Single().Rectangles.Values.Single().Left, line.X1, 1);
        }

        [Fact]
        public async Task BlockUnderline_ReachesInlineContentOfAnInFlowBlockDescendant()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline'><p id='p'>para</p></div>"));
            var d = FindById(root, "d")!;
            var p = FindById(root, "p")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var content = p.Boxes.Single().Rectangles.Values.Single();

            Assert.Equal(content.Left, line.X1, 1);
            Assert.Equal(content.Right, line.X2, 1);
        }

        [Fact]
        public async Task BlockUnderline_SkipsAFloatedDescendant()
        {
            // §2.4 does not propagate a decoration to out-of-flow descendants.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline'>text"
                + "<span id='f' style='float:right; width:100pt'>floated</span></div>"));
            var d = FindById(root, "d")!;
            var f = FindById(root, "f")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());

            Assert.True(line.X2 <= f.Location.X,
                $"the underline ({line.X2}) should stop before the float at {f.Location.X}");
        }

        [Fact]
        public async Task BlockUnderline_WithNoInlineContent_DrawsNothing()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='d' style='width:400pt; height:20pt; text-decoration:underline'></div>"));
            var d = FindById(root, "d")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        [Fact]
        public async Task BlockWithoutDecoration_DrawsNothing()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='d' style='width:400pt'>text</div>"));
            var d = FindById(root, "d")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        // ─── An atomic inline is a block container: only its inline content is decorated ───────────

        [Theory]
        [InlineData("inline-block")]
        [InlineData("inline-flex")]
        [InlineData("inline-grid")]
        public async Task AtomicInlineUnderline_WithNoText_DrawsNothing(string display)
        {
            // An icon-only link (`a { display: inline-block }` with a background image) has no inline
            // content, so there is nothing for §2.4's propagated decoration to land on. Its rectangle is its
            // border box on the parent's line, which used to be underlined across the whole content width.
            var (root, container) = await BuildAndLayout(Wrap(
                $"<p>Empty: <a id='a' style='display:{display}; width:44pt; height:44pt; padding:5pt 5pt 0; "
                + "text-decoration:underline; background:rgb(10,134,114)'> </a></p>"));
            var a = FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, a, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        [Fact]
        public async Task AtomicInlineUnderline_WithShortText_SpansTheTextNotTheContentWidth()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<p>Short: <a id='a' style='display:inline-block; width:120pt; text-decoration:underline'>ab</a></p>"));
            var a = FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, a, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var text = a.Boxes.Single().Rectangles.Values.Single();

            Assert.Equal(text.Left, line.X1, 1);
            Assert.Equal(text.Right, line.X2, 1);
            Assert.True(line.X2 - line.X1 < 60,
                $"the underline ({line.X2 - line.X1}pt) should cover 'ab', not the 120pt content box");
        }

        [Fact]
        public async Task AtomicInlineUnderline_WrappedText_FollowsEachTextLine()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<p><a id='a' style='display:inline-block; width:60pt; font-size:10pt; text-decoration:underline'>"
                + "aaa bbb ccc ddd eee</a></p>"));
            var a = FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, a, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().OrderBy(l => l.Y1).ToList();
            var text = a.Boxes.Single().Rectangles.Values.OrderBy(r => r.Top).ToList();

            Assert.True(text.Count > 1, "the fixture must wrap for this test to mean anything");
            Assert.Equal(text.Count, lines.Count);

            for (var i = 0; i < lines.Count; i++)
            {
                Assert.Equal(text[i].Left, lines[i].X1, 1);
                Assert.Equal(text[i].Right, lines[i].X2, 1);
            }
        }

        [Fact]
        public async Task AtomicInlineUnderline_WithOnlyANonTextChild_DrawsNothing()
        {
            // No inline content that is text: the child is itself an atomic inline, which §2.4 excludes.
            var (root, container) = await BuildAndLayout(Wrap(
                "<p><a id='a' style='display:inline-block; width:60pt; text-decoration:underline'>"
                + "<img width='20' height='10' src='data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA='>"
                + "<span style='display:inline-block; width:10pt; height:10pt'></span></a></p>"));
            var a = FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, a, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        [Fact]
        public async Task AtomicInlineUnderline_WithBlockLevelChildren_ReachesTheirInlineContent()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div><span id='a' style='display:inline-block; width:100pt; text-decoration:underline'>"
                + "<div id='d'>para</div></span></div>"));
            var a = FindById(root, "a")!;
            var d = FindById(root, "d")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, a, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var text = d.Boxes.Single().Rectangles.Values.Single();

            Assert.Equal(text.Left, line.X1, 1);
            Assert.Equal(text.Right, line.X2, 1);
        }

        [Fact]
        public async Task InlineTableUnderline_ReachesTheTextInItsCellsOnly()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<p><span id='a' style='display:inline-table; width:200pt; text-decoration:underline'>"
                + "<span style='display:table-row'><span id='c' style='display:table-cell'>cell</span></span></span></p>"));
            var a = FindById(root, "a")!;
            var c = FindById(root, "c")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, a, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var text = c.Boxes.Single().Rectangles.Values.Single();

            Assert.Equal(text.Left, line.X1, 1);
            Assert.Equal(text.Right, line.X2, 1);
        }

        [Fact]
        public async Task FormFieldUnderline_KeepsItsControlWideLine()
        {
            // A form control is a replaced atomic inline whose text is not an inline child (its value is the
            // control's own word), so the propagated path finds no content for it. Routing it there would
            // draw nothing where it used to draw a line - too wide, but a line - so it stays on the
            // per-own-line path until a control's text can be found as inline content.
            var (root, container) = await BuildAndLayout(Wrap(
                "<p><input id='f' type='text' value='Go' style='text-decoration:underline'></p>"));
            var f = FindById(root, "f")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, f, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var control = f.Rectangles.Values.Single();

            Assert.True(line.X1 >= control.Left && line.X2 <= control.Right,
                $"the line ({line.X1}-{line.X2}) should stay inside the control ({control.Left}-{control.Right})");
            Assert.True(line.X2 - line.X1 > control.Width * 0.8,
                $"the line ({line.X2 - line.X1}pt) should span the control ({control.Width}pt), not just its text");
        }

        [Fact]
        public async Task AtomicInlineUnderline_WithAnEmptyPaddedInlineChild_DrawsNothing()
        {
            // The icon-in-a-link pattern: `<a><span class=icon></span></a>` where the span only carries
            // padding and a background. An empty inline is no inline content, so its padding is not underlined.
            var (root, container) = await BuildAndLayout(Wrap(
                "<p><a id='a' style='display:inline-block; width:80pt; height:30pt; text-decoration:underline'>"
                + "<span style='padding:0 10pt'></span></a></p>"));
            var a = FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, a, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        [Fact]
        public async Task AtomicInlineUnderline_EmptyPaddedInlineBesideText_UnderlinesOnlyTheText()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<p><a id='a' style='display:inline-block; width:80pt; height:30pt; text-decoration:underline'>"
                + "<span style='padding:0 10pt'></span>ab</a></p>"));
            var a = FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, a, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());

            Assert.True(line.X2 - line.X1 < 20, $"the line ({line.X2 - line.X1}pt) should cover 'ab' only, not the 20pt of padding");
        }

        [Fact]
        public async Task InlineFlexUnderline_WithTextInAFlexItem_ReachesTheItemsText()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<p><span id='a' style='display:inline-flex; width:200pt; text-decoration:underline'>"
                + "<span id='t'>ab</span></span></p>"));
            var a = FindById(root, "a")!;
            var t = FindById(root, "t")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, a, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var text = t.Boxes.Single().Rectangles.Values.Single();

            Assert.Equal(text.Left, line.X1, 1);
            Assert.Equal(text.Right, line.X2, 1);
        }

        [Fact]
        public async Task SelectUnderline_KeepsItsControlWideLine()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<p><select id='f' style='text-decoration:underline'><option>Go</option></select></p>"));
            var f = FindById(root, "f")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, f, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var control = f.Rectangles.Values.Single();

            Assert.True(line.X2 - line.X1 > control.Width * 0.8,
                $"the line ({line.X2 - line.X1}pt) should span the control ({control.Width}pt)");
        }

        [Fact]
        public async Task TextareaUnderline_IsUnderItsTextNotTheWholeControl()
        {
            // A <textarea> is a static inline-block, not a form field, so it takes the propagated path.
            var (root, container) = await BuildAndLayout(Wrap(
                "<p><textarea id='f' style='width:200pt; text-decoration:underline'>ab</textarea></p>"));
            var f = FindById(root, "f")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, f, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var control = f.Rectangles.Values.Single();

            Assert.True(line.X2 - line.X1 < control.Width * 0.3,
                $"the line ({line.X2 - line.X1}pt) should cover 'ab', not the {control.Width}pt control");
        }

        [Fact]
        public async Task InlineGridUnderline_WithTextInAGridItem_ReachesTheItemsText()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<p><span id='a' style='display:inline-grid; width:200pt; text-decoration:underline'>"
                + "<span id='t'>ab</span></span></p>"));
            var a = FindById(root, "a")!;
            var t = FindById(root, "t")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, a, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var text = t.Boxes.Single().Rectangles.Values.Single();

            Assert.Equal(text.Left, line.X1, 1);
            Assert.Equal(text.Right, line.X2, 1);
        }

        // ─── The decoration is drawn inside the decorating box's own overflow clip ─────────────────

        [Theory]
        [InlineData("display:inline-block", false)]
        [InlineData("display:block", false)]
        [InlineData("display:inline-block", true)]
        [InlineData("display:block", true)]
        public async Task PropagatedUnderline_OfAClippingBox_IsDrawnInsideItsOwnClip(string display, bool snap)
        {
            // A link truncated with `overflow: hidden; white-space: nowrap`: the text runs far past the box,
            // and so did the underline - the clip a fragment carries is its clipping *ancestor's*, so the
            // decorating box's own clip was not in force while its decoration was drawn. The box is
            // fractional so SnapBoxDecorationsToCssPixels has something to snap: with it on the clip must sit
            // on whole CSS pixels, like the clip the box's own content gets, and with it off on the true edges.
            var (root, container) = await BuildAndLayout(Wrap(
                $"<p><a id='a' style='{display}; width:80.2pt; margin-left:10.3pt; overflow:hidden; white-space:nowrap; "
                + "text-decoration:underline'>abcdefghijklmnopqrstuvwxyz abcdefghijklmnopqrstuvwxyz</a></p>"));
            container.SnapBoxDecorationsToCssPixels = snap;
            var a = FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, a, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var box = a.Rectangles.Count > 0 ? a.Rectangles.Values.Single() : a.Bounds;

            Assert.True(line.X2 > box.Right, "the fixture's text must overflow the box for this test to mean anything");

            var clip = Assert.Single(ClipsAround(g, line));
            var cssPixel = PeachPDF.CSS.Length.PointsPerPx;

            if (snap)
            {
                Assert.Equal(0, clip.Left / cssPixel - System.Math.Round(clip.Left / cssPixel), 6);
                Assert.Equal(0, clip.Right / cssPixel - System.Math.Round(clip.Right / cssPixel), 6);
                Assert.InRange(clip.Left, box.Left - cssPixel / 2 - 1e-6, box.Left + cssPixel / 2 + 1e-6);
                Assert.InRange(clip.Right, box.Right - cssPixel / 2 - 1e-6, box.Right + cssPixel / 2 + 1e-6);
            }
            else
            {
                Assert.Equal(box.Left, clip.Left, 6);
                Assert.Equal(box.Right, clip.Right, 6);
            }
        }

        [Fact]
        public async Task PropagatedUnderline_OfARoundedClippingBox_IsAlsoClippedToItsCorners()
        {
            // The clip is the box's padding edge rounded to its border-radius, like the one its descendants get.
            var (root, container) = await BuildAndLayout(Wrap(
                "<p><a id='a' style='display:inline-block; width:80pt; border-radius:12pt; overflow:hidden; "
                + "white-space:nowrap; text-decoration:underline'>abcdefghijklmnopqrstuvwxyz abcdefghijklmnopqrstuvwxyz</a></p>"));
            var a = FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, a, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var box = a.Rectangles.Values.Single();
            var clips = ClipsAround(g, line);

            Assert.Equal(2, clips.Count);
            Assert.Equal(box.Left, clips[0].Left, 1);
            Assert.Equal(box.Right, clips[0].Right, 1);
        }

        [Fact]
        public async Task PropagatedUnderline_OfABoxThatDoesNotClip_PushesNoClip()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<p><a id='a' style='display:inline-block; width:80pt; text-decoration:underline'>ab</a></p>"));
            var a = FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, a, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());

            Assert.Empty(ClipsAround(g, line));
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<double> GetDecorationYAsync(string decorationLine)
        {
            var (root, container) = await BuildAndLayout(Wrap(
                $"<span id='s' style='text-decoration:{decorationLine}'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            return Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>()).Y1;
        }

        /// <summary>The clip rectangles in force when <paramref name="line"/> was drawn, outermost first.</summary>
        private static List<Rect> ClipsAround(TestRecordingGraphics g, TestRecordingGraphics.DrawLineCall line)
        {
            var open = new List<Rect>();

            foreach (var call in g.Log)
            {
                if (ReferenceEquals(call, line)) return open;
                if (call is TestRecordingGraphics.PushClipCall push) open.Add(push.Rect);
                else if (call is TestRecordingGraphics.PopClipCall && open.Count > 0) open.RemoveAt(open.Count - 1);
            }

            throw new System.InvalidOperationException("the line was not drawn");
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
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

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
