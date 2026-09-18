using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see href="https://www.w3.org/TR/css-text-decor-4/#text-decoration-skip-ink-property">css-text-decor-4
    /// §2.5</see>'s <c>text-decoration-skip-ink</c>: an underline or overline interrupts itself where it
    /// would otherwise run through a glyph's ink.
    /// </summary>
    /// <remarks>
    /// Every fixture registers the bundled TrueType font and names it explicitly, so what counts as a
    /// descender is a property of a font in the repository rather than of whatever happens to be
    /// installed — and so the segment counts below mean the same thing on every machine. The ink itself
    /// is real, measured through <see cref="InkAwareRecordingGraphics"/>: the plain recorder has no font
    /// subsystem, and a test written against it would pass while the feature did nothing.
    /// </remarks>
    public class TextDecorationSkipInkTests
    {
        private const string Family = "SkipInkTestFont";
        private const string HebrewFallbackFamily = "SkipInkHebrewFallbackTestFont";
        private const string CjkFamily = "SkipInkCjkTestFont";

        [Fact]
        public async Task Underline_Auto_BreaksAroundDescenders()
        {
            // "gy" descends below the baseline and through the underline; the line has to break for each.
            var (lines, _) = await UnderlineAsync("gy", skipInk: null);

            Assert.True(lines.Count >= 2,
                $"an underline across two descenders should be drawn as several segments, got {lines.Count}");
        }

        [Fact]
        public async Task Underline_None_DrawsOneUnbrokenLineThroughDescenders()
        {
            var (lines, _) = await UnderlineAsync("gy", skipInk: "none");

            Assert.Single(lines);
        }

        [Fact]
        public async Task Underline_None_DoesNotEvenMeasureInk()
        {
            // The opt-out has to be a real short-circuit, not a subtraction of an ignored result.
            var (_, g) = await UnderlineAsync("gy", skipInk: "none");

            Assert.Equal(0, g.InkQueryCount);
        }

        [Fact]
        public async Task Underline_All_SkipsTheSameWayAuto_Does()
        {
            var (auto, _) = await UnderlineAsync("gy", skipInk: "auto");
            var (all, _) = await UnderlineAsync("gy", skipInk: "all");

            Assert.Equal(auto.Count, all.Count);
            Assert.Equal(auto.Select(l => System.Math.Round(l.X1, 3)), all.Select(l => System.Math.Round(l.X1, 3)));
        }

        [Fact]
        public async Task Underline_Auto_LeavesTextWithNoDescendersUnbroken()
        {
            // "manor" has no descender, so nothing crosses the underline and one segment is correct -
            // the feature must not break a line that never meets any ink.
            var (lines, g) = await UnderlineAsync("manor", skipInk: null);

            Assert.True(g.InkQueryCount > 0, "the painter should still have measured the ink");
            Assert.Single(lines);
        }

        [Fact]
        public async Task ThickUnderline_UsesTheRenderedBaseline()
        {
            var (root, container) = await LayoutAsync(
                $"<div style=\"width:400pt; font:19.5pt '{Family}'\">"
                + "<span id='s' style='text-decoration:underline; text-decoration-thickness:6px;"
                + " text-decoration-skip-ink:none'>"
                + "The quick brown fox</span></div>");
            var s = LayoutHarness.FindById(root, "s")!;

            using var g = new InkAwareRecordingGraphics(Adapter(container));
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(Lines(g));
            var rect = s.Rectangles.Values.Single();
            var cssPixel = PeachPDF.CSS.Length.PointsPerPx;
            var expectedGap = System.Math.Ceiling(line.Width / (2 * cssPixel)) * cssPixel;
            var expectedCenter = rect.Top + s.ActualFont.TextBaselineOffset + expectedGap + line.Width / 2;

            Assert.NotEqual(s.ActualFont.Ascent, s.ActualFont.TextBaselineOffset);
            Assert.Equal(expectedCenter, line.Y1, 3);
        }

        [Fact]
        public async Task Underline_Auto_GapsLandOnTheDescendersThemselves()
        {
            // Not just "more than one segment": the gap has to be where the descender is. The fixture is
            // one descender between two runs of non-descenders, so exactly one gap is expected, and it
            // must contain the descender's own x-range rather than sit anywhere else on the line.
            var (lines, g) = await UnderlineAsync("nnnjnnn", skipInk: null);

            Assert.Equal(2, lines.Count);

            var gapStart = lines[0].X2;
            var gapEnd = lines[1].X1;
            var lineStart = lines[0].X1;
            var lineEnd = lines[1].X2;
            var middle = (lineStart + lineEnd) / 2;

            Assert.True(gapEnd > gapStart, "the gap must be a real interval");
            Assert.True(gapStart < middle && middle < gapEnd,
                $"the gap ({gapStart}..{gapEnd}) should straddle the middle of the line ({lineStart}..{lineEnd}), where the 'j' is");
        }

        [Fact]
        public async Task Underline_ThickerStrokeWidensTheInkGapUpToTheBrowserCompatibleCap()
        {
            static System.Collections.Generic.IReadOnlyList<RInkSpan> Crossing(InkAwareRecordingGraphics.InkQuery query) =>
                [new RInkSpan(query.BaselineOrigin.X + 40, query.BaselineOrigin.X + 50)];

            const string Text = "llllllllllllllllllll";

            var (thinLines, _) = await DecorationAsync("underline", Text, skipInk: "auto",
                scriptedInk: Crossing, thickness: "1px");
            var (thickLines, _) = await DecorationAsync("underline", Text, skipInk: "auto",
                scriptedInk: Crossing, thickness: "6px");
            var (cappedLines, _) = await DecorationAsync("underline", Text, skipInk: "auto",
                scriptedInk: Crossing, thickness: "20px");

            var cssPixel = PeachPDF.CSS.Length.PointsPerPx;

            Assert.Equal(10 + 2 * cssPixel, GapWidth(thinLines), 3);
            Assert.Equal(10 + 2 * 6 * cssPixel, GapWidth(thickLines), 3);
            Assert.Equal(10 + 2 * 13 * cssPixel, GapWidth(cappedLines), 3);
        }

        [Fact]
        public async Task LineThrough_IsNeverSkipped()
        {
            // §2.5: "the line-through value is never skipped" - a strike is meant to cross the glyphs.
            var (lines, _) = await DecorationAsync("line-through", "gyp gyp gyp", skipInk: "all");

            Assert.Single(lines);
        }

        [Fact]
        public async Task Overline_MeasuresItsOwnBand_AndBreaksWhereInkCrossesIt()
        {
            // A real overline sits at the font's ascent line, above every glyph's ink, so no real font
            // can demonstrate this half of the feature - scripted ink is what makes it testable at all.
            // What is asserted is the wiring: the overline asks about a band at its own height, and the
            // crossing it gets back actually cuts it.
            var (lines, g) = await DecorationAsync("overline", "lllllll", skipInk: "all",
                scriptedInk: q => [new RInkSpan(q.BaselineOrigin.X + 12, q.BaselineOrigin.X + 18)]);

            var query = Assert.Single(g.InkQueries);
            var overlineY = lines[0].Y1;

            Assert.True(System.Math.Abs(query.BandCenter - overlineY) < 1,
                $"the band asked about ({query.BandCenter}) should be centred on the overline ({overlineY})");
            Assert.Equal(2, lines.Count);
            Assert.True(lines[0].X2 <= query.BaselineOrigin.X + 12 && lines[1].X1 >= query.BaselineOrigin.X + 18,
                "the scripted crossing should have been cut out of the line");
        }

        [Fact]
        public async Task UnderlineOverline_EachMeasuresItsOwnBand()
        {
            // The two lines cross different parts of the same glyphs, so each must be measured against
            // its own band - one shared subtraction would give both the same gaps.
            var (lines, g) = await DecorationAsync("underline overline", "lllllll", skipInk: "all",
                scriptedInk: q => [new RInkSpan(q.BaselineOrigin.X + 12, q.BaselineOrigin.X + 18)]);

            Assert.Equal(2, g.InkQueries.Count);
            Assert.NotEqual(
                System.Math.Round(g.InkQueries[0].BandCenter, 3),
                System.Math.Round(g.InkQueries[1].BandCenter, 3));

            // Each band's centre is one of the two lines actually drawn.
            var ys = lines.Select(l => System.Math.Round(l.Y1, 3)).Distinct().OrderBy(y => y).ToList();
            var centres = g.InkQueries.Select(q => System.Math.Round(q.BandCenter, 3)).OrderBy(y => y).ToList();

            Assert.Equal(ys, centres);
        }

        [Fact]
        public async Task Underline_WithUndecodableInk_DrawsOneUnbrokenLine()
        {
            // A CFF/OTF-outline font has no decodable glyf outlines, so GetInkCrossings reports null and
            // nothing is skipped. auto is UA discretion, so that is conformant; it must not throw or
            // produce a degenerate line either way.
            var (lines, g) = await DecorationAsync("underline", "gy", skipInk: "all", scriptedInk: _ => null);

            Assert.True(g.InkQueryCount > 0);
            Assert.Single(lines);
        }

        [Fact]
        public async Task SkipInk_Inherits()
        {
            // The property is inherited (§2.5), so an opt-out on an ancestor reaches the decorated text.
            var (lines, _) = await UnderlineAsync("gy", skipInk: null, ancestorStyle: "text-decoration-skip-ink:none");

            Assert.Single(lines);
        }

        [Fact]
        public async Task BlockPropagatedUnderline_SkipsInkToo()
        {
            // The propagated path (a block container's decoration over its inline content) has to skip
            // the same way the ordinary inline path does.
            var (root, container) = await LayoutAsync(
                $"<div id='d' style=\"width:400pt; font:20pt '{Family}'; text-decoration:underline\">gy</div>");
            var d = LayoutHarness.FindById(root, "d")!;

            using var g = new InkAwareRecordingGraphics(Adapter(container));
            FragmentPaintHarness.PaintBox(container, d, g);

            Assert.True(Lines(g).Count >= 2, "a block's propagated underline should break around descenders");
        }

        [Fact]
        public async Task SkipInk_DoesNotDisturbTheAtomicInlineExclusion()
        {
            // The two subtractions compose: the gap around the inline-block survives, and the descender
            // in the trailing text still gets its own.
            var (root, container) = await LayoutAsync(
                $"<div id='d' style=\"width:400pt; font:20pt '{Family}'; text-decoration:underline\">nn "
                + "<span id='a' style='display:inline-block; width:60pt'><div>x</div></span> gy</div>");
            var d = LayoutHarness.FindById(root, "d")!;
            var a = LayoutHarness.FindById(root, "a")!;

            using var g = new InkAwareRecordingGraphics(Adapter(container));
            FragmentPaintHarness.PaintBox(container, d, g);

            var lines = Lines(g);
            var atomicLeft = a.Rectangles.Values.Single().Left;
            var atomicRight = a.Rectangles.Values.Single().Right;

            Assert.True(lines.Count >= 3,
                $"one gap for the atomic inline plus at least one for the descenders, got {lines.Count}");
            Assert.DoesNotContain(lines, l => l.X1 < atomicRight - 0.01 && l.X2 > atomicLeft + 0.01);
        }

        [Fact]
        public async Task Underline_PerCodepointFontFallback_StillMeasuresInk()
        {
            // "SkipInkTestFont" (Source Sans 3) is Latin-only; the trailing Hebrew run has no coverage
            // in it and is resolved per-codepoint to the fallback family instead (CssBox.
            // EmitPerCodepointFragments/ActualFontForCodepoint, at layout time). AddInkExclusions
            // already resolves each word's own font via CssBox.ResolveWordFont before calling
            // GetInkCrossings - the same resolution DrawWordGlyphs paints with - so the fallback-
            // painted run should already get its ink measured against its own (correctly resolved)
            // font, not silently skipped. This is the reproduction #1074's "per-codepoint font
            // fallback" half calls for: if it passes, that gap is closed by #1122's CFF fix alone and
            // no separate adapter-boundary plumbing is needed.
            const string hebrewFinalNun = "ן"; // Final Nun - descends below the baseline
            var (root, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head></head><body style='margin:0'>"
                + $"<div style=\"width:400pt; font:20pt '{Family}', '{HebrewFallbackFamily}'\">"
                + $"<span id='s' style='text-decoration:underline'>ab{hebrewFinalNun}</span></div>"
                + "</body></html>",
                configureAdapter: async adapter =>
                {
                    await BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, Family);
                    await BundledFonts.RegisterFont(adapter, BundledFonts.Hebrew, HebrewFallbackFamily);
                });
            var s = LayoutHarness.FindById(root, "s")!;

            using var g = new InkAwareRecordingGraphics(Adapter(container));
            FragmentPaintHarness.PaintBox(container, s, g);

            Assert.True(g.InkQueryCount > 0, "the painter should have measured ink at all");
            Assert.Contains(g.InkQueries, q => q.Text.Contains(hebrewFinalNun));
        }

        [Fact]
        public async Task Underline_CjkScriptUnderAuto_NeverMeasuresInk()
        {
            // css-text-decor-4 §2.10.5: under 'auto' (UA discretion) a UA "should consider the script
            // of the text" and "should refrain" from ink-skipping CJK-script text - unlike ordinary
            // scripts, where 'auto' skips (see Underline_Auto_BreaksAroundDescenders above).
            var (root, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head></head><body style='margin:0'>"
                + $"<div style=\"width:400pt; font:20pt '{CjkFamily}'\">"
                + "<span id='s' style='text-decoration:underline'>你好</span></div></body></html>",
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.Cjk, CjkFamily));
            var s = LayoutHarness.FindById(root, "s")!;

            using var g = new InkAwareRecordingGraphics(Adapter(container));
            FragmentPaintHarness.PaintBox(container, s, g);

            Assert.Equal(0, g.InkQueryCount);
            Assert.Single(Lines(g));
        }

        [Fact]
        public async Task Underline_CjkScriptUnderAll_StillMeasuresInk()
        {
            // 'all' ("must interrupt") has no script-based carve-out - only 'auto' does.
            var (root, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head></head><body style='margin:0'>"
                + $"<div style=\"width:400pt; font:20pt '{CjkFamily}'\">"
                + "<span id='s' style='text-decoration:underline; text-decoration-skip-ink:all'>你好</span></div></body></html>",
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.Cjk, CjkFamily));
            var s = LayoutHarness.FindById(root, "s")!;

            using var g = new InkAwareRecordingGraphics(Adapter(container));
            FragmentPaintHarness.PaintBox(container, s, g);

            Assert.True(g.InkQueryCount > 0, "'all' must interrupt regardless of script");
        }

        [Fact]
        public async Task VerticalWritingMode_UprightRun_StillNeverMeasuresInk()
        {
            // An upright run (issue #1145's honestly-partial scope) stacks each character down the
            // column with no single natural horizontal layout to reduce to (see
            // EnumerateUprightGlyphPlacements' own remarks) - so, unlike a rotated run below, it still
            // has nothing GetInkCrossings can measure against, and this half of the accepted gap
            // (.claude/accepted-gaps/text-decoration-skip-ink-and-atomic-inline-exclusion-are-horizontal-only.md)
            // remains exactly as before. text-orientation:upright forces this regardless of script.
            var (root, container) = await LayoutAsync(
                $"<div id='d' style=\"writing-mode:vertical-rl; text-orientation:upright; height:300pt; "
                + $"font:20pt '{Family}'; text-decoration:underline\">gy</div>");
            var d = LayoutHarness.FindById(root, "d")!;

            using var g = new InkAwareRecordingGraphics(Adapter(container));
            FragmentPaintHarness.PaintBox(container, d, g);

            Assert.Equal(0, g.InkQueryCount);
            Assert.NotEmpty(Lines(g));
        }

        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task VerticalWritingMode_RotatedRun_MeasuresInkInTheNaturalPreRotationFrame_AndBreaksTheLine(string writingMode)
        {
            // Issue #1145: a rotated (sideways) run is one ordinary horizontal glyph run reoriented as a
            // whole (DrawWordGlyphs' sideways branch), so AddInkExclusions can measure it once it maps
            // the band into that run's own pre-rotation frame - the mixed (default) text-orientation
            // classifies Latin "gy" as rotated, not upright, so this is the same fixture the old
            // "never measures ink" test used before this issue.
            //
            // Scripted rather than real ink, unlike the horizontal Underline_Auto_BreaksAroundDescenders
            // above: a true vertical mode's underline position is only a rect-relative approximation (no
            // real vertical baseline exists to measure from - see ResolveUnderlineCross's own remarks),
            // so unlike the horizontal case's real-baseline-derived position, it does not reliably land
            // on a real font's actual descender ink. Scripted ink (this file's own
            // Overline_MeasuresItsOwnBand_AndBreaksWhereInkCrossesIt precedent, for the analogous reason)
            // is what makes the wiring itself testable independent of that approximation.
            var (root, container) = await LayoutAsync(
                $"<div id='d' style=\"writing-mode:{writingMode}; height:300pt; font:20pt '{Family}'; "
                + "text-decoration:underline; text-decoration-skip-ink:all\">gy</div>");
            var d = LayoutHarness.FindById(root, "d")!;

            using var g = new InkAwareRecordingGraphics(Adapter(container))
            {
                ScriptedInk = q => [new RInkSpan(q.BaselineOrigin.X + 5, q.BaselineOrigin.X + 15)]
            };
            FragmentPaintHarness.PaintBox(container, d, g);

            Assert.True(g.InkQueryCount > 0, "a rotated vertical run should now have its ink measured");
            Assert.True(Lines(g).Count >= 2,
                $"an underline crossing scripted ink should be drawn as several segments, got {Lines(g).Count}");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static PdfSharpAdapter Adapter(PeachPDF.Html.Core.HtmlContainerInt container) =>
            (PdfSharpAdapter)container.Adapter;

        private static System.Collections.Generic.List<TestRecordingGraphics.DrawLineCall> Lines(TestRecordingGraphics g) =>
            g.Log.OfType<TestRecordingGraphics.DrawLineCall>().OrderBy(l => l.Y1).ThenBy(l => l.X1).ToList();

        private static double GapWidth(System.Collections.Generic.IReadOnlyList<TestRecordingGraphics.DrawLineCall> lines)
        {
            Assert.Equal(2, lines.Count);
            return lines[1].X1 - lines[0].X2;
        }

        private static Task<(System.Collections.Generic.List<TestRecordingGraphics.DrawLineCall> Lines, InkAwareRecordingGraphics Graphics)>
            UnderlineAsync(string text, string? skipInk, string? ancestorStyle = null) =>
            DecorationAsync("underline", text, skipInk, ancestorStyle);

        private static async Task<(System.Collections.Generic.List<TestRecordingGraphics.DrawLineCall> Lines, InkAwareRecordingGraphics Graphics)>
            DecorationAsync(string decoration, string text, string? skipInk, string? ancestorStyle = null,
                System.Func<InkAwareRecordingGraphics.InkQuery, System.Collections.Generic.IReadOnlyList<RInkSpan>?>? scriptedInk = null,
                string? thickness = null)
        {
            var skip = skipInk is null ? "" : $"; text-decoration-skip-ink:{skipInk}";
            var resolvedThickness = thickness is null ? "" : $"; text-decoration-thickness:{thickness}";
            var wrapper = ancestorStyle is null ? "" : $" style='{ancestorStyle}'";

            var (root, container) = await LayoutAsync(
                $"<div{wrapper} style=\"width:400pt; font:20pt '{Family}'\">"
                + $"<span id='s' style=\"text-decoration:{decoration}{skip}{resolvedThickness}\">{text}</span></div>");
            var s = LayoutHarness.FindById(root, "s")!;

            using var g = new InkAwareRecordingGraphics(Adapter(container)) { ScriptedInk = scriptedInk };
            FragmentPaintHarness.PaintBox(container, s, g);

            // Disposed here rather than by the caller. Everything a test reads afterwards - the recorded
            // draw calls and ink queries - is plain recorded data that outlives disposal, while what
            // Dispose releases (the delegate GraphicsAdapter and its measure context) is only needed
            // while painting. Handing back an undisposed instance is what leaked one per test.
            return (Lines(g), g);
        }

        private static Task<(PeachPDF.Html.Core.Dom.CssBox Root, PeachPDF.Html.Core.HtmlContainerInt Container)> LayoutAsync(string body) =>
            LayoutHarness.LayoutAsync(
                $"<!DOCTYPE html><html><head></head><body style='margin:0'>{body}</body></html>",
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, Family));
    }
}
