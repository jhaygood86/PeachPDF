using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Fragments;
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
        public async Task SidewaysWritingMode_FallsBackToPlainBorderBoxRectangle()
        {
            // writing-mode: sideways-rl/-lr is a different, genuinely out-of-scope value (see
            // IsHorizontalWritingMode's own remarks) - unlike vertical-rl/-lr (issue #1123, see the
            // VerticalRl_*/VerticalLr_* tests below), it still falls back exactly like an outline-less
            // font does: BuildTextClipPath never even calls GetTextOutline for it.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<h1 id='box' style='margin:0;font-size:40pt;writing-mode:sideways-rl;" +
                "background-color:red;background-clip:text;color:transparent;'>Hi</h1>"));

            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter());

            FragmentPaintHarness.PaintBox(container, box!, recording);

            Assert.Empty(recording.GetTextOutlineCalls);
            Assert.Empty(recording.DrawnPaths);
            Assert.Single(recording.Log, op => op.Kind == PaintOpKind.FillRect);
        }

        /// <summary>
        /// A stand-in outline whose points sit far from the word's own physical footprint (near the
        /// coordinate origin) - so a test can tell whether <see cref="FragmentPainter.SidewaysRotation"/>
        /// (via <see cref="RGraphicsPath.Transform"/>) actually ran: an untransformed (bugged) union
        /// would still contain these origin-relative points, while a correctly transformed one lands
        /// inside the word's real fragment rectangle instead.
        /// </summary>
        private static RGraphicsPath OriginRelativeOutline(RGraphics g, double width, double height)
        {
            // Both dimensions are non-negative - matching where a natural, pre-rotation glyph box
            // actually sits (DrawString's own "point" argument is its top-left, not its center), so a
            // correctly transformed shape lands fully inside the word's own physical footprint below.
            var path = g.GetGraphicsPath();
            path.Start(0, 0);
            path.LineTo(width, 0);
            path.LineTo(width, height);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// An oversized stand-in outline reaching well past <paramref name="cell"/> on every side - used
        /// to prove <see cref="RGraphicsPath.ClipToRect"/> actually confines a character's glyph outline
        /// to its own reserved cell rather than unioning it in raw (issue #1194).
        /// </summary>
        private static RGraphicsPath OversizedOutline(RGraphics g, RRect cell)
        {
            const double overflow = 30;
            var path = g.GetGraphicsPath();
            path.Start(cell.X - overflow, cell.Y - overflow);
            path.LineTo(cell.Right + overflow, cell.Y - overflow);
            path.LineTo(cell.Right + overflow, cell.Bottom + overflow);
            path.LineTo(cell.X - overflow, cell.Bottom + overflow);
            path.CloseFigure();
            return path;
        }

        [Fact]
        public async Task UprightRun_WithRealVerticalMetrics_ClipsUnionToReservedCell()
        {
            // Issue #1194 (found during #1123's own review): a font with real vhea/vmtx metrics makes
            // PaintUprightVerticalRun clip each character's PAINT to its own reserved cell (a real vmtx
            // advance is routinely narrower than the font's line height) - CollectUprightWord now
            // reproduces that same clip on the glyph-outline UNION via RGraphicsPath.ClipToRect, rather
            // than falling back to border-box. BundledFonts.Cjk genuinely carries real vhea/vmtx data
            // (see TextOrientationIntegrationTests's own use of it).
            //
            // A single-character upright run's own reserved cell is exactly its own word rect: layout
            // (CssLayoutEngine.NaturalWordSize) already sized that rect from the same real vmtx advance
            // EnumerateUprightGlyphPlacements resolves for paint, and with only one character there is no
            // second cell sharing the rect. So an oversized stand-in outline reaching well past the word
            // rect on every side must still come back clipped to it.
            var html = "<!DOCTYPE html><html><head><style>" +
                "body { font-family: 'CJK'; margin: 0 }" +
                "</style></head><body>" +
                "<h1 id='box' style='font-size:40pt;writing-mode:vertical-rl;text-orientation:upright;" +
                "background-color:red;background-clip:text;color:transparent;'>テ</h1>" +
                "</body></html>";

            var (root, container) = await LayoutHarness.LayoutAsync(html,
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.Cjk, "CJK"));
            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);
            Assert.True(box!.ActualFont.HasVerticalMetrics, "the bundled CJK test font should carry real vhea/vmtx metrics");

            var wordRect = FindWordRect(FragmentPaintHarness.FragmentOf(container, box));

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            recording.GetTextOutlineOverride = (_, _, _, _, _) => OversizedOutline(recording, wordRect);

            FragmentPaintHarness.PaintBox(container, box, recording);

            Assert.Single(recording.GetTextOutlineCalls);
            Assert.Single(recording.DrawnPaths);
            var union = recording.DrawnPaths[0];
            Assert.NotEmpty(union.Points);

            const double tolerance = 0.5;
            Assert.All(union.Points, p =>
            {
                Assert.InRange(p.X, wordRect.Left - tolerance, wordRect.Right + tolerance);
                Assert.InRange(p.Y, wordRect.Top - tolerance, wordRect.Bottom + tolerance);
            });

            Assert.DoesNotContain(recording.Log, op => op.Kind == PaintOpKind.FillRect);
        }

        [Theory]
        [InlineData("upright")]
        [InlineData("mixed")]
        public async Task VerticalRl_UnsupportedFont_FallsBackToPlainBorderBoxRectangle(string textOrientation)
        {
            // GetTextOutline returning null for every run - a CID-keyed CFF or bitmap font - must fall
            // back the same way it does under horizontal-tb, whether the run is upright (character-by-
            // character) or rotated (mixed's default for Latin text) under a true vertical writing mode.
            var orientationStyle = textOrientation == "mixed" ? "" : $"text-orientation:{textOrientation};";
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<h1 id='box' style='margin:0;font-size:40pt;writing-mode:vertical-rl;{orientationStyle}" +
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

        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task UprightRun_BuildsOnePerCharacterOutline_TranslatedToItsOwnCell(string writingMode)
        {
            // text-orientation:upright forces every word upright (DrawWordGlyphs/IsUprightWordOrientation),
            // so "Hi" - ordinarily a rotated run under the mixed default - is unambiguously upright here.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<h1 id='box' style='margin:0;font-size:40pt;writing-mode:{writingMode};text-orientation:upright;" +
                "background-color:red;background-clip:text;color:transparent;'>Hi</h1>"));

            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            recording.GetTextOutlineOverride = (text, _, origin, _, _) => FakeOutline(recording, text, origin);

            FragmentPaintHarness.PaintBox(container, box!, recording);

            // One GetTextOutline call per character - not one for the whole word - each at its own
            // distinct baseline origin (EnumerateUprightGlyphPlacements' per-character cell), and every
            // one actually reached the final union.
            Assert.Equal(2, recording.GetTextOutlineCalls.Count);
            Assert.All(recording.GetTextOutlineCalls, c => Assert.Equal(1, c.Text.Length));
            Assert.NotEqual(recording.GetTextOutlineCalls[0].BaselineOrigin, recording.GetTextOutlineCalls[1].BaselineOrigin);

            Assert.Single(recording.DrawnPaths);
            Assert.Equal(2, recording.DrawnPaths[0].UnionedPathCount);
            Assert.DoesNotContain(recording.Log, op => op.Kind == PaintOpKind.FillRect);
        }

        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task RotatedRun_BuildsOneWholeWordOutline_TransformedIntoItsPhysicalFootprint(string writingMode)
        {
            // The mixed (default) text-orientation classifies Latin letters as rotated, not upright.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<h1 id='box' style='margin:0;font-size:40pt;writing-mode:{writingMode};" +
                "background-color:red;background-clip:text;color:transparent;'>Hi</h1>"));

            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            recording.GetTextOutlineOverride = (_, _, _, _, _) => OriginRelativeOutline(recording, 20, 10);

            FragmentPaintHarness.PaintBox(container, box!, recording);

            // One call for the whole word (unlike the upright case above), in the natural (pre-rotation)
            // frame - baseline origin's X is 0, matching DrawWordGlyphs' own sideways-branch DrawString
            // point before SidewaysRotation's PushTransform is applied.
            var call = Assert.Single(recording.GetTextOutlineCalls);
            Assert.Equal("Hi", call.Text);
            Assert.Equal(0, call.BaselineOrigin.X, 3);

            Assert.Single(recording.DrawnPaths);
            var union = recording.DrawnPaths[0];
            Assert.NotEmpty(union.Points);

            // A no-op Transform would leave the origin-relative stand-in's points near (0, 0)/negative Y
            // (see OriginRelativeOutline); a real one lands them inside the word's own physical fragment
            // rect instead - the whole point of this test (CLAUDE.md's "a paint feature needs more than
            // a parser test" convention). "Hi" sits on a descendant fragment (h1's own direct text is
            // wrapped in an anonymous inline box), not the h1's own fragment directly - the same subtree
            // BuildTextClipPath itself walks via Collect's recursion into fragment.Children.
            var wordRect = FindWordRect(FragmentPaintHarness.FragmentOf(container, box!));
            Assert.All(union.Points, p =>
            {
                Assert.InRange(p.X, wordRect.Left - 1, wordRect.Right + 1);
                Assert.InRange(p.Y, wordRect.Top - 1, wordRect.Bottom + 1);
            });
        }

        private static RRect FindWordRect(BoxFragment fragment)
        {
            var found = FindWordRectOrNull(fragment);
            Assert.True(found.HasValue, "expected a descendant fragment carrying at least one word");
            return found!.Value;
        }

        private static RRect? FindWordRectOrNull(BoxFragment fragment)
        {
            if (fragment.Words.Count > 0) return fragment.Words[0].Rect;

            foreach (var child in fragment.Children)
            {
                var found = FindWordRectOrNull(child);
                if (found is { } rect) return rect;
            }

            return null;
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
