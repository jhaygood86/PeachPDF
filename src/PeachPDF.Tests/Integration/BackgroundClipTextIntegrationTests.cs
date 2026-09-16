using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>background-clip: text</c> (issue #1117): every background layer clips to the union of the
    /// box's own laid-out glyph outlines rather than the whole border box. Structural/paint-order
    /// assertions against <see cref="RecordingGraphics"/> per this repo's testing conventions - a
    /// content-stream-substring check alone would pass just as well for a fully-broken implementation
    /// that paints the whole box (see <c>PaddingContentEdgeRadiusPaintIntegrationTests</c> for the same
    /// convention applied to the sibling <c>background-clip</c> values).
    /// </summary>
    public class BackgroundClipTextIntegrationTests
    {
        /// <summary>A distinct, non-null stand-in outline per call, so a test can tell how many runs contributed to a union.</summary>
        private static RGraphicsPath FakeOutline(RGraphics g, string text, RPoint origin)
        {
            var path = g.GetGraphicsPath();
            path.Start(origin.X, origin.Y);
            path.LineTo(origin.X + text.Length * 10, origin.Y);
            path.LineTo(origin.X + text.Length * 10, origin.Y - 10);
            path.CloseFigure();
            return path;
        }

        [Fact]
        public async Task SolidColor_PaintsViaDrawPath_NotDrawRectangle()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<h1 id='box' style='margin:0;font-size:40pt;" +
                "background-color:red;background-clip:text;color:transparent;'>Hi</h1>"));

            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            recording.GetTextOutlineOverride = (text, _, origin, _, _) => FakeOutline(recording, text, origin);

            FragmentPaintHarness.PaintBox(container, box!, recording);

            Assert.DoesNotContain(recording.Log, op => op.Kind == PaintOpKind.FillRect);
            Assert.Single(recording.DrawnPaths);
            Assert.NotEmpty(recording.DrawnPaths[0].Points);
        }

        [Fact]
        public async Task NestedSpan_BothRunsContributeToTheUnion()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<h1 id='box' style='margin:0;font-size:40pt;" +
                "background-color:red;background-clip:text;color:transparent;'>" +
                "Rev<span id='inner' style='font-weight:bold'>enue</span></h1>"));

            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            recording.GetTextOutlineOverride = (text, _, origin, _, _) => FakeOutline(recording, text, origin);

            FragmentPaintHarness.PaintBox(container, box!, recording);

            Assert.Equal(["Rev", "enue"], recording.GetTextOutlineCalls.Select(c => c.Text));
            Assert.Single(recording.DrawnPaths);
            Assert.Equal(2, recording.DrawnPaths[0].UnionedPathCount);
        }

        [Fact]
        public async Task WrappedAcrossTwoLines_PaintsOnePathPerLine()
        {
            // A narrow inline box that wraps 'one two three' onto two lines - background-clip: text on
            // the *inline* box means BoxDecorationGeometry resolves per line box, and PaintBackground
            // (hence the text-outline union) runs once per line, each getting its own DrawPath call.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='width:80pt;font-size:20pt;'>" +
                "<span id='box' style='background-color:red;background-clip:text;color:transparent;'>" +
                "one two three</span></div>"));

            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            recording.GetTextOutlineOverride = (text, _, origin, _, _) => FakeOutline(recording, text, origin);

            FragmentPaintHarness.PaintBox(container, box!, recording);

            Assert.True(recording.DrawnPaths.Count >= 2, $"expected the wrapped span to paint at least 2 line rectangles, got {recording.DrawnPaths.Count}");
        }

        [Fact]
        public async Task UnsupportedFont_FallsBackToPlainBorderBoxRectangle()
        {
            // GetTextOutline returning null for every run (a CID-keyed CFF or bitmap font, or a
            // vertical writing mode) must fall back to exactly today's pre-#1117 (unrecognized
            // background-clip value) behavior: a plain border-box rectangle fill, not a hole-punched or
            // partially-missing shape.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<h1 id='box' style='margin:0;font-size:40pt;" +
                "background-color:red;background-clip:text;color:transparent;'>Hi</h1>"));

            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter())
            {
                GetTextOutlineOverride = (_, _, _, _, _) => null
            };

            FragmentPaintHarness.PaintBox(container, box!, recording);

            Assert.Empty(recording.DrawnPaths);
            Assert.Single(recording.Log, op => op.Kind == PaintOpKind.FillRect);
        }

        [Fact]
        public async Task VerticalWritingMode_FallsBackToPlainBorderBoxRectangle()
        {
            // A box set to a vertical writing-mode (accepted gap - see
            // .claude/accepted-gaps/background-clip-text-vertical-writing-mode.md) falls back the same
            // way an outline-less font does: BuildTextClipPath never even calls GetTextOutline for it.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<h1 id='box' style='margin:0;font-size:40pt;writing-mode:vertical-rl;" +
                "background-color:red;background-clip:text;color:transparent;'>Hi</h1>"));

            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter());

            FragmentPaintHarness.PaintBox(container, box!, recording);

            Assert.Empty(recording.GetTextOutlineCalls);
            Assert.Empty(recording.DrawnPaths);
            Assert.Single(recording.Log, op => op.Kind == PaintOpKind.FillRect);
        }

        [Fact]
        public async Task UnsupportedFont_RoundedBox_FallsBackToRoundedBorderBox()
        {
            // Same fallback, but on a box with border-radius: the fallback must still curve the corners
            // (exactly border-box's own existing treatment), not silently lose the rounding along with
            // the text clip.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<h1 id='box' style='margin:0;font-size:40pt;border-radius:12pt;" +
                "background-color:red;background-clip:text;color:transparent;'>Hi</h1>"));

            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter())
            {
                GetTextOutlineOverride = (_, _, _, _, _) => null
            };

            FragmentPaintHarness.PaintBox(container, box!, recording);

            Assert.Single(recording.DrawnPaths);
            var arcs = recording.DrawnPaths[0].Arcs;
            Assert.NotEmpty(arcs);
            Assert.All(arcs, arc => Assert.Equal(12.0, arc.RadiusX, 2));
        }

        [Fact]
        public async Task EmptyBox_PaintsEmptyPathWithoutThrowing()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:40pt;" +
                "background-color:red;background-clip:text;'></div>"));

            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            recording.GetTextOutlineOverride = (text, _, origin, _, _) => FakeOutline(recording, text, origin);

            var exception = Record.Exception(() => FragmentPaintHarness.PaintBox(container, box!, recording));

            Assert.Null(exception);
            Assert.Single(recording.DrawnPaths);
            Assert.Empty(recording.DrawnPaths[0].Points);
            Assert.Empty(recording.GetTextOutlineCalls);
        }

        [Fact]
        public async Task EndToEnd_GeneratesPdfWithClippedGradientText()
        {
            // The issue's own repro, through the real pipeline (real font resolution, real
            // GetTextOutline via GlyphOutlineDecoder/Type2CharstringInterpreter, real PDF write) - not
            // itself proof of correct geometry (that's what the RecordingGraphics tests above are for),
            // but proof the feature doesn't throw and does reach the gradient/clip paint path end to end.
            const string html = "<!DOCTYPE html><html><body style='margin:0'>" +
                                 "<h1 style='font-size:48pt;margin:0;" +
                                 "background:linear-gradient(to right,#e11,#11e);" +
                                 "background-clip:text;color:transparent'>Revenue</h1>" +
                                 "</body></html>";

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var doc = await generator.GeneratePdf(html, config);

            var ms = new MemoryStream();
            doc.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task EndToEnd_TextClipGeometry_IsScaleInvariantAcrossPixelsPerInch()
        {
            // Regression test for a real bug found only by rendering the repro and rasterizing it
            // (per this repo's testing conventions - a structural/RecordingGraphics-mocked test
            // cannot catch this, since the mock never exercises real font/coordinate math): the
            // glyph-outline union path is built in GetTextOutline's own coordinate space, which
            // - like DrawString's own `point` parameter - is layout-pixel units (PixelsPerPoint
            // included), not the "points" space RenderUtils.GetRoundRect's rounded-clip paths use
            // before either can share PaintClippedBrush/DrawPath; at a non-default PixelsPerInch this
            // scaled the whole clip shape away from the glyphs' real position. A second, independent
            // bug compounded it: DrawString positions from the top-left of a word's own box, but
            // GetTextOutline places the baseline directly (SvgRenderer.PaintTextGlyphs already shifts
            // by `font.Ascent` for the identical mismatch) - together, only a sliver of "Hi" survived
            // inside the visible clip instead of the whole word.
            const string html = "<!DOCTYPE html><html><body style='margin:0'>" +
                                 "<h1 style='font-size:48pt;margin:0;" +
                                 "background-color:red;background-clip:text;color:transparent'>Hi</h1>" +
                                 "</body></html>";

            async Task<(double Width, double Height)> ClipBoundsAt(double pixelsPerInch)
            {
                var generator = new PdfGenerator();
                var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false, PixelsPerInch = pixelsPerInch };
                var doc = await generator.GeneratePdf(html, config);
                var ms = new MemoryStream();
                doc.Save(ms);
                var pdfText = Encoding.Latin1.GetString(ms.ToArray());

                // The h1's own solid-color fill - "1 0 0 rg" (red) immediately precedes the glyph
                // union path DrawPath draws, ended by the "f" that fills it. Scoping to this one
                // block (rather than scanning the whole stream) excludes unrelated path-based clips
                // elsewhere in the document (e.g. the page's own content-area clip rectangle), which
                // would otherwise dwarf the glyph shape's own real bounds.
                var colorIndex = pdfText.IndexOf("1 0 0 rg", System.StringComparison.Ordinal);
                Assert.True(colorIndex >= 0, "expected the h1's red fill color operator");
                var fillIndex = pdfText.IndexOf("\nf\n", colorIndex, System.StringComparison.Ordinal);
                Assert.True(fillIndex >= 0, "expected a fill operator ending the glyph union path");
                var pathSegment = pdfText[colorIndex..fillIndex];

                // Every "x y m"/"x y l" path-construction operator's own endpoint within that one
                // block - a coarse but real cross-check that the actual painted clip geometry (not
                // just "some path exists") lands at a plausible, DPI-independent size in PDF points.
                var points = System.Text.RegularExpressions.Regex.Matches(pathSegment, @"(-?[\d.]+) (-?[\d.]+) [ml]\r?\n")
                    .Select(m => (
                        X: double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                        Y: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
                    .ToList();

                Assert.NotEmpty(points);
                return (points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y));
            }

            var at72Dpi = await ClipBoundsAt(72);
            var at150Dpi = await ClipBoundsAt(150);

            // A plausible size for "Hi" at 48pt - well within the page, nowhere near zero (the old
            // bug's symptom: only a sliver of the glyphs' true bounds survived).
            Assert.InRange(at72Dpi.Height, 20, 60);
            Assert.InRange(at72Dpi.Width, 10, 120);

            // Same markup at a different PixelsPerInch must paint the identical shape in PDF points -
            // PixelsPerInch is a pure internal layout-coordinate-scale knob with zero intended visual
            // effect, the same invariant this repo already pins for the sibling border-radius clip
            // path (PaddingContentEdgeRadiusPaintIntegrationTests.OverflowHiddenClipCurve_ArcRadiiAndPosition_AreInvariantUnderNonDefaultPixelsPerInch).
            Assert.Equal(at72Dpi.Width, at150Dpi.Width, 1);
            Assert.Equal(at72Dpi.Height, at150Dpi.Height, 1);
        }
    }
}
