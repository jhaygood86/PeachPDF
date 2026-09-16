using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies border-style painting actually produces the right geometry/color, not just that it
    /// doesn't crash. <c>double</c>/<c>groove</c>/<c>ridge</c> previously threw
    /// <see cref="System.ArgumentOutOfRangeException"/> at paint time in
    /// <c>BordersDrawHandler.GetPen</c> despite being documented as fully supported - a substring/
    /// token-presence check on PDF output would not have caught that, since the render simply never
    /// completed. Uses <see cref="TestRecordingGraphics"/> to assert the actual draw-call sequence,
    /// per this repo's painting-test convention (see <c>MarkerStylingIntegrationTests</c>).
    /// </summary>
    public class BorderStylePaintIntegrationTests
    {
        [Theory]
        [InlineData("dotted")]
        [InlineData("dashed")]
        [InlineData("solid")]
        [InlineData("double")]
        [InlineData("groove")]
        [InlineData("ridge")]
        [InlineData("inset")]
        [InlineData("outset")]
        public async Task BorderStyle_AllCss1Keywords_DoNotThrowWhenPainted(string style)
        {
            var (root, container) = await BuildAndLayout(Wrap(
                $"<div id='b' style='border: 12px {style} rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            var exception = await Record.ExceptionAsync(async () => FragmentPaintHarness.PaintBox(container, div, g));

            Assert.Null(exception);
        }

        [Fact]
        public async Task BorderStyleDouble_DrawsTwoEqualThirdsWithAMatchingGap()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: double; border-top-width: 12pt; border-top-color: rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            // Each ring is a mitred band (a filled quad), not a stroked full-length line - that is what
            // keeps the top edge's inner ring out of the left/right borders' gap at the corners.
            var bands = HorizontalBands(g);
            Assert.Equal(2, bands.Count);

            // CSS 2.1 §8.5.3: the two lines and the space between them sum to border-width, so each is
            // an exact third - 4pt of 12pt, with a 4pt gap.
            Assert.All(bands, b => Assert.Equal(RColor.FromArgb(51, 51, 51), b.Color));
            Assert.Equal(4, bands[0].Height, 2);
            Assert.Equal(4, bands[1].Height, 2);
            Assert.Equal(4, bands[1].Top - bands[0].Bottom, 2);
        }

        [Fact]
        public async Task BorderStyleGroove_TopEdge_IsDarkOutsideAndLightInside()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: groove; border-top-width: 12px; border-top-color: rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var bands = HorizontalBands(g);
            Assert.Equal(2, bands.Count);

            // groove paints its outer half as `inset` and its inner half as `outset`; on a TOP edge
            // inset is the darkened face. The two halves are equal.
            Assert.Equal(BorderBevelColors.Shade(RColor.FromArgb(51, 51, 51), darken: true), bands[0].Color);
            Assert.Equal(BorderBevelColors.Shade(RColor.FromArgb(51, 51, 51), darken: false), bands[1].Color);
            Assert.Equal(bands[0].Height, bands[1].Height, 2);
        }

        [Fact]
        public async Task BorderStyleGroove_BottomEdge_ReversesTheShadingOfTheTopEdge()
        {
            // The per-side flip is the whole 3D effect, and is exactly what a "darken the outer stripe"
            // implementation gets wrong: shading all four sides alike gives a flat two-tone frame.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-style: groove; border-width: 12px; border-color: rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var dark = BorderBevelColors.Shade(RColor.FromArgb(51, 51, 51), darken: true);
            var light = BorderBevelColors.Shade(RColor.FromArgb(51, 51, 51), darken: false);
            Assert.NotEqual(dark, light);

            var bands = HorizontalBands(g);
            Assert.Equal(4, bands.Count); // top's two, bottom's two

            // Top edge, outermost band first.
            Assert.Equal(dark, bands[0].Color);
            Assert.Equal(light, bands[1].Color);
            // Bottom edge: the band nearer the content is the dark one, the outermost is light.
            Assert.Equal(light, bands[3].Color);
            Assert.Equal(dark, bands[2].Color);
        }

        [Fact]
        public async Task BorderRightStyleDouble_DrawsTwoEqualThirdsWithAMatchingGap()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-right-style: double; border-right-width: 12pt; border-right-color: rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            // A right-edge band is vertical - the mirror image of the top-edge case on the other axis.
            var bands = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                .Select(Band)
                .Where(b => b.Width < b.Height)
                .OrderBy(b => b.Left)
                .ToList();
            Assert.Equal(2, bands.Count);

            Assert.All(bands, b => Assert.Equal(RColor.FromArgb(51, 51, 51), b.Color));
            Assert.Equal(4, bands[0].Width, 2);
            Assert.Equal(4, bands[1].Width, 2);
            Assert.Equal(4, bands[1].Left - bands[0].Right, 2);
        }

        [Fact]
        public async Task BorderLeftStyleGroove_IsDarkOutsideAndLightInside()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-left-style: groove; border-left-width: 12px; border-left-color: rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var bands = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                .Select(Band)
                .OrderBy(b => b.Left)
                .ToList();
            Assert.Equal(2, bands.Count);

            // A LEFT edge shades like a top edge: inset (groove's outer half) is the darkened face.
            Assert.Equal(BorderBevelColors.Shade(RColor.FromArgb(51, 51, 51), darken: true), bands[0].Color);
            Assert.Equal(BorderBevelColors.Shade(RColor.FromArgb(51, 51, 51), darken: false), bands[1].Color);
        }

        [Theory]
        [InlineData("inset", true)]
        [InlineData("outset", false)]
        public async Task BorderStyleInsetOutset_DarkensOnePairOfSidesAndLightensTheOther(string style, bool inset)
        {
            var (root, container) = await BuildAndLayout(Wrap(
                $"<div id='b' style='width:40px; height:40px; border: 8px {style} rgb(74,144,217)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().Select(Band).ToList();
            Assert.Equal(4, polys.Count);

            var top = polys.OrderBy(p => p.Top + p.Height / 2).First();
            var bottom = polys.OrderByDescending(p => p.Top + p.Height / 2).First();
            var left = polys.OrderBy(p => p.Left + p.Width / 2).First();
            var right = polys.OrderByDescending(p => p.Left + p.Width / 2).First();

            var baseColor = RColor.FromArgb(74, 144, 217);
            var dark = BorderBevelColors.Shade(baseColor, darken: true);
            var light = BorderBevelColors.Shade(baseColor, darken: false);

            // Sampled from Chrome for this exact color - the lit face is genuinely lightened, not left
            // at the declared color, which is what makes the bevel read as 3D.
            Assert.Equal(RColor.FromArgb(45, 88, 133), dark);
            Assert.Equal(RColor.FromArgb(87, 169, 255), light);

            Assert.Equal(inset ? dark : light, top.Color);
            Assert.Equal(inset ? dark : light, left.Color);
            Assert.Equal(inset ? light : dark, bottom.Color);
            Assert.Equal(inset ? light : dark, right.Color);
        }

        [Fact]
        public void BorderBevelColors_NearBlack_LightensBothFacesRatherThanVanishing()
        {
            // Darkening black produces black, so a plain `border: inset black` - which is what a
            // UA-default fieldset/table border amounts to - would paint an invisible edge. Both values
            // below are sampled from Chrome's own rendering of `border: 10px inset #000`.
            var black = RColor.FromArgb(0, 0, 0);
            Assert.Equal(RColor.FromArgb(84, 84, 84), BorderBevelColors.Shade(black, darken: true));
            Assert.Equal(RColor.FromArgb(168, 168, 168), BorderBevelColors.Shade(black, darken: false));

            // The fallback is a contrast test, not an "is it black" test: a dark-but-not-black color
            // whose darkened form still reads as an edge keeps darkening (gray 33 in Chrome), while one
            // just below the threshold lightens (gray 32).
            Assert.Equal(RColor.FromArgb(0, 0, 0), BorderBevelColors.Shade(RColor.FromArgb(33, 33, 33), darken: true));
            Assert.Equal(RColor.FromArgb(116, 116, 116), BorderBevelColors.Shade(RColor.FromArgb(32, 32, 32), darken: true));
        }

        [Fact]
        public async Task BorderColorPerSide_ResolvesDistinctColorPerEdge_IncludingCurrentColor()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:40px; height:40px; border-style:solid; border-width:4px; color: rgb(9,9,9); "
                + "border-top-color: rgb(1,0,0); border-right-color: rgb(0,1,0); "
                + "border-bottom-color: rgb(0,0,1); border-left-color: currentcolor'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, polys.Count);

            static (double X, double Y) Centroid(TestRecordingGraphics.DrawPolygonCall p) =>
                (p.Points.Average(pt => pt.X), p.Points.Average(pt => pt.Y));

            var withCentroids = polys.Select(p => (Poly: p, Centroid: Centroid(p))).ToList();
            var top = withCentroids.OrderBy(t => t.Centroid.Y).First();
            var bottom = withCentroids.OrderByDescending(t => t.Centroid.Y).First();
            var left = withCentroids.OrderBy(t => t.Centroid.X).First();
            var right = withCentroids.OrderByDescending(t => t.Centroid.X).First();

            Assert.Equal(RColor.FromArgb(1, 0, 0), top.Poly.Color);
            Assert.Equal(RColor.FromArgb(0, 1, 0), right.Poly.Color);
            Assert.Equal(RColor.FromArgb(0, 0, 1), bottom.Poly.Color);
            Assert.Equal(RColor.FromArgb(9, 9, 9), left.Poly.Color);
        }

        [Fact]
        public async Task BorderStyleRidge_IsMirrorImageOfGroove()
        {
            var (grooveRoot, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: groove; border-top-width: 12px; border-top-color: rgb(51,51,51)'>x</div>"));
            var grooveDiv = FindById(grooveRoot, "b")!;
            var grooveG = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, grooveDiv, grooveG);
            var grooveBands = HorizontalBands(grooveG);

            var (ridgeRoot, ridgeContainer) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: ridge; border-top-width: 12px; border-top-color: rgb(51,51,51)'>x</div>"));
            var ridgeDiv = FindById(ridgeRoot, "b")!;
            var ridgeG = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(ridgeContainer, ridgeDiv, ridgeG);
            var ridgeBands = HorizontalBands(ridgeG);

            Assert.Equal(2, grooveBands.Count);
            Assert.Equal(2, ridgeBands.Count);

            // Exactly the class of bug a substring test would miss: visually-identical-but-swapped
            // stripe colors. groove's outer band must equal ridge's inner band, and vice versa.
            Assert.Equal(grooveBands[0].Color, ridgeBands[1].Color);
            Assert.Equal(grooveBands[1].Color, ridgeBands[0].Color);
            Assert.NotEqual(grooveBands[0].Color, grooveBands[1].Color);
        }

        // ─── dotted/dashed pattern fitting ───────────────────────────────────────
        // The pattern is sized to the edge so it starts and ends flush with the corners; a fixed period
        // leaves a ragged stub in one corner, which is what this used to do.

        [Fact]
        public async Task BorderStyleDotted_DrawsRoundDotsFittedToTheEdge()
        {
            // 160pt wide, 16pt border: the ideal 16/16 pattern would need 5 dots and leave a remainder,
            // so the gap is squeezed to fit 6 dots exactly - dot 16, gap (160 - 6*16)/5 = 12.8.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width: 128pt; height: 40pt; border: 16pt dotted rgb(51,51,51)'></div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.Empty(g.ClipPaths);
            var top = g.Log.OfType<TestRecordingGraphics.DrawLineCall>()
                .First(l => Math.Abs(l.Y1 - l.Y2) < 0.01);

            // A dot is a zero-length dash under a round cap - without the cap it paints nothing at all,
            // and with a butt cap it would paint squares (which is what it used to do).
            Assert.Equal(RLineCap.Round, top.LineCap);
            Assert.NotNull(top.DashPattern);
            Assert.Equal(2, top.DashPattern!.Count);
            Assert.Equal(0, top.DashPattern[0]);
            Assert.Equal(16 + 12.8, top.DashPattern[1], 2);

            // The path runs centre-to-centre, so it is inset half a dot at each end (plus the half
            // period of slack that keeps the final dot from landing exactly on the endpoint).
            Assert.Equal(16 / 2d, top.X1 - LeftOf(div), 2);
        }

        [Fact]
        public async Task BorderStyleDashed_FitsDashesToTheEdgeWithButtCaps()
        {
            // 160pt wide, 16pt border: dash 32, ideal gap 16. Four dashes need a 10.67 gap and three
            // need a 32 gap, so four wins - and the whole run lands flush in both corners.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width: 128pt; height: 40pt; border: 16pt dashed rgb(51,51,51)'></div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var top = g.Log.OfType<TestRecordingGraphics.DrawLineCall>()
                .First(l => Math.Abs(l.Y1 - l.Y2) < 0.01);

            Assert.Equal(RLineCap.Butt, top.LineCap);
            Assert.NotNull(top.DashPattern);
            Assert.Equal(32, top.DashPattern![0], 2);
            Assert.Equal((160 - 4 * 32) / 3d, top.DashPattern[1], 2);

            // A dashed edge spans its full outer length, so two adjacent edges meet in a filled corner.
            Assert.Equal(LeftOf(div), top.X1, 2);
            Assert.Equal(LeftOf(div) + 160, top.X2, 2);
        }

        [Fact]
        public async Task MixedBorderStyles_ClipsDottedAndDashedStrokesAtAdjacentStyleTransitions()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:128pt; height:40pt; border:14px rgb(74,144,217); border-style:solid dashed double dotted'></div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.Equal(2, lines.Count);
            Assert.Equal(2, g.ClipPaths.Count);

            foreach (var line in lines)
            {
                var lineIndex = g.Log.IndexOf(line);
                Assert.IsType<TestRecordingGraphics.PushClipCall>(g.Log[lineIndex - 1]);
                Assert.IsType<TestRecordingGraphics.PopClipCall>(g.Log[lineIndex + 1]);
            }
        }

        [Theory]
        [InlineData("border-top:10pt dashed #4a90d9; border-left:4pt solid #4a90d9")]
        [InlineData("border-right:10pt dotted #4a90d9; border-bottom:4pt solid #4a90d9")]
        [InlineData("border-bottom:10pt dashed #4a90d9; border-right:4pt solid #4a90d9")]
        [InlineData("border-left:10pt dotted #4a90d9; border-top:4pt solid #4a90d9")]
        public async Task PatternedEdge_WithOneDifferingAdjacentEdge_ClipsOnlyThatCorner(string css)
        {
            var (root, container) = await BuildAndLayout(Wrap(
                $"<div id='b' style='width:128pt; height:40pt; {css}'></div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.Single(g.ClipPaths);
        }

        [Fact]
        public async Task MixedBorderWidths_LeftDashClipEndsOnBottomBordersDiagonal()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:128pt; height:40pt; border-color:#d94a4a #4ad98a #4a90d9 #d9c74a; " +
                "border-style:double solid groove dashed; border-width:18px 6px 14px 10px'></div>"));
            var div = FindById(root, "b")!;
            var borderRect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var leftLine = Assert.Single(
                g.Log.OfType<TestRecordingGraphics.DrawLineCall>(),
                line => Math.Abs(line.X1 - line.X2) < 0.01 && line.X1 < borderRect.Left + borderRect.Width / 2);
            var lineIndex = g.Log.IndexOf(leftLine);
            Assert.IsType<TestRecordingGraphics.PushClipCall>(g.Log[lineIndex - 1]);
            Assert.IsType<TestRecordingGraphics.PopClipCall>(g.Log[lineIndex + 1]);

            var leftClip = Assert.Single(g.ClipPaths);
            var outerBottom = new RPoint(borderRect.Left, borderRect.Bottom);
            var innerBottom = new RPoint(
                borderRect.Left + div.ActualBorderLeftWidth,
                borderRect.Bottom - div.ActualBorderBottomWidth);
            Assert.Contains(leftClip.Points, point =>
                Math.Abs(point.X - outerBottom.X) < 0.01 &&
                Math.Abs(point.Y - outerBottom.Y) < 0.01);
            Assert.Contains(leftClip.Points, point =>
                Math.Abs(point.X - innerBottom.X) < 0.01 &&
                Math.Abs(point.Y - innerBottom.Y) < 0.01);
        }

        [Fact]
        public void StyledStrokeFitting_PrefersTheSmallerDashCountWhenItsGapIsCloser()
        {
            // Not simply "round up": for a 204-long edge with a 2-wide dashed border, 34 dashes need a
            // 2.06 gap and 35 need 1.88, so the smaller count wins. Measured from Chrome.
            var fitted = StyledStrokeFitting.Fit(dotted: false, strokeWidth: 2, edgeLength: 204);
            Assert.NotNull(fitted);
            Assert.Equal(4, fitted!.Value.DashLength, 3);
            Assert.Equal(68 / 33d, fitted.Value.GapLength, 3);

            // ...and the larger count when its gap is closer (a 164-long edge, 16-wide dotted border).
            var dots = StyledStrokeFitting.Fit(dotted: true, strokeWidth: 16, edgeLength: 164);
            Assert.NotNull(dots);
            Assert.Equal(16, dots!.Value.DashLength, 3);
            Assert.Equal(68 / 5d, dots.Value.GapLength, 3);
        }

        // ─── uniform borders paint as one seamless ring ──────────────────────────
        // Two antialiased polygons that share an edge do not composite to full coverage, so four mitred
        // quads leave a pale hairline down every corner diagonal. It is invisible when the adjacent
        // edges differ in color and obvious when they do not - the commonest border there is.

        [Fact]
        public async Task BorderSolid_AllFourSidesAlike_PaintsOneClosedRingNotFourQuads()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:40pt; height:40pt; border: 6pt solid rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());

            var ring = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(), p => !p.Stroked);
            Assert.Equal(RColor.FromArgb(51, 51, 51), ring.Color);

            // Two rectangular subpaths - the border box and the padding box - filled even-odd.
            Assert.Equal(8, ring.Points.Count);
            var fragment = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;
            Assert.Equal(fragment.Left, ring.Bounds.Left, 1);
            Assert.Equal(fragment.Right, ring.Bounds.Right, 1);
        }

        [Fact]
        public async Task BorderDouble_AllFourSidesAlike_PaintsTwoNestedRings()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:40pt; height:40pt; border: 12pt double rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var rings = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().Where(p => !p.Stroked).ToList();
            Assert.Equal(2, rings.Count);

            // Outer ring first. The inner one begins two thirds of the way through the 12pt border on
            // every side, so its outer boundary is 2 x 8pt smaller in each axis.
            Assert.Equal(16, rings[0].Bounds.Height - rings[1].Bounds.Height, 1);
            Assert.Equal(8, rings[1].Bounds.Top - rings[0].Bounds.Top, 1);

            // Each ring is a third of the border thick: its own two subpaths are 4pt apart.
            Assert.Equal(8, rings[0].Points.Count);
            Assert.Equal(4, rings[0].Points.Skip(4).Min(p => p.Y) - rings[0].Points.Take(4).Min(p => p.Y), 1);
        }

        [Theory]
        [InlineData("border: 6pt solid; border-color: rgb(51,51,51) rgb(9,9,9) rgb(51,51,51) rgb(51,51,51)")]
        [InlineData("border: 6pt rgb(51,51,51); border-style: solid solid dashed solid")]
        [InlineData("border-color: rgb(51,51,51); border-style: solid; border-width: 6pt 6pt 0 6pt")]
        public async Task BorderWithAnySideDiffering_KeepsThePerEdgeMitredQuads(string css)
        {
            // The ring can only represent one style and color, so anything that differs per side - and
            // every beveled style, which shades each side differently by design - stays on the per-edge
            // path, where the mitre diagonal is meant to be visible anyway.
            var (root, container) = await BuildAndLayout(Wrap($"<div id='b' style='width:40pt; height:40pt; {css}'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.NotEmpty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
            Assert.DoesNotContain(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(), p => !p.Stroked);
        }

        [Fact]
        public async Task BorderInset_DoesNotTakeTheRingPath_BecauseEachSideIsShadedDifferently()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:40pt; height:40pt; border: 6pt inset rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.Equal(4, g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().Count());
            Assert.DoesNotContain(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(), p => !p.Stroked);
        }

        // ─── a rounded dotted/dashed border is one continuous outline ────────────

        [Fact]
        public async Task RoundedDottedBorder_StrokesOneContinuousOutline_NotAPathPerEdge()
        {
            // Per-edge rounded paths restart the dash phase four times, and the top edge's path already
            // carries both corner arcs - so dots pile up two and three deep at the corners.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:80pt; height:40pt; border: 8pt dotted rgb(51,51,51); border-radius: 16pt'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var stroked = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(), p => p.Stroked);
            Assert.Equal(RColor.FromArgb(51, 51, 51), stroked.Color);
        }

        [Theory]
        [InlineData("border-style: dotted; border-color: rgb(51,51,51); border-width: 8pt 4pt 8pt 4pt")]
        [InlineData("border: 8pt dashed; border-color: rgb(51,51,51) rgb(9,9,9) rgb(51,51,51) rgb(51,51,51)")]
        public async Task RoundedDottedOrDashedBorder_WithSidesThatDiffer_UsesOneSharedCornerPathPerEdge(string css)
        {
            // A single stroked outline needs one width and one color. Otherwise each edge gets the
            // width-ratio share of both adjacent corner arcs, and its pattern phase restarts there.
            var (root, container) = await BuildAndLayout(Wrap(
                $"<div id='b' style='width:80pt; height:40pt; border-radius: 16pt; {css}'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var stroked = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().Where(p => p.Stroked).ToList();
            Assert.Equal(4, stroked.Count);
            Assert.All(stroked, path => Assert.True(path.Points.Count > 4));
            Assert.Equal(4, g.ClipPaths.Count);
            Assert.All(g.ClipPaths, clip => Assert.True(clip.Points.Count > 8));
        }

        [Fact]
        public async Task RoundedSolidBorder_AllFourSidesAlike_StrokesOneContinuousOutline()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:100pt; height:40pt; border: 10pt solid rgb(51,51,51); border-radius: 20pt'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            // Four strokes that butt end-to-end leave the same antialiasing seam four abutting fills do.
            var stroked = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().Where(p => p.Stroked).ToList();
            Assert.Single(stroked);
            Assert.Equal(RColor.FromArgb(51, 51, 51), stroked[0].Color);
        }

        [Fact]
        public async Task RoundedSolidBorder_WithDifferentSideColors_FillsSharedCornerBands()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:100pt; height:40pt; border: 10pt solid; border-radius: 999px; " +
                "border-color: rgb(51,51,51) rgb(9,9,9) rgb(51,51,51) rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;
            var borderRect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.DoesNotContain(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(), path => path.Stroked);
            var fills = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();
            Assert.Equal(2, fills.Count);
            Assert.Equal(
                [RColor.FromArgb(51, 51, 51), RColor.FromArgb(9, 9, 9)],
                fills.Select(fill => fill.Color));
            Assert.All(fills.SelectMany(fill => fill.Points), point =>
            {
                Assert.InRange(point.X, borderRect.Left - 0.01, borderRect.Right + 0.01);
                Assert.InRange(point.Y, borderRect.Top - 0.01, borderRect.Bottom + 0.01);
            });
        }

        [Fact]
        public void StyledStrokeFittingClosed_LargerCountWouldLeaveNoGap_UsesTheSmallerOne()
        {
            // A 20-long closed outline with a 10-wide dotted border: two dots would exactly fill it and
            // leave no gap at all, so one dot with a 10 gap is the only real pattern.
            var fitted = StyledStrokeFitting.FitClosed(dotted: true, strokeWidth: 10, pathLength: 20);
            Assert.NotNull(fitted);
            Assert.Equal(10, fitted!.Value.DashLength, 3);
            Assert.Equal(10, fitted.Value.GapLength, 3);
        }

        [Fact]
        public void StyledStrokeFittingClosed_GivesAsManyGapsAsDashes()
        {
            // A closed run wraps around, so n dashes have n gaps and the count fixes the gap outright -
            // unlike an open edge, where n dashes have only n-1.
            var fitted = StyledStrokeFitting.FitClosed(dotted: true, strokeWidth: 10, pathLength: 200);
            Assert.NotNull(fitted);
            Assert.Equal(10, fitted!.Value.DashLength, 3);
            Assert.Equal(200 / 10d - 10, fitted.Value.GapLength, 3);

            // Too short for even one dash and its gap.
            Assert.Null(StyledStrokeFitting.FitClosed(dotted: false, strokeWidth: 10, pathLength: 12));
            Assert.Null(StyledStrokeFitting.FitClosed(dotted: true, strokeWidth: 0, pathLength: 200));
        }

        [Fact]
        public void EllipsePerimeter_MatchesTheKnownCircleAndDegenerateCases()
        {
            // A circle is the one case with a closed form, so it pins the approximation's accuracy.
            Assert.Equal(2 * Math.PI * 12, StyledStrokeFitting.EllipsePerimeter(12, 12), 3);
            Assert.Equal(0, StyledStrokeFitting.EllipsePerimeter(0, 0), 6);

            // A fully flattened "ellipse" degenerates to twice its long axis; Ramanujan lands close.
            Assert.Equal(40, StyledStrokeFitting.EllipsePerimeter(10, 0), 0);
        }

        [Fact]
        public void StyledStrokeFitting_LargerDashCountWouldLeaveNoGap_UsesTheSmallerOne()
        {
            // A 6-long edge with a 1-wide dashed border: 3 dashes of 2 fill it exactly, leaving no room
            // for any gap at all, so the 2-dash fit is the only real pattern.
            var fitted = StyledStrokeFitting.Fit(dotted: false, strokeWidth: 1, edgeLength: 6);
            Assert.NotNull(fitted);
            Assert.Equal(2, fitted!.Value.DashLength, 3);
            Assert.Equal(2, fitted.Value.GapLength, 3);
        }

        [Fact]
        public void StyledStrokeFitting_EdgeTooShortForTwoDashes_FallsBackToSolid()
        {
            Assert.Null(StyledStrokeFitting.Fit(dotted: false, strokeWidth: 16, edgeLength: 20));
            Assert.Null(StyledStrokeFitting.Fit(dotted: true, strokeWidth: 16, edgeLength: 0));
            Assert.Null(StyledStrokeFitting.Fit(dotted: true, strokeWidth: 0, edgeLength: 100));

            // A width small enough that the dash count would overflow an int falls back to solid rather
            // than casting to a negative count and producing nonsense geometry.
            Assert.Null(StyledStrokeFitting.Fit(dotted: true, strokeWidth: 1e-12, edgeLength: 1e9));
        }

        [Fact]
        public async Task RoundedGroove_SingleVisibleEdge_KeepsBothBeveledBandsAndOwnsTheCornerArcs()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: groove; border-top-width: 12px; border-top-color: rgb(51,51,51); border-radius: 8px'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var bands = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();
            Assert.Equal(2, bands.Count);
            Assert.DoesNotContain(bands, path => path.Stroked);
            Assert.All(bands, path => Assert.True(path.Points.Count > 8));

            var color = RColor.FromArgb(51, 51, 51);
            Assert.Equal(BorderBevelColors.Shade(color, darken: true), bands[0].Color);
            Assert.Equal(BorderBevelColors.Shade(color, darken: false), bands[1].Color);
        }

        [Theory]
        [InlineData("groove", true)]
        [InlineData("ridge", false)]
        public async Task RoundedGrooveRidge_AllFourSidesAlike_FillsTwoCurvedBeveledBands(
            string style, bool outerIsInset)
        {
            var (root, container) = await BuildAndLayout(Wrap(
                $"<div id='b' style='width:100pt; height:60pt; border: 12pt {style} rgb(51,51,51); border-radius: 20pt'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
            Assert.DoesNotContain(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(), p => p.Stroked);

            var bands = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();
            Assert.Equal(4, bands.Count);
            Assert.All(bands, band => Assert.True(band.Points.Count > 8));

            var color = RColor.FromArgb(51, 51, 51);
            var dark = BorderBevelColors.Shade(color, darken: true);
            var light = BorderBevelColors.Shade(color, darken: false);
            var outerTopLeft = outerIsInset ? dark : light;
            var outerBottomRight = outerIsInset ? light : dark;

            Assert.Equal([outerTopLeft, outerBottomRight, outerBottomRight, outerTopLeft],
                bands.Select(b => b.Color));

            // The first two paths form the outer half and reach the border box. The inner two begin
            // halfway through the 12pt border, so neither can reach its corresponding outer edge.
            var borderRect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;
            Assert.Equal(borderRect.Top, bands[0].Bounds.Top, 1);
            Assert.Equal(borderRect.Left, bands[0].Bounds.Left, 1);
            Assert.True(bands[2].Bounds.Top >= borderRect.Top + 6 - 0.1);
            Assert.True(bands[2].Bounds.Left >= borderRect.Left + 6 - 0.1);
        }

        [Fact]
        public async Task RoundedGroove_NonUniformWidthsAndColors_UsesPerEdgeCurvedBands()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:100pt; height:60pt; border-style:groove; " +
                "border-width:18px 6px 14px 10px; border-color:#d94a4a #4ad98a #4a90d9 #d9c74a; " +
                "border-radius:24px'>x</div>"));
            var div = FindById(root, "b")!;
            var borderRect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;
            var radii = div.ComputeRadii(borderRect);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
            var bands = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();
            Assert.Equal(8, bands.Count);
            Assert.DoesNotContain(bands, path => path.Stroked);

            // CSS Backgrounds 3 leaves the exact continuous width-ratio mapping UA-defined. Chrome's
            // historical mapping gives the left side 10/(18+10) of the top-left quadrant.
            var split = Math.PI + Math.PI / 2 * 10 / (18 + 10d);
            var expectedTransition = new RPoint(
                borderRect.Left + radii.TLX + radii.TLX * Math.Cos(split),
                borderRect.Top + radii.TLY + radii.TLY * Math.Sin(split));
            Assert.Equal(expectedTransition.X, bands[0].Points[0].X, 2);
            Assert.Equal(expectedTransition.Y, bands[0].Points[0].Y, 2);

            var top = RColor.FromArgb(217, 74, 74);
            var left = RColor.FromArgb(217, 199, 74);
            var bottom = RColor.FromArgb(74, 144, 217);
            var right = RColor.FromArgb(74, 217, 138);
            Assert.Equal(
                [
                    BorderBevelColors.Shade(top, darken: true),
                    BorderBevelColors.Shade(left, darken: true),
                    BorderBevelColors.Shade(bottom, darken: false),
                    BorderBevelColors.Shade(right, darken: false),
                    BorderBevelColors.Shade(top, darken: false),
                    BorderBevelColors.Shade(left, darken: false),
                    BorderBevelColors.Shade(bottom, darken: true),
                    BorderBevelColors.Shade(right, darken: true)
                ],
                bands.Select(band => band.Color));
        }

        [Fact]
        public async Task RoundedGrooveRidge_MayDifferByStyleBetweenSides()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:100pt; height:60pt; border-width:12pt; " +
                "border-style:groove ridge ridge groove; border-color:rgb(51,51,51); " +
                "border-radius:20pt'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var bands = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();
            Assert.Equal(2, bands.Count);
            Assert.DoesNotContain(bands, path => path.Stroked);

            // The chosen styles make every outer face dark and every inner face light. All four
            // disjoint sides of each shade must therefore be filled in one operation, avoiding seams.
            var color = RColor.FromArgb(51, 51, 51);
            Assert.Equal(BorderBevelColors.Shade(color, darken: true), bands[0].Color);
            Assert.Equal(BorderBevelColors.Shade(color, darken: false), bands[1].Color);
            Assert.All(bands, path => Assert.True(path.Points.Count > 32));
        }

        [Fact]
        public async Task RoundedGrooveRidge_MixedWithSolidAndDashed_KeepsBeveledBandsAndSharedCorners()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:100pt; height:60pt; border:18pt rgb(74,144,217); " +
                "border-style:groove solid ridge dashed; border-radius:36pt'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var paths = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();
            var fills = paths.Where(path => !path.Stroked).ToList();
            var dashed = Assert.Single(paths, path => path.Stroked);

            var color = RColor.FromArgb(74, 144, 217);
            Assert.Equal(
                [
                    BorderBevelColors.Shade(color, darken: true),
                    color,
                    BorderBevelColors.Shade(color, darken: false)
                ],
                fills.Select(fill => fill.Color));

            Assert.True(dashed.Points.Count > 4);
            var clip = Assert.Single(g.ClipPaths);
            Assert.True(clip.Points.Count > 8);

            var pushIndex = g.Log.FindIndex(entry => entry is TestRecordingGraphics.PushClipCall);
            var strokeIndex = g.Log.FindIndex(entry => ReferenceEquals(entry, dashed));
            var popIndex = g.Log.FindIndex(entry => entry is TestRecordingGraphics.PopClipCall);
            Assert.True(pushIndex >= 0 && pushIndex < strokeIndex && strokeIndex < popIndex);
        }

        [Fact]
        public async Task RoundedDouble_MixedWithOtherFilledStyles_RetainsBothLines()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:100pt; height:60pt; border-width:12pt; " +
                "border-style:double solid outset outset; border-color:rgb(51,51,51); " +
                "border-radius:20pt'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.DoesNotContain(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(), path => path.Stroked);
            var fills = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();

            var color = RColor.FromArgb(51, 51, 51);
            Assert.Equal(2, fills.Count(fill => fill.Color == color));
            Assert.Contains(fills, fill => fill.Color == BorderBevelColors.Shade(color, darken: true));
            Assert.Contains(fills, fill => fill.Color == BorderBevelColors.Shade(color, darken: false));
        }

        [Fact]
        public async Task RoundedGroove_SlicedFragmentWithoutLeftEdge_UsesOpenBeveledBands()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:100pt; height:60pt; border: 12pt groove rgb(51,51,51); border-radius: 20pt'>x</div>"));
            var div = FindById(root, "b")!;
            var borderRect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            BordersDrawHandler.DrawBoxBorders(
                g, div, borderRect,
                hasLeftEdge: false, hasRightEdge: true, hasTopEdge: true, hasBottomEdge: true);

            var bands = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();
            Assert.Equal(4, bands.Count);
            Assert.DoesNotContain(bands, path => path.Stroked);

            // A slice break is not a physical rounded corner: the top and bottom bands end square at
            // the fragment's left edge instead of closing a ring or curving into an absent left border.
            Assert.Contains(bands.SelectMany(path => path.Points), point =>
                Math.Abs(point.X - borderRect.Left) < 0.01 &&
                Math.Abs(point.Y - borderRect.Top) < 0.01);
            Assert.Contains(bands.SelectMany(path => path.Points), point =>
                Math.Abs(point.X - borderRect.Left) < 0.01 &&
                Math.Abs(point.Y - borderRect.Bottom) < 0.01);
        }

        [Theory]
        [InlineData(0, false, false, true, false)]
        [InlineData(1, false, true, false, false)]
        [InlineData(2, false, false, false, true)]
        [InlineData(3, true, false, false, false)]
        public async Task RoundedDashed_SlicedFragment_UsesSquareOpenCenterlineEnds(
            int sideValue, bool hasLeftEdge, bool hasRightEdge, bool hasTopEdge, bool hasBottomEdge)
        {
            var side = (Border)sideValue;
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:100pt; height:60pt; border:12pt dashed rgb(51,51,51); " +
                "border-radius:20pt'>x</div>"));
            var div = FindById(root, "b")!;
            var borderRect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            BordersDrawHandler.DrawBoxBorders(
                g, div, borderRect,
                hasLeftEdge, hasRightEdge, hasTopEdge, hasBottomEdge);

            var stroke = Assert.Single(
                g.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                path => path.Stroked);
            Assert.Equal(2, stroke.Points.Count);
            Assert.Single(g.ClipPaths);

            if (side is Border.Top or Border.Bottom)
            {
                Assert.Equal(borderRect.Left, stroke.Points.Min(point => point.X), 3);
                Assert.Equal(borderRect.Right, stroke.Points.Max(point => point.X), 3);
                Assert.True(stroke.Points[0].X < stroke.Points[^1].X);
            }
            else
            {
                Assert.Equal(borderRect.Top, stroke.Points.Min(point => point.Y), 3);
                Assert.Equal(borderRect.Bottom, stroke.Points.Max(point => point.Y), 3);
                Assert.True(stroke.Points[0].Y < stroke.Points[^1].Y);
            }
        }

        [Fact]
        public async Task RoundedGroove_NonUniformBandsScalePathCoordinatesByPixelsPerPoint()
        {
            const string html =
                "<div id='b' style='width:100pt; height:60pt; border-style:groove; " +
                "border-width:18pt 6pt 14pt 10pt; border-radius:24pt'>x</div>";

            var (rootDefault, containerDefault) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var divDefault = LayoutHarness.FindById(rootDefault, "b")!;
            var gDefault = new TestRecordingGraphics { PixelsPerPointOverride = 1 };
            FragmentPaintHarness.PaintBox(containerDefault, divDefault, gDefault);
            var defaultBands = gDefault.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();

            var (rootScaled, containerScaled) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(html), pixelsPerPoint: 2);
            var divScaled = LayoutHarness.FindById(rootScaled, "b")!;
            var gScaled = new TestRecordingGraphics { PixelsPerPointOverride = 2 };
            FragmentPaintHarness.PaintBox(containerScaled, divScaled, gScaled);
            var scaledBands = gScaled.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();

            Assert.Equal(defaultBands.Count, scaledBands.Count);
            for (var i = 0; i < defaultBands.Count; i++)
            {
                Assert.Equal(defaultBands[i].Bounds.Left, scaledBands[i].Bounds.Left, 2);
                Assert.Equal(defaultBands[i].Bounds.Top, scaledBands[i].Bounds.Top, 2);
                Assert.Equal(defaultBands[i].Bounds.Width, scaledBands[i].Bounds.Width, 2);
                Assert.Equal(defaultBands[i].Bounds.Height, scaledBands[i].Bounds.Height, 2);
            }
        }

        [Fact]
        public async Task RoundedGroove_InsetContoursRenormalizeAsymmetricRadii()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:60pt; height:60pt; border-style:groove; " +
                "border-width:1pt 1pt 1pt 20pt; border-radius:1pt 80pt 1pt 80pt / 20pt'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var topOuterBand = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().First();
            Assert.True(topOuterBand.Points.Count > 12);

            // Points 11 and 12 are the inner top contour's right and left tangent respectively. The
            // right tangent must not pass left of the left tangent after the contour shrinks.
            Assert.True(topOuterBand.Points[11].X >= topOuterBand.Points[12].X - 0.01,
                $"inner contour reversed from x={topOuterBand.Points[11].X} to x={topOuterBand.Points[12].X}");
        }

        [Fact]
        public async Task RoundedDoubleBorder_AllFourSidesAlike_StrokesTwoConcentricOutlinesAtTheThirds()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:100pt; height:60pt; border: 12pt double rgb(51,51,51); border-radius: 20pt'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            // Two outlines rather than one: a pen draws a single band, so `double` needs one stroke per
            // line. They have a gap between them, so unlike four abutting edges they cannot seam.
            var stroked = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().Where(p => p.Stroked).ToList();
            Assert.Equal(2, stroked.Count);
            Assert.All(stroked, p => Assert.Equal(RColor.FromArgb(51, 51, 51), p.Color));

            // CSS 2.1 §8.5.3's equal thirds: each line is 4pt of the 12pt border, so the outer line is
            // centred 2pt in and the inner one 10pt in - their paths are 8pt apart on every side.
            var outer = stroked.OrderByDescending(p => p.Bounds.Width).First();
            var inner = stroked.OrderBy(p => p.Bounds.Width).First();
            Assert.Equal(16, outer.Bounds.Width - inner.Bounds.Width, 1);
            Assert.Equal(16, outer.Bounds.Height - inner.Bounds.Height, 1);
        }


        // ─── border-style 2-value shorthand + per-side suppression (Acid2's "[class~=one].first.one") ──
        // "border-style: none solid" must expand to top=bottom=none, left=right=solid (CSS2.1's 1/2/3/4-
        // value box-shorthand expansion), and only the solid sides may actually paint.

        [Fact]
        public async Task BorderStyleTwoValueShorthand_OnlyPaintsTheSolidSides()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:40px; height:40px; border-width:4px; border-color:rgb(51,51,51); border-style: none solid'>x</div>"));
            var div = FindById(root, "b")!;

            Assert.Equal(LineStyle.None, div.BorderTopStyle.Value);
            Assert.Equal(LineStyle.Solid, div.BorderRightStyle.Value);
            Assert.Equal(LineStyle.None, div.BorderBottomStyle.Value);
            Assert.Equal(LineStyle.Solid, div.BorderLeftStyle.Value);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            // Solid borders paint as a mitered quad (BordersDrawHandler.SetInOutsetRectanglePoints),
            // not a single line - see BordersDrawHandler's own doc comment on why (the classic CSS
            // "border triangle" technique, which Acid2's own nose diamond relies on, needs a real
            // diagonal miter at each corner, not a thick straight line that just overlaps whichever
            // adjacent border painted before it).
            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            // Two sides painted (left, right), each as a mitered quad - top/bottom (none) draw nothing.
            Assert.Equal(2, polys.Count);
            Assert.All(polys, p => Assert.Equal(RColor.FromArgb(51, 51, 51), p.Color));

            // A vertical (left/right) side quad spans more in Y than X; a horizontal one would be the
            // reverse.
            Assert.All(polys, p =>
            {
                var minX = p.Points.Min(pt => pt.X);
                var maxX = p.Points.Max(pt => pt.X);
                var minY = p.Points.Min(pt => pt.Y);
                var maxY = p.Points.Max(pt => pt.Y);
                Assert.True(maxY - minY > maxX - minX,
                    "expected only vertical (left/right) border quads, none horizontal");
            });
        }

        // ─── border-color/border-width 4-value and 2-value expansion resolve per-side ──
        // Acid2's ".nose div div:before { border-color: red yellow black yellow; border-width: 1em; }"
        // (4-value color) and ".picture p { ... }" style earlier "border-width: 0 2em" (2-value) shapes.

        [Fact]
        public async Task BorderColorFourValueShorthand_ResolvesTopRightBottomLeftPerSide()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-style:solid; border-width:1px; border-color: rgb(1,0,0) rgb(0,1,0) rgb(0,0,1) rgb(1,1,0)'>x</div>"));
            var div = FindById(root, "b")!;

            Assert.Equal("rgb(1, 0, 0)", div.BorderTopColor);
            Assert.Equal("rgb(0, 1, 0)", div.BorderRightColor);
            Assert.Equal("rgb(0, 0, 1)", div.BorderBottomColor);
            Assert.Equal("rgb(1, 1, 0)", div.BorderLeftColor);
        }

        [Fact]
        public async Task BorderWidthTwoValueShorthand_ThenLaterOneValue_OverridesAllSidesPerSpecificity()
        {
            // Mirrors the fixture's own "border-width: 0 2em" (2-value: top/bottom=0, left/right=2em)
            // followed later by a same-specificity "border-width: 1em" (all sides) - the later rule
            // must win outright on every side, not merge/leave the 2-value expansion partially intact.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-style:solid'></div>"
                + "<style>#b { border-width: 0 2em; } #b { border-width: 1em; }</style>"));
            var div = FindById(root, "b")!;

            Assert.Equal("1em", div.BorderTopWidth);
            Assert.Equal("1em", div.BorderRightWidth);
            Assert.Equal("1em", div.BorderBottomWidth);
            Assert.Equal("1em", div.BorderLeftWidth);
        }

        // ─── deprecated presentational `border` HTML attribute always resolves solid ──
        // DomParser.CascadeApplyStyles' HtmlConstants.Border case: a non-zero `border` attribute forces
        // every side's style to solid (CssProperty<LineStyle>.FromValue(Keywords.Solid, LineStyle.Solid)
        // / the shared SolidBorderStyle constant). Only the plain-element path is covered here - the
        // table-to-cell cascade (ApplyTableBorder/SetForAllCells, issue #636) is covered by
        // PresentationalAttributeIntegrationTests.BorderAttribute_OnTable_CascadesASolidBorderToCells.

        [Fact]
        public async Task PresentationalBorderAttribute_OnAPlainElement_ForcesSolidOnAllSides()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='b' border='1'>x</div>"));
            var div = FindById(root, "b")!;

            Assert.Equal(LineStyle.Solid, div.BorderTopStyle.Value);
            Assert.Equal(LineStyle.Solid, div.BorderRightStyle.Value);
            Assert.Equal(LineStyle.Solid, div.BorderBottomStyle.Value);
            Assert.Equal(LineStyle.Solid, div.BorderLeftStyle.Value);
        }

        // ─── issue #851: pen stroke width ignores non-default PixelsPerInch ────────

        [Fact]
        public async Task BorderStyleDotted_PenWidth_IsInvariantUnderNonDefaultPixelsPerInch()
        {
            const string html = "<div id='b' style='border: 12pt dotted rgb(51,51,51)'>x</div>";

            var (rootDefault, containerDefault) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var divDefault = LayoutHarness.FindById(rootDefault, "b");
            Assert.NotNull(divDefault);
            var gDefault = new TestRecordingGraphics { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintBox(containerDefault, divDefault!, gDefault);
            var widthsDefault = gDefault.Log.OfType<TestRecordingGraphics.DrawLineCall>().Select(l => l.Width).ToList();

            var (rootScaled, containerScaled) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html), pixelsPerPoint: 2.0);
            var divScaled = LayoutHarness.FindById(rootScaled, "b");
            Assert.NotNull(divScaled);
            var gScaled = new TestRecordingGraphics { PixelsPerPointOverride = 2.0 };
            FragmentPaintHarness.PaintBox(containerScaled, divScaled!, gScaled);
            var widthsScaled = gScaled.Log.OfType<TestRecordingGraphics.DrawLineCall>().Select(l => l.Width).ToList();

            Assert.NotEmpty(widthsDefault);
            Assert.Equal(widthsDefault.Count, widthsScaled.Count);
            for (var i = 0; i < widthsDefault.Count; i++)
                Assert.Equal(widthsDefault[i], widthsScaled[i], 3);

            // Sanity: the pen width should actually equal the declared 12pt border width.
            Assert.All(widthsDefault, w => Assert.Equal(12, w, 1));
        }

        [Fact]
        public async Task BorderStyleDoubleAndGroove_StripeWidths_AreInvariantUnderNonDefaultPixelsPerInch()
        {
            const string html = "<div id='b' style='border-top-style: double; border-top-width: 12pt; " +
                                 "border-top-color: rgb(51,51,51); border-left-style: groove; " +
                                 "border-left-width: 12pt; border-left-color: rgb(51,51,51)'>x</div>";

            var (rootDefault, containerDefault) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var divDefault = LayoutHarness.FindById(rootDefault, "b");
            Assert.NotNull(divDefault);
            var gDefault = new TestRecordingGraphics { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintBox(containerDefault, divDefault!, gDefault);
            var bandsDefault = gDefault.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().Select(Band).ToList();

            var (rootScaled, containerScaled) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html), pixelsPerPoint: 2.0);
            var divScaled = LayoutHarness.FindById(rootScaled, "b");
            Assert.NotNull(divScaled);
            var gScaled = new TestRecordingGraphics { PixelsPerPointOverride = 2.0 };
            FragmentPaintHarness.PaintBox(containerScaled, divScaled!, gScaled);
            var bandsScaled = gScaled.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().Select(Band).ToList();

            // double (top) contributes 2 bands, groove (left) contributes 2 more.
            Assert.Equal(4, bandsDefault.Count);
            Assert.Equal(bandsDefault.Count, bandsScaled.Count);

            // A band is a polygon, so its coordinates stay in layout space and the adapter divides them
            // on the way out - meaning the scaled run SHOULD be exactly PixelsPerPoint larger. What this
            // guards is a thickness that skipped or double-applied that correction (issue #851). Only
            // the thickness is compared: a band's length follows the edge, whose size depends on text
            // measurement that does not scale linearly.
            for (var i = 0; i < bandsDefault.Count; i++)
                Assert.Equal(bandsDefault[i].Thickness, bandsScaled[i].Thickness / 2.0, 3);
        }

        [Fact]
        public async Task CollapsedBorderStyleDouble_StripeWidths_AreInvariantUnderNonDefaultPixelsPerInch()
        {
            // BordersDrawHandler.DrawDoubleOrGrooveRidgeSegment is the collapsed-table-border twin of
            // DrawDoubleOrGrooveRidgeBorder covered above - a separate code path (CollapsedBorderModel's
            // resolved segments, not a box's own DrawBoxBorders) with its own scaling.
            var html = LayoutHarness.Wrap(
                "<table style='border-collapse:collapse'><tr><td style='border:12pt double rgb(51,51,51)'>x</td></tr></table>");

            var (rootDefault, containerDefault) = await LayoutHarness.LayoutAsync(html);
            var gDefault = new TestRecordingGraphics { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintPage(containerDefault, gDefault);
            var bandsDefault = gDefault.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().Select(Band).ToList();

            var (rootScaled, containerScaled) = await LayoutHarness.LayoutAsync(html, pixelsPerPoint: 2.0);
            var gScaled = new TestRecordingGraphics { PixelsPerPointOverride = 2.0 };
            FragmentPaintHarness.PaintPage(containerScaled, gScaled);
            var bandsScaled = gScaled.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().Select(Band).ToList();

            Assert.NotEmpty(bandsDefault);
            Assert.Equal(bandsDefault.Count, bandsScaled.Count);
            for (var i = 0; i < bandsDefault.Count; i++)
                Assert.Equal(bandsDefault[i].Thickness, bandsScaled[i].Thickness / 2.0, 3);
        }

        [Fact]
        public async Task CollapsedBorderStyleGroove_ShadesARowSegmentLikeATopEdge()
        {
            // A collapsed segment has no owning box, so css-tables-3's grid lines borrow a box side: a
            // row (horizontal) line shades like a top edge, a column line like a left one.
            var html = LayoutHarness.Wrap(
                "<table style='border-collapse:collapse'><tr><td style='border:12pt groove rgb(51,51,51)'>x</td></tr></table>");

            var (_, container) = await LayoutHarness.LayoutAsync(html);
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var dark = BorderBevelColors.Shade(RColor.FromArgb(51, 51, 51), darken: true);
            var light = BorderBevelColors.Shade(RColor.FromArgb(51, 51, 51), darken: false);

            var horizontal = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                .Select(Band)
                .Where(b => b.Width > b.Height)
                .OrderBy(b => b.Top)
                .ToList();

            Assert.NotEmpty(horizontal);
            Assert.Equal(dark, horizontal[0].Color);
            Assert.Equal(light, horizontal[1].Color);
        }

        [Theory]
        [InlineData("dotted", true)]
        [InlineData("dashed", false)]
        public async Task CollapsedBorderStyleDottedOrDashed_FitsThePatternToEachSegment(string style, bool dotted)
        {
            // The collapsed-segment path is a separate implementation from a box's own four edges
            // (CollapsedBorderModel's resolved segments), and got the same fitted-pattern treatment.
            var html = LayoutHarness.Wrap(
                $"<table style='border-collapse:collapse; width:400pt'><tr>" +
                $"<td style='height:80pt; border:8pt {style} rgb(51,51,51)'>x</td></tr></table>");

            var (_, container) = await LayoutHarness.LayoutAsync(html);
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.Equal(dotted ? RLineCap.Round : RLineCap.Butt, l.LineCap));
            Assert.All(lines, l =>
            {
                Assert.NotNull(l.DashPattern);
                Assert.Equal(dotted ? 0 : 16, l.DashPattern![0], 1);
            });
        }

        [Fact]
        public async Task CollapsedBorderStyleDashed_SegmentTooShortForAPattern_FallsBackToASolidStroke()
        {
            // A cell narrower than one dash+gap has no pattern to fit; the stroke degenerates to solid
            // rather than emitting a dash array that would paint nothing at all.
            var html = LayoutHarness.Wrap(
                "<table style='border-collapse:collapse'><tr>" +
                "<td style='width:4pt; height:4pt; border:20pt dashed rgb(51,51,51)'></td></tr></table>");

            var (_, container) = await LayoutHarness.LayoutAsync(html);
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.NotEmpty(lines);
            Assert.Contains(lines, l => l.DashPattern is null && l.DashStyle == RDashStyle.Solid);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>A filled band's colour and axis-aligned bounds - every border style except
        /// dotted/dashed now paints bands (mitred quads) rather than stroked lines.</summary>
        private readonly record struct BandInfo(RColor Color, double Left, double Top, double Width, double Height)
        {
            public double Right => Left + Width;
            public double Bottom => Top + Height;

            /// <summary>The band's minor axis - its thickness across the border, independent of how long
            /// the edge it belongs to happens to be.</summary>
            public double Thickness => Math.Min(Width, Height);
        }

        private static BandInfo Band(TestRecordingGraphics.DrawPolygonCall p)
        {
            var left = p.Points.Min(pt => pt.X);
            var top = p.Points.Min(pt => pt.Y);
            return new BandInfo(p.Color, left, top, p.Points.Max(pt => pt.X) - left, p.Points.Max(pt => pt.Y) - top);
        }

        /// <summary>Every horizontal (top/bottom edge) band, ordered top to bottom.</summary>
        private static List<BandInfo> HorizontalBands(TestRecordingGraphics g) =>
            g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                .Select(Band)
                .Where(b => b.Width > b.Height)
                .OrderBy(b => b.Top)
                .ToList();

        private static double LeftOf(CssBox box) => box.Location.X;

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
