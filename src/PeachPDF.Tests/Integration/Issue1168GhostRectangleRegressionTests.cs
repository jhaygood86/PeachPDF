using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for issue #1168: an <c>inline-block</c> whose content needs to wrap was
    /// theorized to sometimes get laid out twice — once flattened word-by-word into its parent's own
    /// line boxes (the wrong path, taken when <see cref="CssLayoutEngine"/>'s atomic-vs-flatten verdict
    /// mistakenly reports "fits on one line") and once via the genuine atomic-box path
    /// (<c>FlowAtomicBlockContentChild</c>), with the first attempt's now-abandoned
    /// <see cref="CssBox.Rectangles"/> entries never cleared — producing a visible "ghost" duplicate of
    /// the box's background/border at the wrong position.
    /// <para>
    /// Investigated after two prior fixes in this area landed: #1032 (the intrinsic max-content-width
    /// walk that <c>LaysOutAsAnAtomicBox</c> depends on undercounted an atomic inline-level box holding
    /// a block-level descendant) and #1105 (three of the four atomic inline-level displays had no
    /// preflight-and-wrap check at all before being placed). Both are exactly the kind of bug that would
    /// make the atomic-vs-flatten verdict wrong on a first pass and only self-correct later — the
    /// mechanism this issue theorized. See <c>.claude/recent-fixes/2026-09-17-atomic-inline-isolated-measurement-and-line-wrap.md</c>.
    /// </para>
    /// <para>
    /// Extensive reproduction attempts against the current codebase — the issue's own inline-`style`
    /// repro, the equivalent stylesheet-rule case, and several scenarios that force a genuine
    /// containing-block-width change across two real layout passes over the same box (a flex item whose
    /// hypothetical auto-width measurement pass differs from its final flex-resolved width, both as the
    /// item's direct child and nested inside an ordinary block; a flex-direction:column stretched item;
    /// a <c>column-fill:balance</c> retry; a widows-forced page-break rewind) — found no case where
    /// <see cref="CssBox.Rectangles"/> retains a stale entry. #1032/#1105 already made the verdict itself
    /// stable, and every real re-visit of a box in this codebase re-runs its own
    /// <see cref="CssBox.RectanglesReset"/> before re-flowing its content (see
    /// <c>CssLayoutEngine.FlowBox</c>'s <c>if (coordinates.ResumeOrdinal == 0) b.RectanglesReset()</c>),
    /// which — because that same reset fires again for every descendant the recursive re-flow visits —
    /// cleans up a flip's whole subtree, not just the box itself.
    /// </para>
    /// <para>
    /// This class keeps that finding under test rather than as a comment: if a future change to the
    /// atomic-vs-flatten dispatch (<c>LaysOutAsAnAtomicBox</c>) or to the reset/resume bookkeeping
    /// reintroduces a stale verdict or a skipped reset, <see cref="FindGhostRectangles"/> catches it here
    /// instead of only in a painted PDF.
    /// </para>
    /// </summary>
    public class Issue1168GhostRectangleRegressionTests
    {
        /// <summary>
        /// The issue's own reproduction: a wrapping inline-block whose <c>width</c> comes from an inline
        /// <c>style</c> attribute rather than a stylesheet rule.
        /// </summary>
        [Fact]
        public async Task InlineStyleDeclaredWidth_WrappingInlineBlock_PaintsExactlyOneRectangle()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>before " +
                "<span id='box' style='display:inline-block;width:120pt;padding:3pt 5pt;border:1pt solid #1d6fa5'>" +
                "This inline-block holds enough words to wrap across several lines of its own rather than " +
                "across the lines of the block around it.</span> after Agy</div>"));

            var box = FindById(root, "box")!;

            Assert.Single(box.Rectangles);
            Assert.Empty(FindGhostRectangles(root));
            // The atomic path ran: the box owns several lines of its own rather than having its content
            // flattened across the parent's lines.
            Assert.True(box.LineBoxes.Count > 1);
        }

        /// <summary>
        /// The same markup with the width declared via a stylesheet rule instead — the shape the shipped
        /// <c>atomic_inline_width</c> showcase and <c>InlineBlockDeclaredWidthGeometryTests</c> already
        /// cover, kept here so the two sources of a declared width are asserted side by side.
        /// </summary>
        [Fact]
        public async Task StylesheetDeclaredWidth_WrappingInlineBlock_PaintsExactlyOneRectangle()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<style>.wrapbox{display:inline-block;width:120pt;padding:3pt 5pt;border:1pt solid #1d6fa5}</style>" +
                "<div style='width:400pt'>before " +
                "<span id='box' class='wrapbox'>" +
                "This inline-block holds enough words to wrap across several lines of its own rather than " +
                "across the lines of the block around it.</span> after Agy</div>"));

            var box = FindById(root, "box")!;

            Assert.Single(box.Rectangles);
            Assert.Empty(FindGhostRectangles(root));
            Assert.True(box.LineBoxes.Count > 1);
        }

        /// <summary>
        /// A genuine atomic-vs-flatten verdict flip: <c>ib</c>'s flex item is first measured at its
        /// hypothetical (near-full-container) auto width, where its 30%-wide inline-block content fits
        /// on one line, and then laid out again at its much narrower final flex-resolved width, where it
        /// no longer does. Confirmed during investigation (temporary instrumentation, not kept) that the
        /// verdict genuinely flips false→true across these two passes — this is the exact mechanism the
        /// issue theorized, and it still produces no ghost.
        /// </summary>
        [Fact]
        public async Task FlexItemWidthResolutionChangesAcrossPasses_LeavesNoGhostRectangles()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;width:400pt'>" +
                "<div id='item' style='flex:1 1 auto'>" +
                "<span id='ib' style='display:inline-block;width:30%'>some longer label text</span>" +
                "</div>" +
                "<div id='itemB' style='flex:0 0 100pt;width:100pt'></div>" +
                "</div>"));

            var ib = FindById(root, "ib")!;

            Assert.Single(ib.Rectangles);
            Assert.Empty(FindGhostRectangles(root));
        }

        /// <summary>
        /// Same verdict-flip setup, but with the inline-block nested a level deeper (inside a
        /// <c>&lt;p&gt;</c>) than the flex item's direct child — the shape where an abandoned first
        /// attempt would have populated a <i>descendant's</i> rectangles rather than the flex item's own.
        /// </summary>
        [Fact]
        public async Task FlexItemWidthResolutionChangesAcrossPasses_DeeplyNested_LeavesNoGhostRectangles()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;width:400pt'>" +
                "<div id='item' style='flex:1 1 auto'><p id='p'>" +
                "<span id='ib' style='display:inline-block;width:30%'>some longer label text</span>" +
                "</p></div>" +
                "<div id='itemB' style='flex:0 0 100pt;width:100pt'></div>" +
                "</div>"));

            Assert.NotNull(FindById(root, "ib"));
            Assert.Empty(FindGhostRectangles(root));
        }

        /// <summary>
        /// A page-break rewind (widows) forces the driver to re-enter an already-laid-out paragraph
        /// holding a wrapping inline-block with a different available height/position than its first
        /// attempt.
        /// </summary>
        [Fact]
        public async Task PageBreakWidowsRewind_WrappingInlineBlock_LeavesNoGhostRectangles()
        {
            var filler = string.Concat(Enumerable.Repeat(
                "<p>Filler paragraph to consume vertical space before the page break point.</p>", 12));

            var (root, _) = await LayoutAsync(
                Wrap(
                    $"<div style='width:400pt'>{filler}" +
                    "<p style='widows:3;orphans:3'>Some leading text right before the wrapping inline-block " +
                    "so that a page break has to consider widows for this paragraph. " +
                    "<span id='ib' style='display:inline-block;width:30%'>" +
                    "this text is long enough that it must wrap onto more than one line inside its own width" +
                    "</span> And trailing text after the inline-block so the paragraph has several lines " +
                    "total, giving the widows calculation real lines to rewind across.</p></div>"),
                pageWidth: 595, pageHeight: 250, margin: 10);

            Assert.NotNull(FindById(root, "ib"));
            Assert.Empty(FindGhostRectangles(root));
        }

        /// <summary>
        /// Walks the whole subtree and reports, for every box, any <see cref="CssBox.Rectangles"/> entry
        /// keyed by a <see cref="CssLineBox"/> that its own owner no longer lists among its current
        /// <see cref="CssBox.LineBoxes"/> — a stale entry left over from an abandoned layout attempt.
        /// </summary>
        private static List<string> FindGhostRectangles(CssBox root)
        {
            var ghosts = new List<string>();

            foreach (var box in Descendants(root))
            {
                foreach (var line in box.Rectangles.Keys)
                {
                    if (!line.OwnerBox.LineBoxes.Contains(line))
                    {
                        ghosts.Add(
                            $"box(id={box.HtmlTag?.TryGetAttribute("id")}) has a Rectangles entry keyed " +
                            $"by a line its owner(id={line.OwnerBox.HtmlTag?.TryGetAttribute("id")}) no " +
                            "longer lists among its own LineBoxes");
                    }
                }
            }

            return ghosts;
        }
    }
}
