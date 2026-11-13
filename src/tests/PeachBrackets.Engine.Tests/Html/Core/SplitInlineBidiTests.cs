using PeachBrackets.Html.Dom;
using PeachPDF.Rendering;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// An inline box whose content is interleaved with another box's by bidi reordering (CSS 2.1 section 9.4.3 and UAX#9): the other
    /// box's text sits between two pieces of it, the box's right edge belongs beside its own last word rather than inside the gap, and
    /// the box is drawn as two separate pieces instead of one rectangle swallowing the text between them. The markup is the one of the
    /// web-platform-tests reftest <c>CSS2/bidi-text/bidi-001</c>: an RLO opens inside the span and its PDF closes outside it, so the
    /// logical words are <c>AB</c> (level 0, in the span), <c>CD</c> (level 1, in the span), <c>EF</c> (level 1, outside), <c>GH</c> (level 0).
    /// </summary>
    public class SplitInlineBidiTests
    {
        private const string Html =
            "<p id='p' style='width:600pt;margin:0'><span id='s' style='border:3pt solid blue;padding:0 6pt;background:#f00'>AB&#x202E;CD</span>EF&#x202C;GH</p>";

        private static async Task<(CssBox Span, CssLineBox Line, List<CssRect> Visual)> LayoutAsync(string html = Html)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var p = LayoutHarness.FindById(root, "p")!;
            var span = LayoutHarness.FindById(root, "s")!;
            var line = Assert.Single(p.LineBoxes);
            var visual = line.Words.OrderBy(w => w.Left).ToList();
            return (span, line, visual);
        }

        [Fact]
        public async Task TheOtherBoxsTextSitsBetweenTheTwoPiecesOfTheSpan_InVisualOrder()
        {
            var (span, _, visual) = await LayoutAsync();

            // AB (span), then EF's mirrored text (outside), then CD's (span), then GH.
            Assert.Equal([true, false, true, false], visual.Select(w => ReferenceEquals(w.OwnerBox.ParentBox, span)).ToArray());
        }

        [Fact]
        public async Task TheSpansRightEdge_SitsBesideItsLastWord_NotBetweenTwoWords()
        {
            var (span, _, visual) = await LayoutAsync();
            var (ab, ef, cd, gh) = (visual[0], visual[1], visual[2], visual[3]);

            // No gap of the span's right border and padding (9pt) between EF and CD: they touch like any two words of one run.
            Assert.True(Math.Abs(ef.Right - cd.Left) < 4, $"EF and CD should be adjacent; EF.Right={ef.Right}, CD.Left={cd.Left}");

            // The edge follows CD instead, and GH starts after it.
            Assert.True(gh.Left >= cd.Right + 9 - 0.5, $"GH should start after the span's right padding and border; CD.Right={cd.Right}, GH.Left={gh.Left}");
            Assert.True(ab.Right <= ef.Left + 0.5);
        }

        [Fact]
        public async Task TheSpanIsRecordedAsTwoPieces_OneOwningItsLeftEdgeAndOneItsRight_NeitherCoveringTheOtherTextBetween()
        {
            var (span, line, visual) = await LayoutAsync();
            var ef = visual[1];
            var union = line.Rectangles[span];

            var pieces = Assert.IsAssignableFrom<IReadOnlyList<InlineSplitPiece>>(line.SplitPieces![span]);
            Assert.Equal(2, pieces.Count);

            Assert.True(pieces[0].HasLeftEdge && !pieces[0].HasRightEdge);
            Assert.True(!pieces[1].HasLeftEdge && pieces[1].HasRightEdge);

            // Left piece ends before EF, right piece starts after it: EF is outside both.
            Assert.True(union.X + pieces[0].End <= ef.Left + 0.5);
            Assert.True(union.X + pieces[1].Start >= ef.Right - 0.5);

            // The pieces together reach the ends of the union rectangle.
            Assert.True(Math.Abs(pieces[0].Start) < 0.5);
            Assert.True(Math.Abs(union.X + pieces[1].End - union.Right) < 0.5);
        }

        [Fact]
        public async Task ABoxWhoseWordsAreContiguous_IsNotSplit_AndAnLtrLineIsUntouched()
        {
            var (span, line, _) = await LayoutAsync("<p id='p' style='width:600pt'><span id='s' style='border:1pt solid'>one two</span> three</p>");

            Assert.Null(line.SplitPieces);
            Assert.NotNull(span);
        }

        [Fact]
        public async Task APlainRtlSpanWithPadding_KeepsItsPaddingOnItsOwnSides()
        {
            // dir=rtl, one span: its words are contiguous (no split), and the padding must stay at the span's physical left and right.
            var (span, line, visual) = await LayoutAsync("<p id='p' dir='rtl' style='width:600pt'><span id='s' style='padding:0 10pt;background:#0f0'>one two</span></p>");
            var union = line.Rectangles[span];

            Assert.Null(line.SplitPieces);
            Assert.True(visual[0].Left - union.X >= 10 - 0.5, "left padding sits left of the leftmost word");
            Assert.True(union.Right - visual[^1].Right >= 10 - 0.5, "right padding sits right of the rightmost word");
        }

        [Fact]
        public async Task TheBackground_IsPaintedAsTwoRectangles_NeitherCoveringTheOtherText()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(Html));
            var line = LayoutHarness.FindById(root, "p")!.LineBoxes[0];
            var ef = line.Words.OrderBy(w => w.Left).ToList()[1];

            using var graphics = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintPage(container, graphics, 0);

            var fills = graphics.Log.Where(op => op.Kind == PaintOpKind.FillRect && op.Bounds.Width > 1).Select(op => op.Bounds).ToList();
            var span = LayoutHarness.FindById(root, "s")!;
            var union = line.Rectangles[span];

            var inSpan = fills.Where(r => r.X >= union.X - 0.5 && r.Right <= union.Right + 0.5 && r.Height <= union.Height + 0.5).ToList();
            Assert.Equal(2, inSpan.Count);
            Assert.DoesNotContain(inSpan, r => r.X < ef.Right - 0.5 && r.Right > ef.Left + 0.5);
        }
    }
}
