using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A text decoration on a <c>text-overflow: ellipsis</c> line covers the kept text only: it ends where
    /// the ellipsis begins, instead of running under the ellipsis and the room after it out to the box
    /// edge. <see href="https://www.w3.org/TR/css-text-decor-3/#line-decoration">css-text-decor-3 §2</see>
    /// decorates the box's inline content, and the truncated part is no longer any of it.
    /// </summary>
    /// <remarks>
    /// The truncation is a paint-time step, so layout still lays the decoration's rectangle out over the
    /// whole untruncated line; every test asserts on the draw calls and paints the whole page, because the
    /// cut is found through the page's fragment tree.
    /// </remarks>
    public class TextOverflowDecorationTests
    {
        private const string Long = "A long link title that is truncated by the box";

        private const string Block =
            "width:120pt;overflow:hidden;white-space:nowrap;text-overflow:ellipsis;font-size:12pt";

        [Fact]
        public async Task BlockUnderline_EndsWhereTheEllipsisBegins()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Page(
                $"<div id='d' style='{Block};text-decoration:underline'>{Long}</div>"));

            var g = Paint(container);

            var ellipsis = Ellipsis(g);
            var line = Assert.Single(Lines(g));
            Assert.Equal(ellipsis.PaintPoint.X, line.X2, 1);
        }

        [Fact]
        public async Task InlineUnderline_EndsWhereTheEllipsisBegins()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Page(
                $"<div style='{Block}'><a id='a' href='#' style='text-decoration:underline'>{Long}</a></div>"));

            var g = Paint(container);

            var line = Assert.Single(Lines(g));
            Assert.Equal(Ellipsis(g).PaintPoint.X, line.X2, 1);
        }

        [Fact]
        public async Task UnderlineOnAnAncestorOfTheCuttingBox_EndsWhereTheEllipsisBegins()
        {
            // The cut is in the <span>, whose words paint after the <a> has already drawn its decoration.
            var (_, container) = await LayoutHarness.LayoutAsync(Page(
                $"<div style='{Block}'><a href='#' style='text-decoration:underline'><span>{Long}</span></a></div>"));

            var g = Paint(container);

            var line = Assert.Single(Lines(g));
            Assert.Equal(Ellipsis(g).PaintPoint.X, line.X2, 1);
        }

        [Fact]
        public async Task LineStillCoversTheKeptTextOfEachDecoratedBox_WhenTheCutIsInALaterSibling()
        {
            // "short" fits whole and keeps its underline; the plain text after it is what gets cut.
            var (root, container) = await LayoutHarness.LayoutAsync(Page(
                $"<div style='{Block}'><a id='a' href='#' style='text-decoration:underline'>short</a> {Long}</div>"));

            var g = Paint(container);

            var line = Assert.Single(Lines(g));
            var a = LayoutHarness.FindById(root, "a")!;
            Assert.Equal(a.Rectangles.Values.Single().Right, line.X2, 1);
        }

        [Fact]
        public async Task DecoratedBoxEntirelyPastTheCut_DrawsNoLine()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Page(
                $"<div style='{Block}'>{Long} <a href='#' style='text-decoration:underline'>link</a></div>"));

            var g = Paint(container);

            Assert.Empty(Lines(g));
            Assert.NotNull(Ellipsis(g));
        }

        [Fact]
        public async Task LineThrough_AlsoEndsWhereTheEllipsisBegins()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Page(
                $"<div style='{Block};text-decoration:line-through'>{Long}</div>"));

            var g = Paint(container);

            Assert.Equal(Ellipsis(g).PaintPoint.X, Assert.Single(Lines(g)).X2, 1);
        }

        [Fact]
        public async Task LineThatFits_KeepsItsWholeDecoration()
        {
            // Compared with the same markup minus text-overflow, which has nothing to cut.
            var (_, withEllipsis) = await LayoutHarness.LayoutAsync(Page(
                $"<div style='{Block};width:400pt;text-decoration:underline'>short text</div>"));
            var (_, without) = await LayoutHarness.LayoutAsync(Page(
                "<div style='width:400pt;overflow:hidden;white-space:nowrap;font-size:12pt;text-decoration:underline'>short text</div>"));

            var g = Paint(withEllipsis);

            Assert.DoesNotContain(g.Log.OfType<TestRecordingGraphics.DrawStringCall>(), s => s.Text == "…");
            var expected = Assert.Single(Lines(Paint(without)));
            var line = Assert.Single(Lines(g));
            Assert.Equal(expected.X1, line.X1, 3);
            Assert.Equal(expected.X2, line.X2, 3);
        }

        [Fact]
        public async Task Overline_EndsWhereTheEllipsisBegins()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Page(
                $"<div style='{Block};text-decoration:overline'>{Long}</div>"));

            var g = Paint(container);

            Assert.Equal(Ellipsis(g).PaintPoint.X, Assert.Single(Lines(g)).X2, 1);
        }

        [Fact]
        public async Task TextIndent_PlacesTheEllipsisAtTheDroppedWordsOwnStart()
        {
            // The dropped first word starts at the indent, not the content edge; the ellipsis goes where
            // the word would have begun.
            var (root, container) = await LayoutHarness.LayoutAsync(Page(
                "<div id='d' style='width:12pt;text-indent:10pt;overflow:hidden;white-space:nowrap;"
                + "text-overflow:ellipsis;font-size:12pt;text-decoration:underline'>WWWWWWWWWW</div>"));

            var g = Paint(container);

            Assert.Empty(Lines(g));
            var left = FragmentPaintHarness.FragmentOf(container, LayoutHarness.FindById(root, "d")!).Rect.Left;
            Assert.Equal(left + 10, Ellipsis(g).PaintPoint.X, 1);
        }

        [Fact]
        public async Task WithoutEllipsis_TheDecorationIsUntouched()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Page(
                $"<div style='width:120pt;overflow:hidden;white-space:nowrap;font-size:12pt;text-decoration:underline'>{Long}</div>"));

            var g = Paint(container);

            Assert.True(Assert.Single(Lines(g)).X2 > 120, "no ellipsis: the underline keeps running to the clip");
        }

        [Fact]
        public async Task Rtl_UnderlineStartsWhereTheEllipsisEnds()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Page(
                $"<div style='{Block};direction:rtl;text-decoration:underline'>{Long}</div>"));

            var g = Paint(container);

            var ellipsis = Ellipsis(g);
            var line = Assert.Single(Lines(g));
            var ellipsisRight = ellipsis.PaintPoint.X + ellipsis.Size.Width;
            Assert.True(line.X1 >= ellipsisRight - 1,
                $"the underline ({line.X1}) should begin at or after the ellipsis' right edge ({ellipsisRight})");
            Assert.True(line.X2 - line.X1 > 20, "the underline should still cover the kept text");
        }

        [Fact]
        public async Task TwoDecoratedSiblingsPastTheCut_TheFirstCutDecidesWhereBothEnd()
        {
            // Both spans make a plan of their own: the first is cut part-way through, the second starts past
            // the box edge and is dropped whole. The first one in tree order is the cut the word painter
            // draws (it claims the line), so it is the one the decorations must follow - not the last.
            var (_, container) = await LayoutHarness.LayoutAsync(Page(
                $"<div style='{Block}'><span style='text-decoration:underline'>{Long}</span>"
                + $"<span style='text-decoration:underline'>{Long}</span></div>"));

            var g = Paint(container);

            var ellipsisX = Ellipsis(g).PaintPoint.X;
            var lines = Lines(g);
            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.True(l.X2 <= ellipsisX + 0.5,
                $"a decoration ({l.X1}..{l.X2}) runs past where the ellipsis begins ({ellipsisX})"));
            Assert.Equal(ellipsisX, lines[0].X2, 1);
        }

        [Fact]
        public async Task DecorationOnAnAncestorBlock_EndsWhereTheEllipsisBegins()
        {
            // The outer block's propagated decoration descends into the inner block, which is the one that
            // truncates - the decorating box and its containing block are neither of them ellipsis-active.
            var (_, container) = await LayoutHarness.LayoutAsync(Page(
                $"<div style='text-decoration:underline'><div style='{Block}'>{Long}</div></div>"));

            var g = Paint(container);

            Assert.Equal(Ellipsis(g).PaintPoint.X, Assert.Single(Lines(g)).X2, 1);
        }

        [Fact]
        public async Task DecoratedBoxBeforeADroppedFirstWord_KeepsItsLine()
        {
            // The span's first word cannot keep even one character, so it is dropped whole. The ellipsis then
            // follows the last thing kept - the decorated "ab" - not the block's content edge.
            var (root, container) = await LayoutHarness.LayoutAsync(Page(
                "<div style='width:24pt;overflow:hidden;white-space:nowrap;text-overflow:ellipsis;font-size:12pt'>"
                + "<a id='a' href='#' style='text-decoration:underline'>ab</a><span>WWWWWWWWWW</span></div>"));

            var g = Paint(container);

            var a = LayoutHarness.FindById(root, "a")!;
            Assert.Equal(a.Rectangles.Values.Single().Right, Assert.Single(Lines(g)).X2, 1);
            Assert.Equal(a.Rectangles.Values.Single().Right, Ellipsis(g).PaintPoint.X, 1);
        }

        [Fact]
        public async Task NothingKept_DrawsNoLineAndTheEllipsisSitsAtTheLineStart()
        {
            // The line's very first word has no room for even one character, so nothing is kept to decorate.
            var (root, container) = await LayoutHarness.LayoutAsync(Page(
                "<div id='d' style='width:8pt;overflow:hidden;white-space:nowrap;text-overflow:ellipsis;font-size:12pt;"
                + "text-decoration:underline'>WWWWWWWWWW</div>"));

            var g = Paint(container);

            Assert.Empty(Lines(g));
            var left = FragmentPaintHarness.FragmentOf(container, LayoutHarness.FindById(root, "d")!).Rect.Left;
            Assert.Equal(left, Ellipsis(g).PaintPoint.X, 1);
        }

        [Fact]
        public async Task DoubleStyle_EndsWhereTheEllipsisBegins()
        {
            // A non-solid style is stroked segment by segment; the clamped span must bound every stroke.
            var (_, container) = await LayoutHarness.LayoutAsync(Page(
                $"<div style='{Block};text-decoration:underline double'>{Long}</div>"));

            var g = Paint(container);

            var lines = Lines(g);
            Assert.True(lines.Count >= 2, "double draws two strokes");
            var ellipsisX = Ellipsis(g).PaintPoint.X;
            Assert.All(lines, l => Assert.Equal(ellipsisX, l.X2, 1));
        }

        [Fact]
        public async Task VerticalColumn_UnderlineEndsWhereTheEllipsisBegins()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Page(
                "<div style='writing-mode:vertical-rl;text-orientation:upright;overflow:hidden;"
                + "text-overflow:ellipsis;height:60pt;width:40pt;font-size:12pt;text-decoration:underline'>"
                + "ABCDEFGHIJKLMNOPQRSTUVWXYZ</div>"));

            var g = Paint(container);

            var ellipsis = Ellipsis(g);
            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var top = System.Math.Min(line.Y1, line.Y2);
            var bottom = System.Math.Max(line.Y1, line.Y2);
            Assert.True(bottom <= ellipsis.PaintPoint.Y + 0.5,
                $"the underline ({top}..{bottom}) should end where the ellipsis begins ({ellipsis.PaintPoint.Y})");
            Assert.True(bottom - top > 5, "the underline should still cover the kept characters");
        }

        private static TestRecordingGraphics Paint(PeachPDF.Html.Core.HtmlContainerInt container)
        {
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);
            return g;
        }

        private static TestRecordingGraphics.DrawStringCall Ellipsis(TestRecordingGraphics g) =>
            g.Log.OfType<TestRecordingGraphics.DrawStringCall>().Single(s => s.Text == "…");

        private static List<TestRecordingGraphics.DrawLineCall> Lines(TestRecordingGraphics g) =>
            g.Log.OfType<TestRecordingGraphics.DrawLineCall>().OrderBy(l => l.Y1).ThenBy(l => l.X1).ToList();

        private static string Page(string body) =>
            $"<!DOCTYPE html><html><head></head><body style='margin:0'>{body}</body></html>";
    }
}
