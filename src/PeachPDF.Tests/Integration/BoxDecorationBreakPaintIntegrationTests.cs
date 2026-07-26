using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>box-decoration-break</c> at fragmentation breaks
    /// (<see href="https://www.w3.org/TR/css-break-3/#break-decoration">css-break-3 §6.2</see>), on both of
    /// the axes it applies to: the line-box breaks of an inline box, and the page breaks of a block box.
    /// <para>
    /// <c>slice</c> — the initial value — renders the box "as though the element were rendered with no
    /// breaks present, and then sliced by the breaks afterward", so a decoration resolves against the whole
    /// unbroken box and a clip cuts it: one continuous rounded, gradient-filled shape across a wrap, no
    /// border or padding inserted at a break, no shadow at a broken edge. <c>clone</c> wraps each fragment
    /// independently: its own border on every side, its own radii, its own shadow, and a <c>no-repeat</c>
    /// background image once per fragment.
    /// </para>
    /// <para>
    /// Asserted on the ordered <see cref="TestRecordingGraphics.Log"/>, per this repo's painting-test
    /// conventions: the tokens a content-stream substring check would look for are present either way, so
    /// only the rectangle each decoration is <i>resolved against</i> distinguishes the two values.
    /// </para>
    /// </summary>
    public class BoxDecorationBreakPaintIntegrationTests
    {
        // A span forced onto three lines inside a narrow block. 10pt Arial in the test adapter measures
        // predictably, and <br> makes the wrap points exact rather than font-metric-dependent.
        private const string ThreeLineSpan =
            "<div style='width:200pt;font:10pt Arial'>" +
            "<span id='s' style='{0}'>Alpha<br>Beta<br>Gamma</span></div>";

        private static string WrappingSpan(string spanStyle) => LayoutHarness.Wrap(string.Format(ThreeLineSpan, spanStyle));

        // ── slice, line axis ───────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Slice_WrappingInline_ResolvesItsGradientAgainstTheWholeUnbrokenBox()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(WrappingSpan(
                "background:linear-gradient(to right,#f00,#00f)"));

            var span = LayoutHarness.FindById(root, "s")!;
            var rects = OrderedRectangles(span);
            Assert.Equal(3, rects.Count);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            // One fill per line, and each is resolved against the unbroken box - the box as it would be on
            // a single infinitely long line - so the gradient runs continuously across the three lines
            // instead of restarting on each.
            var total = rects.Sum(r => r.Width);
            var fills = GradientFills(g).ToList();
            Assert.Equal(3, fills.Count);

            var preceding = 0d;
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(total, fills[i].Width, 3);
                Assert.Equal(rects[i].X - preceding, fills[i].X, 3);
                preceding += rects[i].Width;
            }

            // ...and each fill is cut to the line it belongs to, so it cannot paint over the space beside it.
            AssertEachFillIsClippedTo(g, rects);
        }

        [Fact]
        public async Task Slice_WrappingInline_RoundsOnlyTheTrueStartAndEndCorners()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(WrappingSpan(
                "background:#0f0;border-radius:6pt"));

            var span = LayoutHarness.FindById(root, "s")!;
            var rects = OrderedRectangles(span);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            // A rounded background is a path, not a rect. Each line paints the unbroken box's rounded
            // outline, whose left arc sits at the first line's left and whose right arc sits at the last
            // line's right - so after the per-line clip, only line 0 shows left rounding and only line 2
            // shows right rounding. That is exactly what a browser draws for the default `slice`.
            var paths = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(p => p.Color == RColor.FromArgb(0, 255, 0)).ToList();
            Assert.Equal(3, paths.Count);

            var total = rects.Sum(r => r.Width);
            var preceding = 0d;

            for (var i = 0; i < 3; i++)
            {
                var bounds = paths[i].Bounds;
                Assert.Equal(total, bounds.Width, 1);
                Assert.Equal(rects[i].X - preceding, bounds.Left, 1);
                preceding += rects[i].Width;
            }
        }

        [Fact]
        public async Task Slice_WrappingInline_DrawsNoBorderAtABreak()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(WrappingSpan(
                "border:2pt solid #00f;padding:0 4pt"));

            var span = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            // "No border is inserted at a break": the leading edge is drawn once, on the first line, and the
            // trailing edge once, on the last - never at the two wrap points.
            var blue = RColor.FromArgb(0, 0, 255);
            var vertical = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                .Where(p => p.Color == blue && IsVerticalEdge(p.Points)).ToList();

            Assert.Equal(2, vertical.Count);
        }

        [Fact]
        public async Task Slice_WrappingInline_InsetsAnUnderlineOnlyAtTheBoxsOwnEnds()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(WrappingSpan(
                "text-decoration:underline;padding:0 8pt"));

            var span = LayoutHarness.FindById(root, "s")!;
            var rects = OrderedRectangles(span);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var underlines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>()
                .OrderBy(l => l.Y1).ToList();
            Assert.Equal(3, underlines.Count);

            // Padding is part of the box's own leading and trailing edges, so it insets the first line's
            // underline on the left and the last line's on the right - and neither at a break.
            Assert.Equal(rects[0].Left + 8, underlines[0].X1, 1);
            Assert.Equal(rects[1].Left, underlines[1].X1, 1);
            Assert.Equal(rects[2].Right - 8, underlines[2].X2, 1);
            Assert.Equal(rects[1].Right, underlines[1].X2, 1);
        }

        [Fact]
        public async Task Slice_WrappingInline_DrawsNoShadowAtABrokenEdge()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(WrappingSpan(
                "background:#fff;box-shadow:0 0 5pt #000"));

            var span = LayoutHarness.FindById(root, "s")!;
            var rects = OrderedRectangles(span);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            // A shadow belongs to the unbroken box and falls outside it, so it is cut at the break edges
            // only: the middle line's slice is closed on both sides, while the first line keeps the room the
            // shadow needs to spill into on its left.
            var clips = g.Log.OfType<TestRecordingGraphics.PushClipCall>()
                .Where(c => c.Rect.Width < double.MaxValue / 2).Select(c => c.Rect).ToList();

            Assert.Contains(clips, c => c.Left < rects[0].Left && Approximately(c.Right, rects[0].Right));
            Assert.Contains(clips, c => Approximately(c.Left, rects[1].Left) && Approximately(c.Right, rects[1].Right));
            Assert.Contains(clips, c => Approximately(c.Left, rects[2].Left) && c.Right > rects[2].Right);
        }

        [Fact]
        public async Task Slice_WrappingInline_PaintsANoRepeatImageWhereItBelongsInTheWholeBox()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(WrappingSpan(
                $"background:url({TinyPng}) no-repeat right center"));

            var span = LayoutHarness.FindById(root, "s")!;
            var rects = OrderedRectangles(span);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            // The image is positioned in the unbroken box, so `right` puts it at the end of the whole box -
            // which lands over the LAST line, the only line whose clip lets it through. Paint used to gate
            // the layer to the box's first rectangle instead, putting a `right`-positioned image on the
            // first line. Each line resolves the position against the same unbroken box seen from its own
            // offset, so exactly one placement falls inside the line that drew it (the recording adapter
            // logs a draw without applying the clip, which is how the other two are observable at all).
            Assert.Equal(3, g.DrawImageCalls.Count);

            for (var i = 0; i < 3; i++)
            {
                var placedInsideItsOwnLine =
                    g.DrawImageCalls[i].DestRect.Left >= rects[i].Left - 0.5 &&
                    g.DrawImageCalls[i].DestRect.Left <= rects[i].Right + 0.5;

                Assert.Equal(i == 2, placedInsideItsOwnLine);
            }
        }

        // ── clone, line axis ──────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Clone_WrappingInline_ResolvesEveryDecorationAgainstEachLine()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(WrappingSpan(
                "background:linear-gradient(to right,#f00,#00f);box-decoration-break:clone"));

            var span = LayoutHarness.FindById(root, "s")!;
            var rects = OrderedRectangles(span);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            // Each fragment is wrapped independently, so each line gets its own complete gradient over its
            // own box - and needs no clip, because its decoration area is already its own.
            var fills = GradientFills(g).ToList();
            Assert.Equal(3, fills.Count);

            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(rects[i].Width, fills[i].Width, 3);
                Assert.Equal(rects[i].X, fills[i].X, 3);
            }
        }

        [Fact]
        public async Task Clone_WrappingInline_DrawsEveryBorderEdgeOnEveryLine()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(WrappingSpan(
                "border:2pt solid #00f;padding:0 4pt;box-decoration-break:clone"));

            var span = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            // Two vertical edges per line, against slice's two for the whole box.
            var vertical = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                .Where(p => p.Color == RColor.FromArgb(0, 0, 255) && IsVerticalEdge(p.Points)).ToList();

            Assert.Equal(6, vertical.Count);
        }

        [Fact]
        public async Task Clone_WrappingInline_RoundsEveryLinesOwnCorners()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(WrappingSpan(
                "background:#0f0;border-radius:6pt;box-decoration-break:clone"));

            var span = LayoutHarness.FindById(root, "s")!;
            var rects = OrderedRectangles(span);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var paths = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(p => p.Color == RColor.FromArgb(0, 255, 0)).ToList();
            Assert.Equal(3, paths.Count);

            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(rects[i].Width, paths[i].Bounds.Width, 1);
                Assert.Equal(rects[i].Left, paths[i].Bounds.Left, 1);
            }
        }

        [Fact]
        public async Task Clone_WrappingInline_PaintsANoRepeatImageOncePerLine()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(WrappingSpan(
                $"background:url({TinyPng}) no-repeat right center;box-decoration-break:clone"));

            var span = LayoutHarness.FindById(root, "s")!;
            var rects = OrderedRectangles(span);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            // Each fragment carries its own background, so `right` puts a copy of the image at the end of
            // every line rather than one copy at the end of the box.
            Assert.Equal(3, g.DrawImageCalls.Count);

            var placements = g.DrawImageCalls.Select(c => c.DestRect.Left).OrderBy(x => x).ToList();
            Assert.Equal(3, placements.Distinct().Count());

            for (var i = 0; i < 3; i++)
            {
                Assert.InRange(g.DrawImageCalls[i].DestRect.Left, rects[i].Left, rects[i].Right);
            }
        }

        [Fact]
        public async Task Clone_WrappingInline_InsetsAnUnderlineOnEveryLinesOwnEnds()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(WrappingSpan(
                "text-decoration:underline;padding:0 8pt;box-decoration-break:clone"));

            var span = LayoutHarness.FindById(root, "s")!;
            var rects = OrderedRectangles(span);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var underlines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().OrderBy(l => l.Y1).ToList();
            Assert.Equal(3, underlines.Count);

            // Every fragment carries its own padding, so every underline is inset at both ends.
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(rects[i].Left + 8, underlines[i].X1, 1);
                Assert.Equal(rects[i].Right - 8, underlines[i].X2, 1);
            }
        }

        // ── the page axis ─────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Slice_BlockSpanningTwoPages_PaintsTheWholeBoxOnBothAndLetsThePageClipCut()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='tall' style='height:300pt;background:rgb(10,20,30);border:3pt solid #000'>x</div>"),
                pageHeight: 200, margin: 0);

            var tall = LayoutHarness.FindById(root, "tall")!;
            Assert.Equal(2, container.FragmentTree!.Fragmentainers.Count);

            for (var page = 0; page < 2; page++)
            {
                var g = new TestRecordingGraphics();
                FragmentPaintHarness.PaintBox(container, tall, g, page);

                // The whole box's background rectangle, on both pages - the page clip does the cutting, so
                // no border is inserted at the break.
                var fill = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawRectCall>(),
                    r => r.Color == RColor.FromArgb(10, 20, 30));

                Assert.Equal(tall.Bounds.Height, fill.Height, 1);
            }
        }

        [Fact]
        public async Task Clone_BlockSpanningTwoPages_ClosesEachFragmentAtTheBreak()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='tall' style='height:300pt;background:rgb(10,20,30);" +
                                   "border:3pt solid #000;box-decoration-break:clone'>x</div>"),
                pageHeight: 200, margin: 0);

            var tall = LayoutHarness.FindById(root, "tall")!;
            var bandHeight = container.PageBandHeightOf(0);

            var page0 = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, tall, page0, 0);

            // Each fragment is wrapped independently, so the first page's fragment is the part of the box
            // that lives on it - and it is closed off with its own bottom border at the band edge, rather
            // than running off the page.
            var fill = Assert.Single(page0.Log.OfType<TestRecordingGraphics.DrawRectCall>(),
                r => r.Color == RColor.FromArgb(10, 20, 30));

            Assert.True(fill.Height < tall.Bounds.Height,
                $"expected the fragment to be cut to its band, got the whole box ({fill.Height})");
            Assert.True(fill.Y + fill.Height <= bandHeight + 1,
                $"expected the fragment to end at the band bottom ({bandHeight}), got {fill.Y + fill.Height}");

            var horizontal = page0.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                .Where(p => !IsVerticalEdge(p.Points)).ToList();
            Assert.Contains(horizontal, p => p.Points.Max(pt => pt.Y) >= bandHeight - 1);
        }

        [Fact]
        public async Task Slice_InlineSpanningAPageBreak_DrawsNoBorderAtThePageBreakEither()
        {
            // Three lines land on page 0 and the fourth on page 1. Paint used to read "is this the first
            // line *on this page*", so it drew a leading edge again at the top of page 1 and a trailing edge
            // at the bottom of page 0 - two borders §6.2 says are not there.
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div style='width:200pt;font:10pt Arial;line-height:30pt'>" +
                                   "<span id='s' style='border:2pt solid #00f'>Alpha<br>Beta<br>Gamma<br>Delta</span></div>"),
                pageHeight: 80, margin: 0);

            var span = LayoutHarness.FindById(root, "s")!;
            Assert.True(container.FragmentTree!.Fragmentainers.Count >= 2);

            var blue = RColor.FromArgb(0, 0, 255);
            var vertical = 0;

            for (var page = 0; page < container.FragmentTree.Fragmentainers.Count; page++)
            {
                var g = new TestRecordingGraphics();
                FragmentPaintHarness.PaintBox(container, span, g, page);

                vertical += g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                    .Count(p => p.Color == blue && IsVerticalEdge(p.Points));
            }

            Assert.Equal(2, vertical);
        }

        // ── the column axis ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A column boundary is the one break with no clip behind it: two columns of a page share its band,
        /// so the border a fragment draws at the break is a border that is really there. §6.2's
        /// <c>slice</c> says it is not — the box is rendered unbroken and then cut — so however many
        /// fragments the paragraph ends up in, exactly one top edge and one bottom edge are drawn.
        /// </summary>
        [Fact]
        public async Task Slice_BlockSplitAtAColumnBoundary_DrawsNoBorderAtTheBreak()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                SplitAcrossColumns("border:2pt solid #00f"), pageHeight: 200, margin: 0);

            var fragments = FragmentCountOf(container, LayoutHarness.FindById(root, "long")!);
            Assert.True(fragments > 2, $"expected the paragraph to split across columns, got {fragments} fragments");

            Assert.Equal(2, CountBlueEdges(container, vertical: false));

            // Its own two sides are drawn once per fragment either way - they are not what a block-axis
            // break cuts.
            Assert.Equal(2 * fragments, CountBlueEdges(container, vertical: true));
        }

        /// <summary>
        /// <c>clone</c> is the value that <i>does</i> close the box at a column break, and it is only
        /// distinguishable from <c>slice</c> there now that <c>slice</c> stopped doing it too.
        /// </summary>
        [Fact]
        public async Task Clone_BlockSplitAtAColumnBoundary_ClosesEveryFragment()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                SplitAcrossColumns("border:2pt solid #00f;box-decoration-break:clone"), pageHeight: 200, margin: 0);

            var fragments = FragmentCountOf(container, LayoutHarness.FindById(root, "long")!);
            Assert.True(fragments > 2, $"expected the paragraph to split across columns, got {fragments} fragments");

            Assert.Equal(2 * fragments, CountBlueEdges(container, vertical: false));
        }

        /// <summary>
        /// The rest of the decoration follows the same rule as the border: <c>slice</c> renders the box
        /// unbroken and cuts it, so a gradient layer runs continuously through every fragment rather than
        /// restarting in each — <c>clone</c>'s behaviour under the <c>slice</c> value. On the page grid this
        /// already held, because a page-split box's fragments each report the whole box; in a column each
        /// reports only its own extent, so the unbroken box has to be put back together from them.
        /// </summary>
        [Fact]
        public async Task Slice_BlockSplitAtAColumnBoundary_ResolvesItsGradientAgainstTheWholeUnbrokenBox()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                SplitAcrossColumns("background:linear-gradient(to bottom,#f00,#00f)"), pageHeight: 200, margin: 0);

            var paragraph = LayoutHarness.FindById(root, "long")!;
            var fragments = OrderedFragmentsOf(container, paragraph);

            Assert.True(fragments.Count > 2, $"expected the paragraph to split across columns, got {fragments.Count}");

            var total = fragments.Sum(f => f.Lines[0].Rect.Height);
            var preceding = 0d;

            foreach (var (fragment, fill, clips) in GradientLayerPerFragment(container, fragments))
            {
                // One layer per fragment, each as tall as the whole box and lifted by the fragments before
                // it, so the same point of the gradient falls at the same point of the box everywhere.
                Assert.Equal(total, fill.Height, 1);
                Assert.Equal(fragment.Lines[0].Rect.Y - preceding, fill.Y, 1);

                // And it is cut to this fragment, which is what makes it a slice rather than a repaint.
                Assert.Contains(clips,
                    c => Approximately(c.Top, fragment.Lines[0].Rect.Top)
                         && Approximately(c.Bottom, fragment.Lines[0].Rect.Bottom));

                preceding += fragment.Lines[0].Rect.Height;
            }
        }

        /// <summary>
        /// <c>clone</c> at the same boundary is the contrast: each fragment is wrapped independently, so
        /// the layer is measured against the fragment and starts again in each one.
        /// </summary>
        [Fact]
        public async Task Clone_BlockSplitAtAColumnBoundary_RestartsItsGradientInEveryFragment()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                SplitAcrossColumns("background:linear-gradient(to bottom,#f00,#00f);box-decoration-break:clone"),
                pageHeight: 200, margin: 0);

            var paragraph = LayoutHarness.FindById(root, "long")!;
            var fragments = OrderedFragmentsOf(container, paragraph);

            Assert.True(fragments.Count > 2, $"expected the paragraph to split across columns, got {fragments.Count}");

            foreach (var (fragment, fill, _) in GradientLayerPerFragment(container, fragments))
            {
                Assert.Equal(fragment.Lines[0].Rect.Height, fill.Height, 1);
            }
        }

        // ── the equivalence short circuit ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Slice_WrappingInlineWithAPlainSolidBackground_PaintsPerLineWithNoClip()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(WrappingSpan("background:#0f0"));

            var span = LayoutHarness.FindById(root, "s")!;
            var rects = OrderedRectangles(span);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            // A solid colour on a square box paints the same wherever it is measured from, so slicing it
            // would only add a clip pair and a wider fill to the output for no visible difference. Painting
            // it per line is the identical result and is what keeps such a box's content stream unchanged.
            var fills = g.Log.OfType<TestRecordingGraphics.DrawRectCall>()
                .Where(r => r.Color == RColor.FromArgb(0, 255, 0)).ToList();
            Assert.Equal(3, fills.Count);

            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(rects[i].Width, fills[i].Width, 3);
                Assert.Equal(rects[i].X, fills[i].X, 3);
            }

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.PushClipCall>());
        }

        [Fact]
        public async Task Slice_WrappingInlineWithANonDefaultBackgroundClip_TakesTheUnbrokenPath()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(WrappingSpan(
                "background:#0f0;background-clip:padding-box;border:2pt solid #00f;padding:0 6pt"));

            var span = LayoutHarness.FindById(root, "s")!;
            var rects = OrderedRectangles(span);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            // background-clip insets the fill by the box's own border and padding, which belongs at the
            // box's true ends - not at every break. So this box cannot take the per-line short path.
            var fills = g.Log.OfType<TestRecordingGraphics.DrawRectCall>()
                .Where(r => r.Color == RColor.FromArgb(0, 255, 0)).ToList();

            var total = rects.Sum(r => r.Width);
            // padding-box sits inside the border, so the fill is inset by the border alone.
            Assert.All(fills, f => Assert.Equal(total - 2 * 2, f.Width, 1));
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A paragraph long enough to outlast the columns of a 200pt page, so it splits at column
        /// boundaries and at a page boundary — every block-axis break there is.
        /// </summary>
        private static string SplitAcrossColumns(string style) => LayoutHarness.Wrap(
            "<div style='columns:2;column-gap:20pt;column-fill:auto;font:10pt Arial'>" +
            $"<p id='long' style='margin:0;{style}'>" +
            string.Join(" ", Enumerable.Range(0, 300).Select(i => $"word{i}")) +
            "</p></div>");

        /// <summary>
        /// Every fragment <paramref name="box"/> produced, in fill order: page first, then column within it.
        /// </summary>
        private static List<BoxFragment> OrderedFragmentsOf(PeachPDF.Html.Core.HtmlContainerInt container, CssBox box) =>
        [
            .. container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Where(f => ReferenceEquals(f.Box, box) && f.Lines.Count > 0)
                .OrderBy(f => f.FragmentainerIndex).ThenBy(f => f.Rect.X)
        ];

        /// <summary>
        /// Pairs each of <paramref name="fragments"/> with the background layer painted for it, and with
        /// the clips in force on its page.
        /// </summary>
        /// <remarks>
        /// Painted page by page, as production does, because two column fragments of one box share a page —
        /// which is also why the layer is matched to its fragment by inline position rather than by asking
        /// the paint harness for "the box's fragment on this page".
        /// </remarks>
        private static IEnumerable<(BoxFragment Fragment, RRect Fill, List<RRect> Clips)> GradientLayerPerFragment(
            PeachPDF.Html.Core.HtmlContainerInt container, List<BoxFragment> fragments)
        {
            foreach (var page in fragments.Select(f => f.FragmentainerIndex).Distinct())
            {
                var g = new TestRecordingGraphics();
                FragmentPaintHarness.PaintPage(container, g, page);

                var clips = g.Log.OfType<TestRecordingGraphics.PushClipCall>().Select(c => c.Rect).ToList();
                var fills = GradientFills(g).OrderBy(f => f.X).ToList();
                var onThisPage = fragments.Where(f => f.FragmentainerIndex == page).ToList();

                Assert.Equal(onThisPage.Count, fills.Count);

                for (var i = 0; i < onThisPage.Count; i++)
                {
                    yield return (onThisPage[i], fills[i], clips);
                }
            }
        }

        /// <summary>How many fragments <paramref name="box"/> produced across the whole document.</summary>
        private static int FragmentCountOf(PeachPDF.Html.Core.HtmlContainerInt container, CssBox box) =>
            container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Count(f => ReferenceEquals(f.Box, box));

        /// <summary>
        /// Every blue border trapezoid the whole document paints, on one axis. Painting page by page is
        /// what the production loop does, and it is also the only way to see a fragment that shares a page
        /// with another fragment of the same box.
        /// </summary>
        private static int CountBlueEdges(PeachPDF.Html.Core.HtmlContainerInt container, bool vertical)
        {
            var blue = RColor.FromArgb(0, 0, 255);
            var count = 0;

            for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
            {
                var g = new TestRecordingGraphics();
                FragmentPaintHarness.PaintPage(container, g, page);

                count += g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                    .Count(p => p.Color == blue && IsVerticalEdge(p.Points) == vertical);
            }

            return count;
        }

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child)) yield return descendant;
            }
        }

        /// <summary>A 1×1 PNG as a data URI - enough to be a real background image layer.</summary>
        private const string TinyPng =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4/58BAAT/Af9jgNErAAAAAElFTkSuQmCC";

        /// <summary>
        /// A box's own decoration rectangles in line order, in the same document space
        /// <see cref="LayoutHarness"/> lays out in. Zero-margin fixtures put fragment-local coordinates at
        /// the same values, so these read directly against what the painter recorded.
        /// </summary>
        private static List<RRect> OrderedRectangles(CssBox box) =>
            box.Rectangles.Values.OrderBy(r => r.Y).ThenBy(r => r.X).ToList();

        /// <summary>
        /// The rectangles a full-box gradient layer was filled into. The test adapter resolves a gradient
        /// brush to its first stop's colour, so a <c>linear-gradient(to right,#f00,...)</c> layer shows up as
        /// a red fill over the layer's own clip rectangle.
        /// </summary>
        private static IEnumerable<RRect> GradientFills(TestRecordingGraphics g) =>
            g.Log.OfType<TestRecordingGraphics.DrawRectCall>()
                .Where(r => r.Color == RColor.FromArgb(255, 0, 0))
                .Select(r => new RRect(r.X, r.Y, r.Width, r.Height));

        private static void AssertEachFillIsClippedTo(TestRecordingGraphics g, List<RRect> rects)
        {
            var clips = g.Log.OfType<TestRecordingGraphics.PushClipCall>().Select(c => c.Rect).ToList();

            foreach (var rect in rects)
            {
                Assert.Contains(clips, c => Approximately(c.Left, rect.Left) && Approximately(c.Right, rect.Right));
            }
        }

        /// <summary>
        /// Whether a border trapezoid is one of the two inline-axis (left/right) edges rather than a
        /// top/bottom one - the edges §6.2 suppresses at a line break.
        /// </summary>
        private static bool IsVerticalEdge(IReadOnlyList<RPoint> points)
        {
            var width = points.Max(p => p.X) - points.Min(p => p.X);
            var height = points.Max(p => p.Y) - points.Min(p => p.Y);
            return height > width;
        }

        private static bool Approximately(double a, double b) => System.Math.Abs(a - b) < 0.5;
    }
}
