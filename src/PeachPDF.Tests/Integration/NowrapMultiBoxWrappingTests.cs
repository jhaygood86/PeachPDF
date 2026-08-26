using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for issue #841: <c>white-space: nowrap</c> correctly prevented wrapping for a
    /// single inline box's own text, but a nowrap block whose content was split across more than one
    /// sibling inline box (even a plain <c>&lt;span&gt;</c>, not just <c>&lt;b&gt;</c>) still wrapped onto
    /// a second line box. <c>CssLayoutEngine.FlowBox</c>'s <c>wrapNoWrapBox</c> mechanism - which moves an
    /// unfittable <i>nested</i> nowrap run to a fresh line as a whole unit - fired even when the
    /// <i>containing block itself</i> was nowrap, where no line break is legal anywhere in its content.
    /// </summary>
    public class NowrapMultiBoxWrappingTests
    {
        [Fact]
        public async Task NowrapBlock_ContentSplitAcrossSpan_StaysOnOneLine()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='d' style='width:160pt;white-space:nowrap;font-size:14pt'>" +
                    "short <span>spanned</span> and more text that overflows the container width</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            Assert.Single(d.LineBoxes);
        }

        [Fact]
        public async Task NowrapBlock_ContentSplitAcrossBold_StaysOnOneLine()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='d' style='width:160pt;white-space:nowrap;font-size:14pt'>" +
                    "short <b>spanned</b> and more text that overflows the container width</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            Assert.Single(d.LineBoxes);
        }

        [Fact]
        public async Task NowrapBlock_ContentSplitAcrossSpan_WithOverflowHidden_StaysOnOneLine()
        {
            // Confirms the bug is purely a wrap-boundary defect, not anything to do with overflow/clip
            // handling (the common overflow:hidden;white-space:nowrap "truncate" idiom).
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='d' style='width:160pt;white-space:nowrap;overflow:hidden;font-size:14pt'>" +
                    "short <span>spanned</span> and more text that overflows the container width</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            Assert.Single(d.LineBoxes);
        }

        [Fact]
        public async Task NestedNowrapSpan_InOtherwiseWrappingBlock_StillMovesToNextLineAsUnit()
        {
            // The legitimate scenario CssLayoutEngine.FlowBox's wrapNoWrapBox mechanism exists for: the
            // containing block wraps normally, but a *nested* nowrap span's own content must not be split
            // mid-phrase - it moves to the next line as a whole once it stops fitting. This must keep
            // working: the #841 fix only suppresses the mechanism when the containing block itself
            // forbids wrapping altogether.
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='d' style='margin:0;width:100pt;font-size:14pt'>" +
                    "aa bb <span style='white-space:nowrap'>ccccccccccccccccccccccccccccccc</span> dd ee</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            Assert.True(d.LineBoxes.Count >= 3,
                "expected the nowrap span to be pushed onto its own line, separate from the text before and after it");

            var spanWord = d.LineBoxes[1].Words[0];
            Assert.Equal(d.ClientLeft, spanWord.Left, 1);
        }
    }
}
