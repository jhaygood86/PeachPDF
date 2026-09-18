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

            var ring = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                p => !p.Stroked && p.Color == RColor.FromArgb(10, 20, 30));
            Assert.DoesNotContain(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>(),
                p => p.Color == RColor.FromArgb(10, 20, 30));

            const double offset = 4;
            const double width = 6;
            const double reach = offset + width;

            var outer = ring.Points.Take(4).ToList();
            var inner = ring.Points.Skip(4).Take(4).ToList();
            Assert.Equal(rect.Top - reach, outer.Min(p => p.Y), 1);
            Assert.Equal(rect.Left - reach, outer.Min(p => p.X), 1);
            Assert.Equal(rect.Right + reach, outer.Max(p => p.X), 1);
            Assert.Equal(rect.Top - offset, inner.Min(p => p.Y), 1);
            Assert.Equal(rect.Right + offset, inner.Max(p => p.X), 1);
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

            var ring = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                p => !p.Stroked && p.Color == RColor.FromArgb(10, 20, 30));
            var outerTop = ring.Points.Take(4).Min(p => p.Y);
            var innerTop = ring.Points.Skip(4).Take(4).Min(p => p.Y);

            // offset = -3, width = 6: the ring's inner boundary sits 3pt *inside* the border edge and
            // its outer boundary sits 3pt outside it - straddling the box's own edge.
            Assert.Equal(rect.Top + 3, innerTop, 1);
            Assert.Equal(rect.Top - 3, outerTop, 1);
        }

        [Theory]
        [InlineData(20, 20, -20, 8, 8)]
        [InlineData(8, 40, -10, 8, 28)]
        public async Task OutlineOffset_LargeNegative_KeepsOutsideShapeAtLeastTwiceTheOutlineWidth(
            double boxWidth, double boxHeight, double offset, double expectedWidth, double expectedHeight)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='width:{boxWidth}pt; height:{boxHeight}pt; " +
                $"outline:4pt solid rgb(10,20,30); outline-offset:{offset}pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var ring = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                p => !p.Stroked && p.Color == RColor.FromArgb(10, 20, 30));
            var outer = ring.Points.Take(4).ToList();

            Assert.Equal(expectedWidth, outer.Max(p => p.X) - outer.Min(p => p.X), 1);
            Assert.Equal(expectedHeight, outer.Max(p => p.Y) - outer.Min(p => p.Y), 1);
        }

        [Fact]
        public async Task RoundedSolidOutline_ExpandsTheBorderRadiusWithItsOffsetAndWidth()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:80pt; height:50pt; border-radius:20pt; " +
                "outline:6pt solid rgb(10,20,30); outline-offset:4pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            var rect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var outline = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                p => p.Stroked && p.Color == RColor.FromArgb(10, 20, 30));
            Assert.True(outline.Points.Count > 8);
            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());

            // Outer radius = 20 + offset 4 + width 6 = 30. The 6pt stroke's centerline is
            // inset 3pt from that contour, hence a 27pt centerline radius on a rectangle inflated 7pt.
            Assert.Equal(rect.Left - 7, outline.Bounds.Left, 2);
            Assert.Equal(rect.Top - 7, outline.Bounds.Top, 2);
            Assert.Equal(rect.Left + 20, outline.Points[0].X, 2);
            Assert.Equal(rect.Top - 7, outline.Points[0].Y, 2);
        }

        [Fact]
        public async Task AsymmetricRoundedSolidOutline_MatchesTheEquivalentBorder()
        {
            const string box =
                "width:80pt; height:50pt; margin:0; border-top-left-radius:20pt";
            var color = RColor.FromArgb(10, 20, 30);

            var (borderRoot, borderContainer) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='{box}; border:6pt solid rgb(10,20,30)'>x</div>"));
            var borderG = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(
                borderContainer, LayoutHarness.FindById(borderRoot, "b")!, borderG);

            var (outlineRoot, outlineContainer) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='{box}; border:6pt solid transparent; " +
                "outline:6pt solid rgb(10,20,30); outline-offset:-6pt'>x</div>"));
            var outlineG = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(
                outlineContainer, LayoutHarness.FindById(outlineRoot, "b")!, outlineG);

            var borderPath = Assert.Single(
                borderG.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                path => path.Stroked && path.Color == color);
            var outlinePath = Assert.Single(
                outlineG.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                path => path.Stroked && path.Color == color);

            Assert.Equal(borderPath.StrokeWidth, outlinePath.StrokeWidth);
            Assert.Equal(borderPath.Points, outlinePath.Points);
        }

        [Fact]
        public async Task RoundedOutline_NegativeOffsetPastRadius_BecomesSquare()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:80pt; height:50pt; border-radius:2pt; " +
                "outline:4pt solid rgb(10,20,30); outline-offset:-10pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var outline = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                path => !path.Stroked && path.Color == RColor.FromArgb(10, 20, 30));
            Assert.Equal(8, outline.Points.Count);
        }

        [Theory]
        [InlineData("solid")]
        [InlineData("dashed")]
        [InlineData("dotted")]
        [InlineData("double")]
        [InlineData("groove")]
        [InlineData("ridge")]
        [InlineData("inset")]
        [InlineData("outset")]
        public async Task RoundedOutline_AllLineStylesUseCurvedPaths(string style)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='width:80pt; height:50pt; border-radius:20pt; " +
                $"outline:8pt {style} rgb(51,51,51); outline-offset:3pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var paths = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();
            Assert.NotEmpty(paths);
            Assert.All(paths, path => Assert.True(path.Points.Count > 8));
            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
        }

        [Theory]
        [InlineData("groove")]
        [InlineData("ridge")]
        [InlineData("inset")]
        [InlineData("outset")]
        public async Task RoundedBevelOutline_BuildsCurvedSideBandsWithoutRectangularClips(string style)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='width:80pt; height:50pt; border-radius:20pt; " +
                $"outline:8pt {style} rgb(51,51,51); outline-offset:3pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            // A rounded ring clipped by rectangular side trapezoids looks plausible in the recording
            // mock but exposes square inner corners in the PDF backend. Each colored side must instead
            // be an intrinsically curved band path, exactly as for a rounded border.
            Assert.Empty(g.Log.OfType<TestRecordingGraphics.PushClipCall>());
            var bands = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();
            Assert.Equal(style is "groove" or "ridge" ? 4 : 2, bands.Count);
            Assert.All(bands, band =>
            {
                Assert.False(band.Stroked);
                Assert.True(band.Points.Count > 16);
            });
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

            var ring = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                p => !p.Stroked && p.Color == RColor.FromArgb(9, 9, 9));

            // Chrome centres the auto ring on the rectangle outline-offset inflates the border box to,
            // rather than seating it wholly outside that rectangle the way every other style sits - so
            // at the default zero offset it straddles the border edge, half a ring either side.
            var half = AutoRingWidthPt / 2;
            var outer = ring.Points.Take(4).ToList();
            var inner = ring.Points.Skip(4).Take(4).ToList();
            Assert.Equal(rect.Top - half, outer.Min(p => p.Y), 3);
            Assert.Equal(rect.Top + half, inner.Min(p => p.Y), 3);
            Assert.Equal(rect.Right + half, outer.Max(p => p.X), 3);
            Assert.Equal(rect.Right - half, inner.Max(p => p.X), 3);
        }

        [Fact]
        public async Task OutlineStyleAuto_PaintsAsSolid_AtTheUaWidthCentredOnTheOffsetEdge()
        {
            // "User agents may treat auto as solid" licenses the style, not the width - so auto is the
            // same seam-free solid ring any solid outline paints, but at the UA's own width and
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

            var autoRing = Assert.Single(autoG.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                p => !p.Stroked && p.Color == RColor.FromArgb(9, 9, 9));
            var solidRing = Assert.Single(solidG.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                p => !p.Stroked && p.Color == RColor.FromArgb(9, 9, 9));
            Assert.Equal(solidRing.Points, autoRing.Points);
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

            static double RingThickness(TestRecordingGraphics g)
            {
                var ring = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                    .Single(p => !p.Stroked && p.Color == RColor.FromArgb(9, 9, 9));
                return ring.Points.Skip(4).Take(4).Min(p => p.Y) - ring.Points.Take(4).Min(p => p.Y);
            }

            var (rootDefault, containerDefault) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var gDefault = new TestRecordingGraphics { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintBox(containerDefault, LayoutHarness.FindById(rootDefault, "b")!, gDefault);

            var (rootScaled, containerScaled) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(html), pixelsPerPoint: 2.0);
            var gScaled = new TestRecordingGraphics { PixelsPerPointOverride = 2.0 };
            FragmentPaintHarness.PaintBox(containerScaled, LayoutHarness.FindById(rootScaled, "b")!, gScaled);

            Assert.Equal(AutoRingWidthPt, RingThickness(gDefault), 3);
            Assert.Equal(AutoRingWidthPt, RingThickness(gScaled), 3);
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

            Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                p => !p.Stroked && p.Color == RColor.FromArgb(7, 8, 9));
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

            var ring = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                p => !p.Stroked && p.Color == RColor.White);

            // Every outline draw call sits inside the push/pop bracket.
            var ringIndex = g.Log.IndexOf(ring);
            Assert.True(ringIndex > pushIndex);
            Assert.True(ringIndex < popIndex);
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

            // Both the uniform border and the complete solid outline paint as one ring path. Match on
            // color so this assertion remains about paint order rather than path representation.
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
            var firstOutlineIndex = g.Log.FindIndex(e => e is TestRecordingGraphics.DrawPathCall { Stroked: false } p && p.Color == RColor.FromArgb(2, 2, 2));

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

            // Each stripe is one complete even-odd ring rather than four abutting side polygons.
            var rings = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(p => !p.Stroked && p.Color == RColor.FromArgb(51, 51, 51))
                .ToList();
            Assert.Equal(2, rings.Count);
            Assert.DoesNotContain(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>(),
                p => p.Color == RColor.FromArgb(51, 51, 51));

            var nearBox = rings.OrderByDescending(InnerTop).First();
            var farFromBox = rings.OrderBy(InnerTop).First();

            // CSS 2.1 §8.5.3's exact thirds: 4pt ring, 4pt gap, 4pt ring out of 12pt.
            Assert.Equal(4, RingThickness(nearBox), 2);
            Assert.Equal(4, RingThickness(farFromBox), 2);
            Assert.Equal(rect.Top, InnerTop(nearBox), 2);
            Assert.Equal(4, OuterTop(nearBox) - InnerTop(farFromBox), 2);
        }

        [Theory]
        [InlineData("groove", true)]
        [InlineData("ridge", false)]
        public async Task OutlineStyleGrooveRidge_GroupsEqualShadePairsWithoutCornerSeams(
            string style, bool outerIsInset)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='width:20pt; height:20pt; outline: 12pt {style} rgb(51,51,51)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
            var pairs = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(path => !path.Stroked)
                .ToList();
            Assert.Equal(4, pairs.Count);
            Assert.All(pairs, pair => Assert.Equal(6, pair.Points.Count));

            // groove's outer half (farthest from the box) paints as `inset`, its inner half as `outset`.
            // Each entry is one connected equal-shade side pair, so no same-color corner seam remains.
            var dark = BorderBevelColors.Shade(RColor.FromArgb(51, 51, 51), darken: true);
            var light = BorderBevelColors.Shade(RColor.FromArgb(51, 51, 51), darken: false);
            var outerTopLeft = outerIsInset ? dark : light;
            var outerBottomRight = outerIsInset ? light : dark;
            Assert.Equal(
                [outerTopLeft, outerBottomRight, outerBottomRight, outerTopLeft],
                pairs.Select(pair => pair.Color));
        }

        [Fact]
        public async Task OutlineStyleInset_DarkensTopAndLeft_LightensRightAndBottom()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; outline: 6pt inset rgb(100,100,100)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
            var pairs = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(path => !path.Stroked)
                .ToList();
            Assert.Equal(2, pairs.Count);
            Assert.All(pairs, pair => Assert.Equal(6, pair.Points.Count));

            var baseColor = RColor.FromArgb(100, 100, 100);
            var dark = BorderBevelColors.Shade(baseColor, darken: true);
            var light = BorderBevelColors.Shade(baseColor, darken: false);

            // The lit pair is genuinely lightened rather than left at the declared color, matching what
            // a browser paints - and matching border, which shares the same shading.
            Assert.NotEqual(baseColor, light);
            Assert.Equal(dark, pairs[0].Color);  // one connected top + left path
            Assert.Equal(light, pairs[1].Color); // one connected bottom + right path
        }

        [Fact]
        public async Task OutlineStyleOutset_DarkensRightAndBottom_LightensTopAndLeft()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; outline: 6pt outset rgb(100,100,100)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
            var pairs = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(path => !path.Stroked)
                .ToList();
            Assert.Equal(2, pairs.Count);
            Assert.All(pairs, pair => Assert.Equal(6, pair.Points.Count));

            var baseColor = RColor.FromArgb(100, 100, 100);
            var dark = BorderBevelColors.Shade(baseColor, darken: true);
            var light = BorderBevelColors.Shade(baseColor, darken: false);

            Assert.Equal(light, pairs[0].Color); // one connected top + left path
            Assert.Equal(dark, pairs[1].Color);  // one connected bottom + right path
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
        public async Task OutlineOnAWrappingInlineElement_PaintsOneConnectedShapeAcrossEveryLine()
        {
            // A span forced onto three lines. CSS Basic User Interface 4 §4 recommends a fragmented
            // outline be drawn as one fully connected shape rather than one left open - or closed
            // separately - at every wrap, which is also what Chromium does: it unions every fragment's
            // rectangle and traces the boundary of the region they cover. Here the three lines start at
            // the same left edge and abut vertically, so that region is a single connected stepped
            // polygon and exactly one path paints it - not three rings, and never a separate per-side
            // polygon the way an open edge set would need.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                "<span id='s' style='outline:2pt solid #00f'>Alpha<br>Beta<br>Gamma</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;
            var rects = FragmentPaintHarness.FragmentOf(container, span).Lines.Select(line => line.Rect).ToList();

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var blue = RColor.FromArgb(0, 0, 255);
            var shapes = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(p => !p.Stroked && p.Color == blue)
                .ToList();

            var shape = Assert.Single(shapes);
            Assert.DoesNotContain(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>(), p => p.Color == blue);

            // One connected contour, so the band is exactly two subpaths: the contour itself and the
            // inset copy that hollows it out. Three separate rings would be six.
            Assert.Equal(2, shape.SubpathStarts.Count);

            // That one shape spans the whole union: the widest line sets its right edge and the first
            // and last lines its top and bottom, each pushed out by the outline's full 2pt reach.
            Assert.Equal(rects.Min(r => r.Left) - 2, shape.Bounds.Left, 1);
            Assert.Equal(rects.Max(r => r.Right) + 2, shape.Bounds.Right, 1);
            Assert.Equal(rects.Min(r => r.Top) - 2, shape.Bounds.Top, 1);
            Assert.Equal(rects.Max(r => r.Bottom) + 2, shape.Bounds.Bottom, 1);
        }

        [Fact]
        public async Task OutlineAndBorderOnAWrappingInlineElement_OutlineUnionsEveryLine_BorderStaysOpenAtWraps()
        {
            // Proves the union is genuinely outline-specific, not a change to the shared
            // HasLeftEdge/HasRightEdge geometry border and background also read: the very same span,
            // painted in one pass, must show its border still open at both interior wrap points (the
            // pre-existing, correct box-decoration-break `slice` behavior css-break-3 §6.2 requires)
            // while its outline becomes one connected shape around all three lines.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                "<span id='s' style='border:2pt solid #f00;outline:2pt solid #00f'>Alpha<br>Beta<br>Gamma</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var red = RColor.FromArgb(255, 0, 0);
            var blue = RColor.FromArgb(0, 0, 255);

            // Border: still open at both wrap points - only the true leading and trailing edges draw
            // their own separate vertical side polygon.
            var borderVerticalSides = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                .Where(p => p.Color == red && IsVerticalEdge(p.Points))
                .ToList();
            Assert.Equal(2, borderVerticalSides.Count);

            // Outline: one connected shape over all three lines - two subpaths, the contour and the
            // inset copy hollowing it out - never a separate side polygon the way an open edge set
            // would produce, and never a ring per line.
            var outlineShapes = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(p => !p.Stroked && p.Color == blue)
                .ToList();
            var outline = Assert.Single(outlineShapes);
            Assert.Equal(2, outline.SubpathStarts.Count);
            Assert.DoesNotContain(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>(), p => p.Color == blue);
        }

        [Fact]
        public async Task RoundedOutlineOnAWrappingInlineElement_PaintsOneConnectedRoundedShape()
        {
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                "<span id='s' style='border-radius:6pt;outline:2pt solid #00f'>Alpha<br>Beta<br>Gamma</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;
            var rects = FragmentPaintHarness.FragmentOf(container, span).Lines.Select(line => line.Rect).ToList();

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var bluePaths = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(path => path.Color == RColor.FromArgb(0, 0, 255))
                .ToList();
            var shape = Assert.Single(bluePaths);

            // The union is filled, not stroked: a stepped contour and its inset copy bound a band of
            // constant thickness, which a single centerline stroke cannot express once the contour has
            // the concave corners a wrap introduces. No clip is needed either - the shape is built from
            // the fragments' own rectangles, so nothing spills outside it to clip away.
            Assert.False(shape.Stroked);
            Assert.All(
                g.Log.Select((entry, index) => (entry, index))
                    .Where(item => item.entry is TestRecordingGraphics.DrawPathCall path &&
                                   path.Color == RColor.FromArgb(0, 0, 255)),
                item => Assert.IsNotType<TestRecordingGraphics.PushClipCall>(g.Log[item.index - 1]));

            // Being a filled band rather than a centerline stroke, the recorded bounds reach the true
            // outer edge: the full offset (0) + width (2), not half of it.
            const double reach = 2;
            Assert.Equal(rects.Min(r => r.Top) - reach, shape.Bounds.Top, 1);
            Assert.Equal(rects.Max(r => r.Bottom) + reach, shape.Bounds.Bottom, 1);
            Assert.Equal(rects.Min(r => r.Left) - reach, shape.Bounds.Left, 1);
            Assert.Equal(rects.Max(r => r.Right) + reach, shape.Bounds.Right, 1);
        }

        [Fact]
        public async Task RoundedOutlineOnAWrappingInlineElement_LargeNegativeOffsetClampsEveryLineBeforeUnioning()
        {
            // The "never shrink past 2x the outline width"
            // floor OutlineOffset_LargeNegative_KeepsOutsideShapeAtLeastTwiceTheOutlineWidth proves for
            // an unbroken box is applied to each line's own rectangle first, and only the clamped
            // rectangles are unioned - matching Chromium, which likewise clamps per fragment before
            // building the region. Without that clamp an interior line's rectangle could be inflated
            // past zero size with nothing to stop it.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                "<span id='s' style='border-radius:6pt;outline:4pt solid #00f;outline-offset:-20pt'>" +
                "Alpha<br>Beta<br>Gamma</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var shapes = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(path => path.Color == RColor.FromArgb(0, 0, 255))
                .ToList();
            var shape = Assert.Single(shapes);

            // The clamp held per line - each contributed a 2 x 4pt = 8pt square rather than collapsing
            // to nothing - and the three clamped squares, no longer touching once shrunk that far from
            // their shared line edges, stayed three disjoint pieces of the region.
            Assert.Equal(6, shape.SubpathStarts.Count);
            foreach (var i in new[] { 0, 2, 4 })
            {
                Assert.Equal(8, shape.SubpathBounds(i).Width, 1);
                Assert.Equal(8, shape.SubpathBounds(i).Height, 1);
            }

            var pdf = await new PdfGenerator().GeneratePdf(html, PageSize.A4);
            using var stream = new MemoryStream();
            pdf.Save(stream);
            Assert.True(stream.Length > 0);
        }

        [Fact]
        public async Task OutlineOnABlockSpanningAPageBreak_StaysOpenAtTheBreak()
        {
            // Mirrors BoxDecorationBreakPaintIntegrationTests' own
            // "Slice_InlineSpanningAPageBreak_DrawsNoBorderAtThePageBreakEither" - three lines land on
            // page 0 and the fourth on page 1 - but puts the outline directly on the block-level box
            // whose own content forces the split, rather than on a nested wrapping inline. A page break
            // is the one boundary the union must not cross: the rectangles it unions are always one
            // fragmentainer's, so a box broken across pages gets its own shape per page and the
            // block-axis edge the break cuts through stays open on both sides of it.
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='b' style='width:200pt;font:10pt Arial;line-height:30pt;" +
                                   "outline:2pt solid #00f'>Alpha<br>Beta<br>Gamma<br>Delta</div>"),
                pageHeight: 80, margin: 0);

            var div = LayoutHarness.FindById(root, "b")!;
            Assert.True(container.FragmentTree!.Fragmentainers.Count >= 2);

            var blue = RColor.FromArgb(0, 0, 255);
            var horizontal = 0;

            for (var page = 0; page < container.FragmentTree.Fragmentainers.Count; page++)
            {
                var g = new TestRecordingGraphics();
                FragmentPaintHarness.PaintBox(container, div, g, page);

                // No fully closed ring appears on either page.
                Assert.DoesNotContain(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(), p => p.Color == blue);

                horizontal += g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                    .Count(p => p.Color == blue && !IsVerticalEdge(p.Points));
            }

            // Only the box's own true top (page 0) and true bottom (page 1) horizontal edges paint -
            // never the page break itself, on either side of it.
            Assert.Equal(2, horizontal);
        }

        [Fact]
        public async Task OutlineOnAWrappingInlineElement_LinesThatDoNotTouch_StayAsSeparateShapes()
        {
            // The union is a genuine region operation, not a "join everything" rule. A line box's
            // rectangle is the inline's own box, not the whole line-height slot, so a line-height taller
            // than the text leaves real vertical gaps between consecutive lines. The region is then
            // three disjoint pieces and the band is six subpaths - three contours, each with its own
            // inset copy - which is what keeps a widely-spaced wrapped outline looking as it always has.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial;line-height:40pt'>" +
                "<span id='s' style='outline:2pt solid #00f'>Alpha<br>Beta<br>Gamma</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var blue = RColor.FromArgb(0, 0, 255);
            var shape = Assert.Single(
                g.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                p => !p.Stroked && p.Color == blue);

            Assert.Equal(6, shape.SubpathStarts.Count);

            // The three pieces really are separate: each contour's bottom sits strictly above the next
            // contour's top, so no two of them touch.
            var contourBounds = new[] { shape.SubpathBounds(0), shape.SubpathBounds(2), shape.SubpathBounds(4) }
                .OrderBy(r => r.Top)
                .ToList();
            for (var i = 1; i < contourBounds.Count; i++)
                Assert.True(contourBounds[i - 1].Bottom < contourBounds[i].Top);
        }

        [Fact]
        public async Task OutlineOnAWrappingInlineElement_OffsetIsAppliedPerLineBeforeUnioning()
        {
            // outline-offset is applied to each fragment's rectangle before the union, exactly as
            // Chromium does. Starting from the widely-spaced lines above - three disjoint pieces - an
            // offset large enough to close the gaps between them changes the region's topology to a
            // single connected shape, which is only observable if the offset really is applied to the
            // rectangles going into the union rather than to the finished union.
            const string template =
                "<div style='width:200pt;font:10pt Arial;line-height:40pt'>" +
                "<span id='s' style='outline:2pt solid #00f;outline-offset:{0}'>Alpha<br>Beta<br>Gamma</span></div>";

            Assert.Equal(6, await CountSubpaths(string.Format(template, "0")));
            Assert.Equal(2, await CountSubpaths(string.Format(template, "20pt")));

            static async Task<int> CountSubpaths(string body)
            {
                var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));
                var span = LayoutHarness.FindById(root, "s")!;

                var g = new TestRecordingGraphics();
                FragmentPaintHarness.PaintBox(container, span, g);

                return g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                    .Single(p => !p.Stroked && p.Color == RColor.FromArgb(0, 0, 255))
                    .SubpathStarts.Count;
            }
        }

        [Fact]
        public async Task DoubleOutlineOnAWrappingInlineElement_PaintsTwoConcentricConnectedBands()
        {
            // css-backgrounds-3 §4.3's two lines, drawn over the unioned contour rather than per line:
            // two filled shapes, the inner one strictly inside the outer, each an equal third of the
            // declared width with the middle third left as the gap between them.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                "<span id='s' style='outline:6pt double #00f'>Alpha<br>Beta<br>Gamma</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;
            var rects = FragmentPaintHarness.FragmentOf(container, span).Lines.Select(line => line.Rect).ToList();

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var bands = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(p => !p.Stroked && p.Color == RColor.FromArgb(0, 0, 255))
                .ToList();
            Assert.Equal(2, bands.Count);

            // The outer band starts at the outline's full 6pt reach; the inner one starts two thirds of
            // the way in, so its outer edge sits 4pt short of the first.
            Assert.Equal(rects.Min(r => r.Top) - 6, bands[0].Bounds.Top, 1);
            Assert.Equal(rects.Min(r => r.Top) - 2, bands[1].Bounds.Top, 1);
            Assert.Equal(rects.Max(r => r.Right) + 6, bands[0].Bounds.Right, 1);
            Assert.Equal(rects.Max(r => r.Right) + 2, bands[1].Bounds.Right, 1);
        }

        [Theory]
        [InlineData("dotted")]
        [InlineData("dashed")]
        public async Task RoundedPatternedOutlineOnAWrappingInlineElement_FitsOnePatternToTheWholeContour(
            string style)
        {
            // A rounded union has no straight edge to fit a pattern along - a corner arc is part of the
            // run, not a break in it - so the whole closed contour is fitted at once and stroked as a
            // single path. That is a different code path from the square-cornered per-edge fit, and the
            // observable difference is exactly this: one stroked path carrying one dash array, instead
            // of one DrawLine per edge.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                "<span id='s' style='border-radius:8pt;" +
                $"outline:3pt {style} #00f'>Alpha<br>Alpha<br>Alpha</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;
            var rects = FragmentPaintHarness.FragmentOf(container, span).Lines.Select(l => l.Rect).ToList();

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            // Not one DrawLine per edge: the rounded contour is stroked whole.
            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());

            var contour = Assert.Single(
                g.Log.OfType<TestRecordingGraphics.DrawPathCall>(), p => p.Stroked);

            // Fitted, not a canned dash style, and a dotted dot is a zero-length dash under a round cap.
            Assert.NotNull(contour.DashPattern);
            Assert.Equal(2, contour.DashPattern!.Count);
            if (style == "dotted")
            {
                Assert.Equal(0, contour.DashPattern[0]);
                Assert.Equal(RLineCap.Round, contour.LineCap);
            }
            else
            {
                Assert.Equal(6, contour.DashPattern[0], 1); // dashed = 2x the outline width
                Assert.Equal(RLineCap.Butt, contour.LineCap);
            }

            // The one path is the whole union's boundary, not a single line's ring.
            var unionWidth = rects.Max(r => r.Right) - rects.Min(r => r.Left);
            var unionHeight = rects.Max(r => r.Bottom) - rects.Min(r => r.Top);
            Assert.True(contour.Bounds.Width >= unionWidth, "the contour should span the whole union");
            Assert.True(contour.Bounds.Height >= unionHeight, "the contour should span the whole union");
        }

        [Fact]
        public async Task PatternedOutlineOnAnEdgeTooShortToCarryADash_StrokesThatEdgeSolid()
        {
            // A pattern is fitted per straight edge, so a short edge is fitted on its own terms - and
            // one shorter than a single dash plus its gap has no fitting to apply. The fallback is a
            // solid stroke over that edge rather than no stroke at all, which would leave a visible
            // hole in the contour. A staircase's step is where such an edge actually occurs: the step's
            // own risers are only as tall as the difference between two lines' widths is wide.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                "<span id='s' style='outline:12pt dashed #00f'>Alpha alpha<br>Be</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.NotEmpty(lines);

            // The step's short edges cannot carry a 24pt dash, so they fall back to solid, while the
            // long edges of the same contour still carry their fitted pattern.
            Assert.Contains(lines, l => l.DashPattern is null && l.DashStyle == RDashStyle.Solid);
            Assert.Contains(lines, l => l.DashPattern is not null);
        }

        [Theory]
        [InlineData("dotted")]
        [InlineData("dashed")]
        public async Task PatternedOutlineOnUnevenLines_StrokesTheStaircaseTheUnionMakes(string style)
        {
            // Lines of differing width union into a staircase rather than a rectangle, which is the
            // shape this feature exists for: the step between two lines is a concave corner, a thing a
            // per-side dash pattern has no notion of. Two long lines over one short one make a single
            // step, so the region is an L - six edges, one connected run, against the twelve three
            // independent rings would stroke.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                $"<span id='s' style='outline:3pt {style} #00f'>Alpha alpha<br>Alpha alpha<br>Be</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;
            var rects = FragmentPaintHarness.FragmentOf(container, span).Lines.Select(l => l.Rect).ToList();

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.Equal(6, lines.Count);
            Assert.All(lines, line => Assert.NotNull(line.DashPattern));

            // The step's two edges are interior to neither line: one horizontal edge sits at the short
            // line's own top, which only exists because the wider lines above it end there.
            var stepY = rects[2].Top;
            Assert.Contains(
                lines.Where(l => Math.Abs(l.Y1 - l.Y2) < 0.01),
                line => Math.Abs(line.Y1 - stepY) < rects[0].Height);

            // Exactly one horizontal edge spans the widest line; the short line contributes its own,
            // shorter one. Three rings would instead produce three full-width horizontals.
            var full = lines
                .Where(l => Math.Abs(l.Y1 - l.Y2) < 0.01)
                .Count(l => Math.Abs(l.X2 - l.X1) >= rects.Max(r => r.Width));
            Assert.Equal(1, full);
        }

        [Theory]
        [InlineData("dotted")]
        [InlineData("dashed")]
        public async Task PatternedOutlineOnAWrappingInlineElement_StrokesTheUnionsEdgesNotEachLines(
            string style)
        {
            // A patterned outline is fitted per straight edge, so the proof that it followed the union
            // rather than each line is which edges exist to be stroked. Three stacked lines of equal
            // width merge into a single rectangle, whose boundary has exactly four edges - where three
            // separate rings would have twelve.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                $"<span id='s' style='outline:3pt {style} #00f'>Alpha<br>Alpha<br>Alpha</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;
            var rects = FragmentPaintHarness.FragmentOf(container, span).Lines.Select(l => l.Rect).ToList();

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.Equal(4, lines.Count);

            // Every stroke carries a fitted pattern - the whole point of these styles.
            Assert.All(lines, line => Assert.NotNull(line.DashPattern));

            // The two horizontal edges span the union's full width, which is the widest line's. Were
            // these each line's own edges, the narrower lines would produce shorter strokes.
            var horizontal = lines.Where(l => Math.Abs(l.Y1 - l.Y2) < 0.01).ToList();
            Assert.Equal(2, horizontal.Count);
            Assert.All(horizontal, line =>
                Assert.True(
                    Math.Abs(line.X2 - line.X1) >= rects.Max(r => r.Width),
                    "a horizontal edge should span the whole union, not one line"));

            // The two vertical edges run the full height of the merged run, past both interior line
            // boundaries - impossible for a per-line ring, whose verticals are one line tall.
            var vertical = lines.Where(l => Math.Abs(l.X1 - l.X2) < 0.01).ToList();
            Assert.Equal(2, vertical.Count);
            var runHeight = rects.Max(r => r.Bottom) - rects.Min(r => r.Top);
            Assert.All(vertical, line =>
                Assert.True(
                    Math.Abs(line.Y2 - line.Y1) >= runHeight,
                    "a vertical edge should run the whole union, not one line"));
        }

        [Theory]
        [InlineData("groove")]
        [InlineData("ridge")]
        [InlineData("inset")]
        [InlineData("outset")]
        public async Task BevelledOutlineOnAWrappingInlineElement_ShadesByTravelDirectionOverTheUnion(
            string style)
        {
            // A bevel's two faces are chosen from the direction each boundary edge travels in, so a
            // correctly unioned bevel paints in exactly two colours - and each shade is laid down in a
            // single fill covering every edge that resolves to it.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                $"<span id='s' style='outline:6pt {style} #36c'>Alpha<br>Beta<br>Gamma</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;
            var rects = FragmentPaintHarness.FragmentOf(container, span).Lines.Select(l => l.Rect).ToList();

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var paths = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();
            Assert.NotEmpty(paths);

            // Exactly two shades - a lit face and a shaded one. One would be a flat frame, meaning the
            // direction rule never fired; more would mean a per-side colour leaked in.
            var shades = paths.Select(p => p.Color).Distinct().ToList();
            Assert.Equal(2, shades.Count);
            Assert.DoesNotContain(RColor.FromArgb(0x33, 0x66, 0xcc), shades);

            // One fill per shade - two for a single-pass bevel, twice that for groove/ridge's two
            // passes. A fill per EDGE instead would abut two same-shade faces along each mitre, and two
            // abutting antialiased fills leave a pale seam down the join.
            var passes = style is "groove" or "ridge" ? 2 : 1;
            Assert.Equal(2 * passes, paths.Count);
            Assert.Equal(
                paths.Count, g.Log.OfType<TestRecordingGraphics.PushClipCall>().Count());

            // Each shade's clip carries one quad per edge it owns. These three lines are of differing
            // widths, so the union is a staircase of six edges, splitting three apiece between the two
            // shades - and a clip per EDGE would instead give six single-quad clips.
            Assert.All(
                g.ClipPaths.TakeLast(2 * passes),
                clip => Assert.Equal(3, clip.SubpathStarts.Count));

            // Each fill is the whole union's band - it is the clip, not the geometry, that selects the
            // edges - so every path reaches the union's full extent rather than one line's.
            var unionWidth = rects.Max(r => r.Right) - rects.Min(r => r.Left);
            var unionHeight = rects.Max(r => r.Bottom) - rects.Min(r => r.Top);
            Assert.All(paths, path =>
            {
                Assert.True(path.Bounds.Width >= unionWidth, "a bevel face should span the union");
                Assert.True(path.Bounds.Height >= unionHeight, "a bevel face should span the union");
            });
        }

        [Theory]
        [InlineData("groove")]
        [InlineData("ridge")]
        [InlineData("inset")]
        [InlineData("outset")]
        public async Task RoundedBevelledOutlineOnAWrappingInlineElement_ShadesTheWholeBandIncludingItsArcs(
            string style)
        {
            // A rounded corner's arc has no single direction of travel, so it has no shade of its own -
            // and a bevel that derived its clips from the rounded path would find nothing to give the
            // arcs and leave them unpainted, which is a hole in the band rather than a subtle flaw. The
            // clips instead come from the region's own right-angled corners, whose mitres split each arc
            // down the 45-degree diagonal between the two edges it joins - and each mitre reaches far
            // enough along that diagonal to hold the arc, which bulges away from the straight run the
            // rest of the edge follows.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                $"<span id='s' style='border-radius:8pt;outline:6pt {style} #36c'>" +
                "Alpha<br>Beta<br>Gamma</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;
            var rects = FragmentPaintHarness.FragmentOf(container, span).Lines.Select(l => l.Rect).ToList();

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var paths = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();
            var passes = style is "groove" or "ridge" ? 2 : 1;
            Assert.Equal(2 * passes, paths.Count);
            Assert.Equal(2, paths.Select(p => p.Color).Distinct().Count());

            // The band really is rounded - a square one would reach its own corners.
            var band = paths[0];
            Assert.DoesNotContain(
                band.Points,
                p => Math.Abs(p.X - (rects.Min(r => r.Left) - 6)) < 0.5
                     && Math.Abs(p.Y - (rects.Min(r => r.Top) - 6)) < 0.5);

            // And the clips of one pass cover it: every point the band actually passes through - the
            // arcs included, flattened through their control points - falls inside one clip quad or
            // the other. Asserting only the clips' outer extent would not show this, because the
            // straight edges' own quads already reach that extent whether or not anything covers the
            // arcs between them.
            var clips = g.ClipPaths.TakeLast(2).SelectMany(Quads).ToList();

            Assert.All(Flatten(band), point =>
                Assert.True(
                    clips.Any(quad => ContainsPoint(quad, point)),
                    $"({point.X:0.##}, {point.Y:0.##}) of the band is inside no clip, so that part of "
                    + "the band - an arc, if the mitres are only as deep as the straight run - goes "
                    + "unpainted"));
        }

        [Theory]
        [InlineData("groove")]
        [InlineData("ridge")]
        [InlineData("inset")]
        [InlineData("outset")]
        public async Task BevelledOutlineOnAWrappingInlineElement_KeepsEachClipConvex(string style)
        {
            // Each clip subpath is the territory of one boundary edge, bounded by the 45-degree mitre
            // it shares with the neighbour at either end. Both mitres are cut from the edge's own line,
            // so nothing stops them from crossing - and once they have, the far side of the crossing is
            // territory turned inside out, which draws as a bowtie whose waist cancels under nonzero
            // winding. The band is unpainted along that waist: not a rounding-sized flaw but a clean
            // diagonal slash, and it appears on any edge shorter than twice the mitre's reach, which
            // the short steps of a wrapped inline's staircase routinely are.
            //
            // A coverage test cannot see this, because a ray cast from a point in the cancelled waist
            // still crosses the bowtie's outline an odd number of times. So assert the shape itself:
            // every subpath of every clip has to be a simple polygon.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                $"<span id='s' style='border-radius:8pt;outline:6pt {style} #36c'>" +
                "Alpha<br>Beta<br>Gamma</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var quads = g.ClipPaths.SelectMany(Quads).ToList();
            Assert.NotEmpty(quads);

            Assert.All(quads, quad =>
                Assert.False(
                    SelfIntersects(quad),
                    "a bevel clip's mitres crossed, folding its edge's territory into a bowtie whose "
                    + "waist cancels under nonzero winding and leaves a diagonal slash of the band "
                    + "unpainted"));
        }

        /// <summary>
        /// Whether any two non-adjacent sides of the closed polygon <paramref name="polygon"/> cross.
        /// </summary>
        private static bool SelfIntersects(IReadOnlyList<RPoint> polygon)
        {
            var n = polygon.Count;

            for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                // Sides sharing an endpoint touch there by construction.
                if (j == i + 1 || (i == 0 && j == n - 1)) continue;

                if (SegmentsCross(
                        polygon[i], polygon[(i + 1) % n], polygon[j], polygon[(j + 1) % n]))
                    return true;
            }

            return false;

            static bool SegmentsCross(RPoint a, RPoint b, RPoint c, RPoint d) =>
                Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0;

            static int Side(RPoint a, RPoint b, RPoint p)
            {
                var cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                return Math.Abs(cross) < 1e-9 ? 0 : Math.Sign(cross);
            }
        }

        /// <summary>
        /// The points <paramref name="path"/> passes through: its straight vertices, plus each Bézier
        /// sampled along its length. The two control points of a curve are not on it - they are what
        /// it bends towards - so a test asking where the shape is has to evaluate the curve rather
        /// than assert against them.
        /// </summary>
        private static IEnumerable<RPoint> Flatten(TestRecordingGraphics.DrawPathCall path)
        {
            for (var i = 0; i < path.Points.Count; i++)
            {
                if (path.BezierControlPoints.Contains(i))
                {
                    // i and i + 1 are the control points, i + 2 the curve's end, i - 1 its start.
                    if (path.BezierControlPoints.Contains(i - 1)) continue;

                    var p0 = path.Points[i - 1];
                    var p1 = path.Points[i];
                    var p2 = path.Points[i + 1];
                    var p3 = path.Points[i + 2];

                    for (var step = 1; step < 8; step++)
                    {
                        var t = step / 8.0;
                        var u = 1 - t;
                        yield return new RPoint(
                            u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
                            u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y);
                    }

                    continue;
                }

                yield return path.Points[i];
            }
        }

        /// <summary>
        /// <paramref name="path"/> split into its subpaths - for a bevel clip, the one quad it holds
        /// per boundary edge of that shade.
        /// </summary>
        private static IEnumerable<IReadOnlyList<RPoint>> Quads(TestGraphicsPath path)
        {
            for (var i = 0; i < path.SubpathStarts.Count; i++)
            {
                var start = path.SubpathStarts[i];
                var end = i + 1 < path.SubpathStarts.Count
                    ? path.SubpathStarts[i + 1]
                    : path.Points.Count;
                yield return path.Points.GetRange(start, end - start);
            }
        }

        /// <summary>
        /// Whether <paramref name="point"/> lies within <paramref name="polygon"/>, by the winding of a
        /// ray cast from it. Tolerant by a hair on the boundary, since a point the clip is meant to
        /// carry exactly - an arc's end, which sits on the mitre it is cut by - would otherwise land
        /// either side of the edge on rounding alone.
        /// </summary>
        private static bool ContainsPoint(IReadOnlyList<RPoint> polygon, RPoint point)
        {
            const double tolerance = 0.01;
            var inside = false;

            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];

                // On the edge itself, within tolerance.
                var cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
                var length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                if (length > 0 && Math.Abs(cross) / length <= tolerance
                    && point.X >= Math.Min(a.X, b.X) - tolerance
                    && point.X <= Math.Max(a.X, b.X) + tolerance
                    && point.Y >= Math.Min(a.Y, b.Y) - tolerance
                    && point.Y <= Math.Max(a.Y, b.Y) + tolerance)
                    return true;

                if (a.Y > point.Y != b.Y > point.Y
                    && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }

            return inside;
        }

        [Fact]
        public async Task RoundedOutlineOnAWrappingInlineElement_PutsEachCornerRadiusOnItsOwnCorner()
        {
            // An asymmetric border-radius proves the union's corners are mapped individually rather
            // than all treated alike: only the top-left corner is rounded here, so the shape's top-left
            // must be cut away while its top-right stays a true right angle. The top edge belongs to the
            // first line, so that right angle is at that line's own right edge - not the widest line's,
            // which is further right but lower down.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                "<span id='s' style='border-radius:20pt 0 0 0;outline:2pt solid #00f'>Alpha<br>Beta<br>Gamma</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;
            var rects = FragmentPaintHarness.FragmentOf(container, span).Lines.Select(line => line.Rect).ToList();

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var shape = Assert.Single(
                g.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                p => !p.Stroked && p.Color == RColor.FromArgb(0, 0, 255));

            var outer = shape.Subpath(0);
            var left = rects.Min(r => r.Left) - 2;
            var top = rects.Min(r => r.Top) - 2;

            // Top-right (the first line's own): square, so the contour reaches its exact corner point.
            Assert.Contains(outer, p => Math.Abs(p.X - (rects[0].Right + 2)) < 0.5 && Math.Abs(p.Y - top) < 0.5);

            // Top-left: rounded, so nothing reaches the corner itself - the contour instead leaves the
            // top edge some way to its right and rejoins the left edge some way below it.
            Assert.DoesNotContain(outer, p => Math.Abs(p.X - left) < 0.5 && Math.Abs(p.Y - top) < 0.5);
            Assert.Contains(outer, p => p.X > left + 1 && Math.Abs(p.Y - top) < 0.5);
            Assert.Contains(outer, p => Math.Abs(p.X - left) < 0.5 && p.Y > top + 1);
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
        public async Task OutlineStyleDouble_StripeWidths_AreInvariantUnderNonDefaultPixelsPerInch()
        {
            const string html = "<div id='b' style='width:20pt; height:20pt; outline: 12pt double rgb(51,51,51)'>x</div>";

            var (rootDefault, containerDefault) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var divDefault = LayoutHarness.FindById(rootDefault, "b")!;
            var gDefault = new TestRecordingGraphics { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintBox(containerDefault, divDefault, gDefault);
            var bandsDefault = gDefault.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(p => !p.Stroked && p.Color == RColor.FromArgb(51, 51, 51)).ToList();

            var (rootScaled, containerScaled) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html), pixelsPerPoint: 2.0);
            var divScaled = LayoutHarness.FindById(rootScaled, "b")!;
            var gScaled = new TestRecordingGraphics { PixelsPerPointOverride = 2.0 };
            FragmentPaintHarness.PaintBox(containerScaled, divScaled, gScaled);
            var bandsScaled = gScaled.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(p => !p.Stroked && p.Color == RColor.FromArgb(51, 51, 51)).ToList();

            // double contributes two complete rings, one for each stripe.
            Assert.Equal(2, bandsDefault.Count);
            Assert.Equal(bandsDefault.Count, bandsScaled.Count);

            // Ring paths normalize their layout-space coordinates before reaching the adapter, so both
            // resolutions must record the same physical 4pt stripe width (issue #812).
            for (var i = 0; i < bandsDefault.Count; i++)
            {
                Assert.Equal(4, RingThickness(bandsDefault[i]), 3);
                Assert.Equal(RingThickness(bandsDefault[i]), RingThickness(bandsScaled[i]), 3);
            }
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

        private static double OuterTop(TestRecordingGraphics.DrawPathCall ring) =>
            ring.Points.Take(4).Min(p => p.Y);

        private static double InnerTop(TestRecordingGraphics.DrawPathCall ring) =>
            ring.Points.Skip(4).Take(4).Min(p => p.Y);

        private static double RingThickness(TestRecordingGraphics.DrawPathCall ring) =>
            InnerTop(ring) - OuterTop(ring);

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
