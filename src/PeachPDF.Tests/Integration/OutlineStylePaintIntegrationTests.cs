using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies <c>outline</c>/<c>outline-color</c>/<c>outline-style</c>/<c>outline-width</c>/
    /// <c>outline-offset</c> actually paint the right geometry/color/order (issue #697), not just that
    /// they parse or that painting doesn't throw - per this repo's painting-test convention (see
    /// <c>BorderStylePaintIntegrationTests</c>). Uses <see cref="TestRecordingGraphics"/> to assert the
    /// real draw-call sequence, plus one real-PDF content-stream test
    /// (<see cref="OutlineColorInvert_ProducesADifferenceBlendModeExtGStateInTheRealPdf"/>) proving the
    /// new <see cref="PeachPDF.Html.Adapters.RGraphics.PushBlendMode"/>/<see cref="PeachPDF.Html.Adapters.RGraphics.PopBlendMode"/>
    /// primitive actually reaches the PDF-writing layer, not just the test mock.
    /// </summary>
    public class OutlineStylePaintIntegrationTests
    {
        [Fact]
        public async Task Outline_PaintsRingOutsetByWidthAndOffset_OutsideTheBorderEdge()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:50pt; height:30pt; outline: 6pt solid rgb(10,20,30); outline-offset: 4pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            var rect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, polys.Count);
            Assert.All(polys, p => Assert.Equal(RColor.FromArgb(10, 20, 30), p.Color));

            const double offset = 4;
            const double width = 6;
            const double reach = offset + width;

            var top = polys.OrderBy(p => p.Points.Average(pt => pt.Y)).First();
            var ys = top.Points.Select(pt => pt.Y).ToList();
            var xs = top.Points.Select(pt => pt.X).ToList();
            Assert.Equal(rect.Top - offset, ys.Max(), 1);
            Assert.Equal(rect.Top - reach, ys.Min(), 1);
            Assert.Equal(rect.Left - reach, xs.Min(), 1);
            Assert.Equal(rect.Right + reach, xs.Max(), 1);

            var right = polys.OrderByDescending(p => p.Points.Average(pt => pt.X)).First();
            var rxs = right.Points.Select(pt => pt.X).ToList();
            Assert.Equal(rect.Right + offset, rxs.Min(), 1);
            Assert.Equal(rect.Right + reach, rxs.Max(), 1);
        }

        [Fact]
        public async Task OutlineOffset_Negative_PullsTheRingInwardPastTheBorderEdge()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:50pt; height:30pt; outline: 6pt solid rgb(10,20,30); outline-offset: -3pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            var rect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            var top = polys.OrderBy(p => p.Points.Average(pt => pt.Y)).First();
            var ys = top.Points.Select(pt => pt.Y).ToList();

            // offset = -3, width = 6: the ring's inner boundary sits 3pt *inside* the border edge and
            // its outer boundary sits 3pt outside it - straddling the box's own edge.
            Assert.Equal(rect.Top + 3, ys.Max(), 1);
            Assert.Equal(rect.Top - 3, ys.Min(), 1);
        }

        /// <summary>
        /// The UA ring's thickness in points - 2 CSS px, the width Chrome draws an <c>auto</c> outline
        /// at whatever the author declares. Layout space is 1pt here (the harness leaves
        /// <c>PixelsPerInch</c> at its 72 default), so no <c>PixelsPerPoint</c> factor applies.
        /// </summary>
        private const double AutoRingWidthPt = 2 * 0.75;

        [Theory]
        [InlineData("outline-width: 5pt")]
        [InlineData("outline-width: 1px")]
        [InlineData("outline-width: 0")]
        [InlineData("outline-width: 20px")]
        [InlineData("outline-width: thick")]
        [InlineData("")]
        public async Task OutlineStyleAuto_IgnoresTheDeclaredOutlineWidth(string widthDecl)
        {
            // CSS-UI-4 4: "The outline-width property is ignored when outline-style is auto." The
            // sentence is normative and unconditional, so every declaration here - including a declared
            // zero, and including no declaration at all - has to produce the very same ring. Measured
            // against Chrome, which renders 0/1px/8px/20px auto at an identical 2 CSS px.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='width:40pt; height:40pt; outline-style: auto; {widthDecl}; " +
                "outline-color: rgb(9,9,9)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            Assert.Equal(OutlineStyle.Auto, div.OutlineStyle.Value);
            var rect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, polys.Count);
            Assert.All(polys, p => Assert.Equal(RColor.FromArgb(9, 9, 9), p.Color));

            // Chrome centres the auto ring on the rectangle outline-offset inflates the border box to,
            // rather than seating it wholly outside that rectangle the way every other style sits - so
            // at the default zero offset it straddles the border edge, half a ring either side.
            var half = AutoRingWidthPt / 2;
            var top = polys.OrderBy(p => p.Points.Average(pt => pt.Y)).First();
            var ys = top.Points.Select(pt => pt.Y).ToList();
            Assert.Equal(rect.Top + half, ys.Max(), 3);
            Assert.Equal(rect.Top - half, ys.Min(), 3);

            var right = polys.OrderByDescending(p => p.Points.Average(pt => pt.X)).First();
            var rxs = right.Points.Select(pt => pt.X).ToList();
            Assert.Equal(rect.Right - half, rxs.Min(), 3);
            Assert.Equal(rect.Right + half, rxs.Max(), 3);
        }

        [Fact]
        public async Task OutlineStyleAuto_PaintsAsSolid_AtTheUaWidthCentredOnTheOffsetEdge()
        {
            // "User agents may treat auto as solid" licenses the style, not the width - so auto is the
            // same four mitred solid quads any solid outline paints, but at the UA's own width and
            // centred on the offset edge. An equal-width solid outline pulled back half a width is
            // exactly that ring, which makes solid the oracle for auto's geometry.
            const string box = "width:40pt; height:40pt; outline-color: rgb(9,9,9)";

            var (autoRoot, autoContainer) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='{box}; outline-style: auto; outline-width: 5pt; outline-offset: 7pt'>x</div>"));
            var autoG = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(autoContainer, LayoutHarness.FindById(autoRoot, "b")!, autoG);

            // Invariant culture, deliberately: a decimal comma reaches the parser as invalid CSS, and
            // outline-width/-offset then silently fall back to medium/0 - a green test that compares
            // auto against the wrong ring entirely.
            var solidWidth = AutoRingWidthPt.ToString(CultureInfo.InvariantCulture);
            var solidOffset = (7 - AutoRingWidthPt / 2).ToString(CultureInfo.InvariantCulture);
            var (solidRoot, solidContainer) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='{box}; outline-style: solid; outline-width: {solidWidth}pt; " +
                $"outline-offset: {solidOffset}pt'>x</div>"));
            var solidG = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(solidContainer, LayoutHarness.FindById(solidRoot, "b")!, solidG);

            var autoPolys = autoG.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            var solidPolys = solidG.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, autoPolys.Count);
            Assert.Equal(solidPolys.Select(p => p.Color), autoPolys.Select(p => p.Color));
            Assert.Equal(solidPolys.Select(p => p.Points), autoPolys.Select(p => p.Points));
        }

        [Fact]
        public async Task OutlineStyleAuto_RingWidth_IsInvariantUnderNonDefaultPixelsPerInch()
        {
            // Chrome's ring is 2 *CSS* px, not 2 device px - it measures 4 device px at a 2x device
            // scale factor. The UA width is therefore a CSS length like any other and has to carry the
            // same PixelsPerPoint inflation a declared width would have (issue #856's correction,
            // applied to a constant this time rather than to a cascaded value).
            const string html = "<div id='b' style='width:40pt; height:40pt; outline-style: auto; " +
                                "outline-color: rgb(9,9,9)'>x</div>";

            static double RingThickness(TestRecordingGraphics g, double pixelsPerPoint)
            {
                var top = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                    .OrderBy(p => p.Points.Average(pt => pt.Y)).First();
                var ys = top.Points.Select(pt => pt.Y).ToList();
                return (ys.Max() - ys.Min()) / pixelsPerPoint;
            }

            var (rootDefault, containerDefault) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var gDefault = new TestRecordingGraphics { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintBox(containerDefault, LayoutHarness.FindById(rootDefault, "b")!, gDefault);

            var (rootScaled, containerScaled) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(html), pixelsPerPoint: 2.0);
            var gScaled = new TestRecordingGraphics { PixelsPerPointOverride = 2.0 };
            FragmentPaintHarness.PaintBox(containerScaled, LayoutHarness.FindById(rootScaled, "b")!, gScaled);

            Assert.Equal(AutoRingWidthPt, RingThickness(gDefault, 1.0), 3);
            Assert.Equal(AutoRingWidthPt, RingThickness(gScaled, 2.0), 3);
        }

        [Fact]
        public async Task OutlineColorCurrentColor_ResolvesToTheElementsOwnColor()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; color: rgb(7,8,9); outline: 3pt solid'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            // CssUtils.ApplyCurrentColor already rewrites the initial "currentcolor" to the box's own
            // resolved color during cascade (the same mechanism border-*-color relies on).
            Assert.Equal("rgb(7, 8, 9)", div.OutlineColor);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, polys.Count);
            Assert.All(polys, p => Assert.Equal(RColor.FromArgb(7, 8, 9), p.Color));
        }

        [Fact]
        public async Task OutlineColorInvert_PaintsWhiteWrappedInADifferenceBlendMode()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; outline: 3pt solid invert'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            Assert.Equal("invert", div.OutlineColor);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var pushIndex = g.Log.FindIndex(e => e is TestRecordingGraphics.PushBlendModeCall);
            var popIndex = g.Log.FindIndex(e => e is TestRecordingGraphics.PopBlendModeCall);
            Assert.True(pushIndex >= 0, "expected a PushBlendMode call");
            Assert.True(popIndex > pushIndex, "expected PopBlendMode to follow PushBlendMode");
            Assert.Equal(RBlendMode.Difference, ((TestRecordingGraphics.PushBlendModeCall)g.Log[pushIndex]).Mode);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, polys.Count);
            Assert.All(polys, p => Assert.Equal(RColor.White, p.Color));

            // Every outline draw call sits inside the push/pop bracket.
            var firstPolyIndex = g.Log.FindIndex(e => e is TestRecordingGraphics.DrawPolygonCall);
            var lastPolyIndex = g.Log.FindLastIndex(e => e is TestRecordingGraphics.DrawPolygonCall);
            Assert.True(firstPolyIndex > pushIndex);
            Assert.True(lastPolyIndex < popIndex);
        }

        [Fact]
        public async Task OutlineColorInvert_ProducesADifferenceBlendModeExtGStateInTheRealPdf()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div style="width: 40pt; height: 40pt; background: #ff0000; outline: 6pt solid invert; outline-offset: 2pt;"></div>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/BM /Difference", pdfText);
        }

        [Fact]
        public async Task Outline_PaintsAfterBorder_InPaintOrder()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; border: 2pt solid rgb(1,1,1); outline: 3pt solid rgb(2,2,2)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            // The border is uniform, so it paints as one ring path; the outline's four sides differ in
            // nothing but still paint as quads. Match on color rather than on which primitive was used.
            static bool IsFill(object entry, RColor color) => entry switch
            {
                TestRecordingGraphics.DrawPolygonCall p => p.Color == color,
                TestRecordingGraphics.DrawPathCall { Stroked: false } p => p.Color == color,
                _ => false
            };

            var lastBorderIndex = g.Log.FindLastIndex(e => IsFill(e, RColor.FromArgb(1, 1, 1)));
            var firstOutlineIndex = g.Log.FindIndex(e => IsFill(e, RColor.FromArgb(2, 2, 2)));

            Assert.True(lastBorderIndex >= 0);
            Assert.True(firstOutlineIndex >= 0);
            Assert.True(firstOutlineIndex > lastBorderIndex, "expected outline to paint after border");
        }

        [Fact]
        public async Task Outline_PaintsAfterItsOwnTextAndDescendants_NotJustAfterBorder()
        {
            // CSS-UI-4 §4: outline "is drawn 'over' a box" - CSS2.1 Appendix E draws it as the very last
            // step for the element, after its own generated content AND its whole stacking context (every
            // descendant). A negative outline-offset large enough to reach over the box's own text makes
            // this observable: the outline ring must still end up on top of that text, not underneath it.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:100pt; height:30pt; font:10pt Arial; outline: 20pt solid rgb(2,2,2); " +
                "outline-offset: -25pt; color: rgb(9,9,9)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var lastTextIndex = g.Log.FindLastIndex(e => e is TestRecordingGraphics.DrawStringCall);
            var firstOutlineIndex = g.Log.FindIndex(e => e is TestRecordingGraphics.DrawPolygonCall p && p.Color == RColor.FromArgb(2, 2, 2));

            Assert.True(lastTextIndex >= 0);
            Assert.True(firstOutlineIndex >= 0);
            Assert.True(firstOutlineIndex > lastTextIndex, "expected outline to paint after the box's own text");
        }

        [Theory]
        [InlineData("dotted")]
        [InlineData("dashed")]
        public async Task OutlineStyleDottedOrDashed_DrawsOneMidBandLinePerSide(string style)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='width:120pt; height:120pt; outline: 8pt {style} rgb(3,3,3); outline-offset: 2pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var dotted = style == "dotted";
            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.Equal(4, lines.Count);
            Assert.All(lines, l => Assert.Equal(RColor.FromArgb(3, 3, 3), l.Color));
            Assert.All(lines, l => Assert.Equal(8, l.Width, 1));

            // A dot is a zero-length dash under a round cap; a dash is a real segment under a butt cap.
            // The pattern is fitted to each side, so its exact lengths depend on the side - but the cap
            // and the zero-vs-nonzero dash are what tell the two styles apart at all.
            Assert.All(lines, l => Assert.Equal(dotted ? RLineCap.Round : RLineCap.Butt, l.LineCap));
            Assert.All(lines, l =>
            {
                Assert.NotNull(l.DashPattern);
                Assert.Equal(2, l.DashPattern!.Count);
                if (dotted)
                    Assert.Equal(0, l.DashPattern[0]);
                else
                    Assert.Equal(16, l.DashPattern[0], 1); // dashed = 2x the outline width
            });
        }

        [Theory]
        [InlineData("dotted")]
        [InlineData("dashed")]
        public async Task OutlineStyleDottedOrDashed_LinesSpanTheRingsOuterEdge(string style)
        {
            // Chrome paints an outline by handing its border painter an outer rectangle inflated by
            // outline-offset + outline-width, so each side's line spans that outer rectangle's full
            // side - corner squares included - exactly as a border's own dotted edge spans
            // rect.Left..rect.Right. That span is what the dash pattern is fitted to, so a line stopped
            // at the mid-band distance instead would be fitted to an edge one whole outline-width
            // short: wrong gap sizes, and the first and last dot pulled half a width in from the corner.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='width:20pt; height:20pt; outline: 8pt {style} rgb(3,3,3); outline-offset: 2pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            var rect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            const double offset = 2;
            const double width = 8;
            var mid = offset + width / 2;
            var reach = offset + width;

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            var top = lines.Single(l => l.Y1 == l.Y2 && l.Y1 < rect.Top);
            var left = lines.Single(l => l.X1 == l.X2 && l.X1 < rect.Left);

            // A dotted path runs dot-centre to dot-centre, so its endpoint sits half a dot inside the
            // outer corner - and the round cap then paints that half back out to it. What has to reach
            // the corner is the ink, not the path, so compare the inked reach for both styles.
            var inset = style == "dotted" ? width / 2 : 0;

            // Each line sits on its own band's centreline, but starts at the outer rectangle's corner,
            // so the two overlap across the whole corner square and leave no hole.
            Assert.Equal(rect.Top - mid, top.Y1, 1);
            Assert.Equal(rect.Left - reach, top.X1 - inset, 1);
            Assert.Equal(rect.Left - mid, left.X1, 1);
            Assert.Equal(rect.Top - reach, left.Y1 - inset, 1);
        }

        [Theory]
        [InlineData("dotted")]
        [InlineData("dashed")]
        public async Task OutlineStyleDottedOrDashed_MatchesTheEquivalentBorder(string style)
        {
            // An outline-offset of -outline-width lands the ring exactly where an equal-width border
            // would sit, so the two must produce identical strokes: same centrelines, same span, and
            // therefore the same fitted dash pattern. Border's own dotted/dashed rendering was measured
            // against Chrome (PR #1126), which makes it the reference the outline path has to agree
            // with - and the fitted pattern only agrees if both measure the same edge length.
            const string box = "width:120pt; height:60pt; margin:0";

            var (borderRoot, borderContainer) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='{box}; border: 6pt {style} rgb(3,3,3)'>x</div>"));
            var borderG = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(borderContainer, LayoutHarness.FindById(borderRoot, "b")!, borderG);

            var (outlineRoot, outlineContainer) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='{box}; border: 6pt solid transparent; " +
                $"outline: 6pt {style} rgb(3,3,3); outline-offset: -6pt'>x</div>"));
            var outlineG = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(outlineContainer, LayoutHarness.FindById(outlineRoot, "b")!, outlineG);

            var borderLines = borderG.Log.OfType<TestRecordingGraphics.DrawLineCall>()
                .Where(l => l.Color == RColor.FromArgb(3, 3, 3)).ToList();
            var outlineLines = outlineG.Log.OfType<TestRecordingGraphics.DrawLineCall>()
                .Where(l => l.Color == RColor.FromArgb(3, 3, 3)).ToList();

            Assert.Equal(4, borderLines.Count);
            Assert.Equal(4, outlineLines.Count);

            static (double, double, double, double, double, double) Key(TestRecordingGraphics.DrawLineCall l) =>
                (Math.Round(l.X1, 3), Math.Round(l.Y1, 3), Math.Round(l.X2, 3), Math.Round(l.Y2, 3),
                 Math.Round(l.DashPattern![0], 3), Math.Round(l.DashPattern[1], 3));

            Assert.Equal(borderLines.Select(Key).OrderBy(k => k), outlineLines.Select(Key).OrderBy(k => k));
        }

        [Fact]
        public async Task OutlineStyleDouble_DrawsTwoEqualWidthStripesWithGap()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; outline: 12pt double rgb(51,51,51)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            var rect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            // Every band strictly above the box's own top edge belongs to the top side's pair of rings -
            // unambiguous, since the bottom side's rings sit strictly below rect.Bottom. Draw order (not
            // Y order) distinguishes them: the near-the-box ring is always drawn first
            // (OutlineDrawHandler.DrawDoubleOrGrooveRidge).
            var topBands = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                .Select(Band)
                .Where(b => b.Bottom <= rect.Top + 0.01)
                .ToList();
            Assert.Equal(2, topBands.Count);

            var nearBox = topBands[0];
            var farFromBox = topBands[1];
            Assert.Equal(RColor.FromArgb(51, 51, 51), nearBox.Color);
            Assert.Equal(RColor.FromArgb(51, 51, 51), farFromBox.Color);

            // CSS 2.1 §8.5.3's exact thirds: 4pt ring, 4pt gap, 4pt ring out of 12pt.
            Assert.Equal(4, nearBox.Height, 2);
            Assert.Equal(4, farFromBox.Height, 2);
            Assert.Equal(4, nearBox.Top - farFromBox.Bottom, 2);
        }

        [Fact]
        public async Task OutlineStyleGroove_ShadesEachHalfLikeAnInsetThenAnOutsetRing()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; outline: 12pt groove rgb(51,51,51)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            var rect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var topBands = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                .Select(Band)
                .Where(b => b.Bottom <= rect.Top + 0.01)
                .OrderBy(b => b.Top)
                .ToList();
            Assert.Equal(2, topBands.Count);

            // groove's outer half (farthest from the box) paints as `inset`, its inner half as `outset`.
            // On a top edge inset is the darkened face - the same rule border uses, so the two agree.
            Assert.Equal(BorderBevelColors.Shade(RColor.FromArgb(51, 51, 51), darken: true), topBands[0].Color);
            Assert.Equal(BorderBevelColors.Shade(RColor.FromArgb(51, 51, 51), darken: false), topBands[1].Color);
            Assert.NotEqual(topBands[0].Color, topBands[1].Color);
        }

        [Fact]
        public async Task OutlineStyleInset_DarkensTopAndLeft_LightensRightAndBottom()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; outline: 6pt inset rgb(100,100,100)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, polys.Count);

            var top = polys.OrderBy(p => p.Points.Average(pt => pt.Y)).First();
            var left = polys.OrderBy(p => p.Points.Average(pt => pt.X)).First();
            var right = polys.OrderByDescending(p => p.Points.Average(pt => pt.X)).First();
            var bottom = polys.OrderByDescending(p => p.Points.Average(pt => pt.Y)).First();

            var baseColor = RColor.FromArgb(100, 100, 100);
            var dark = BorderBevelColors.Shade(baseColor, darken: true);
            var light = BorderBevelColors.Shade(baseColor, darken: false);

            // The lit pair is genuinely lightened rather than left at the declared color, matching what
            // a browser paints - and matching border, which shares the same shading.
            Assert.NotEqual(baseColor, light);
            Assert.Equal(dark, top.Color);
            Assert.Equal(dark, left.Color);
            Assert.Equal(light, right.Color);
            Assert.Equal(light, bottom.Color);
        }

        [Fact]
        public async Task OutlineStyleOutset_DarkensRightAndBottom_LightensTopAndLeft()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; outline: 6pt outset rgb(100,100,100)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, polys.Count);

            var top = polys.OrderBy(p => p.Points.Average(pt => pt.Y)).First();
            var left = polys.OrderBy(p => p.Points.Average(pt => pt.X)).First();
            var right = polys.OrderByDescending(p => p.Points.Average(pt => pt.X)).First();
            var bottom = polys.OrderByDescending(p => p.Points.Average(pt => pt.Y)).First();

            var baseColor = RColor.FromArgb(100, 100, 100);
            var dark = BorderBevelColors.Shade(baseColor, darken: true);
            var light = BorderBevelColors.Shade(baseColor, darken: false);

            Assert.Equal(light, top.Color);
            Assert.Equal(light, left.Color);
            Assert.Equal(dark, right.Color);
            Assert.Equal(dark, bottom.Color);
        }

        [Fact]
        public async Task OutlineStyleNone_ZeroesActualOutlineWidth_EvenWithANonZeroDeclaredWidth()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='outline-style: none; outline-width: 6pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            // DerivedStyle.ActualOutlineWidth zeroes itself when the style is None, independent of
            // OutlineDrawHandler's own early-out on style - a direct property read (not painting)
            // exercises that branch.
            Assert.Equal(0, div.ActualOutlineWidth);
        }

        [Theory]
        [InlineData("thin")]
        [InlineData("medium")]
        [InlineData("thick")]
        public async Task OutlineWidthKeywords_ResolveTheSamePixelValuesAsBorder(string keyword)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='outline-style: solid; outline-width: {keyword}; border-style: solid; border-width: {keyword}'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            // Both reuse CssValueParser.GetActualBorderWidth against the same keyword, so they must
            // resolve to the exact same pixel value as each other, whatever it is.
            Assert.Equal(div.ActualBorderTopWidth, div.ActualOutlineWidth, 3);
            Assert.True(div.ActualOutlineWidth > 0);
        }

        [Theory]
        [InlineData("none")]
        [InlineData("hidden")]
        public async Task OutlineStyleNoneOrHidden_PaintsNothing(string style)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='width:20pt; height:20pt; outline-style: {style}; outline-width: 6pt; outline-color: red'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        [Fact]
        public async Task OutlineWidthZero_PaintsNothing()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; outline: 0 solid red'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        [Fact]
        public async Task OutlineOnAWrappingInlineElement_OnlyPaintsTheEdgesEachLineOwns()
        {
            // A span forced onto three lines - mirrors BoxDecorationBreakPaintIntegrationTests' own
            // "Slice_WrappingInline_DrawsNoBorderAtABreak", since outline is gated by the exact same
            // per-line HasLeftEdge/HasRightEdge flags FragmentPainter already computes for border.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                "<span id='s' style='outline:2pt solid #00f'>Alpha<br>Beta<br>Gamma</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var blue = RColor.FromArgb(0, 0, 255);
            var vertical = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                .Where(p => p.Color == blue && IsVerticalEdge(p.Points))
                .ToList();

            // The leading edge draws once (first line) and the trailing edge once (last line) - never
            // at either of the two internal wrap points.
            Assert.Equal(2, vertical.Count);
        }

        [Fact]
        public async Task Outline_DoesNotAffectLayout()
        {
            var withoutOutline = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='width:30pt; height:20pt;'>x</div>" +
                "<div id='b' style='width:30pt; height:20pt;'>y</div>"));
            var aWithout = LayoutHarness.FindById(withoutOutline.Root, "a")!;
            var bWithout = LayoutHarness.FindById(withoutOutline.Root, "b")!;

            var withOutline = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='width:30pt; height:20pt; outline: 20pt solid red; outline-offset: 15pt;'>x</div>" +
                "<div id='b' style='width:30pt; height:20pt;'>y</div>"));
            var aWith = LayoutHarness.FindById(withOutline.Root, "a")!;
            var bWith = LayoutHarness.FindById(withOutline.Root, "b")!;

            // A large outline (offset+width = 35pt, comfortably wider than the box itself) visually
            // overlaps the sibling below, but must not shift it - outline is layout-neutral by spec.
            Assert.Equal(aWithout.Location, aWith.Location);
            Assert.Equal(aWithout.Size, aWith.Size);
            Assert.Equal(bWithout.Location, bWith.Location);
            Assert.Equal(bWithout.Size, bWith.Size);
        }

        // ─── issue #856: pen stroke width ignores non-default PixelsPerInch ────────

        [Fact]
        public async Task OutlineStyleDotted_PenWidth_IsInvariantUnderNonDefaultPixelsPerInch()
        {
            const string html = "<div id='b' style='width:20pt; height:20pt; outline: 8pt dotted rgb(3,3,3)'>x</div>";

            var (rootDefault, containerDefault) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var divDefault = LayoutHarness.FindById(rootDefault, "b")!;
            var gDefault = new TestRecordingGraphics { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintBox(containerDefault, divDefault, gDefault);
            var widthsDefault = gDefault.Log.OfType<TestRecordingGraphics.DrawLineCall>().Select(l => l.Width).ToList();

            var (rootScaled, containerScaled) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html), pixelsPerPoint: 2.0);
            var divScaled = LayoutHarness.FindById(rootScaled, "b")!;
            var gScaled = new TestRecordingGraphics { PixelsPerPointOverride = 2.0 };
            FragmentPaintHarness.PaintBox(containerScaled, divScaled, gScaled);
            var widthsScaled = gScaled.Log.OfType<TestRecordingGraphics.DrawLineCall>().Select(l => l.Width).ToList();

            Assert.NotEmpty(widthsDefault);
            Assert.Equal(widthsDefault.Count, widthsScaled.Count);
            for (var i = 0; i < widthsDefault.Count; i++)
                Assert.Equal(widthsDefault[i], widthsScaled[i], 3);

            // Sanity: the pen width should actually equal the declared 8pt outline width.
            Assert.All(widthsDefault, w => Assert.Equal(8, w, 1));
        }

        [Fact]
        public async Task OutlineStyleDoubleAndGroove_StripeWidths_AreInvariantUnderNonDefaultPixelsPerInch()
        {
            const string html = "<div id='b' style='width:20pt; height:20pt; outline: 12pt double rgb(51,51,51)'>x</div>";

            var (rootDefault, containerDefault) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var divDefault = LayoutHarness.FindById(rootDefault, "b")!;
            var gDefault = new TestRecordingGraphics { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintBox(containerDefault, divDefault, gDefault);
            var bandsDefault = gDefault.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().Select(Band).ToList();

            var (rootScaled, containerScaled) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html), pixelsPerPoint: 2.0);
            var divScaled = LayoutHarness.FindById(rootScaled, "b")!;
            var gScaled = new TestRecordingGraphics { PixelsPerPointOverride = 2.0 };
            FragmentPaintHarness.PaintBox(containerScaled, divScaled, gScaled);
            var bandsScaled = gScaled.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().Select(Band).ToList();

            // double contributes 2 rings per side, 4 sides.
            Assert.Equal(8, bandsDefault.Count);
            Assert.Equal(bandsDefault.Count, bandsScaled.Count);

            // A band is a polygon, so its coordinates stay in layout space and the adapter divides them
            // on the way out - meaning the scaled run SHOULD be exactly PixelsPerPoint larger. What this
            // guards is a thickness that skipped or double-applied that correction (issue #851).
            for (var i = 0; i < bandsDefault.Count; i++)
                Assert.Equal(bandsDefault[i].Thickness, bandsScaled[i].Thickness / 2.0, 3);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>A filled ring band's colour and axis-aligned bounds.</summary>
        private readonly record struct BandInfo(RColor Color, double Left, double Top, double Width, double Height)
        {
            public double Bottom => Top + Height;

            /// <summary>The band's minor axis - its thickness across the ring, independent of how long
            /// the side it belongs to happens to be.</summary>
            public double Thickness => System.Math.Min(Width, Height);
        }

        private static BandInfo Band(TestRecordingGraphics.DrawPolygonCall p)
        {
            var left = p.Points.Min(pt => pt.X);
            var top = p.Points.Min(pt => pt.Y);
            return new BandInfo(p.Color, left, top, p.Points.Max(pt => pt.X) - left, p.Points.Max(pt => pt.Y) - top);
        }

        private static bool IsVerticalEdge(System.Collections.Generic.IReadOnlyList<RPoint> points)
        {
            var width = points.Max(p => p.X) - points.Min(p => p.X);
            var height = points.Max(p => p.Y) - points.Min(p => p.Y);
            return height > width;
        }

        private static async Task<string> GetPdfText(string html)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }
    }
}
