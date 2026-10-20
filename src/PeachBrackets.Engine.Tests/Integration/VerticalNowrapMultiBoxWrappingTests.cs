using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for issue #844: <c>CreateVerticalLineBoxes</c> (the vertical-writing-mode
    /// counterpart of horizontal's <c>FlowBox</c>) had no counterpart to <c>overflows</c>'s <c>white-space:
    /// nowrap</c>/<c>pre</c> exclusion at all - a vertical box's own <c>white-space: nowrap</c> had zero
    /// effect on column-breaking, and a nested nowrap run split across sibling inline boxes (the vertical
    /// analog of issue #841) wrapped onto a fresh column mid-run instead of moving together as a unit.
    /// </summary>
    public class VerticalNowrapMultiBoxWrappingTests
    {
        [Fact]
        public async Task NowrapColumn_SingleBox_StaysOnOneColumn()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='el' style='margin:0;writing-mode:vertical-rl;height:40pt;white-space:nowrap;font-size:14pt'>" +
                    "a long run of unwrapped text here</div>"));

            var el = LayoutHarness.FindById(root, "el")!;
            Assert.Single(el.LineBoxes);
        }

        [Fact]
        public async Task NowrapBlock_ContentSplitAcrossSpan_StaysOnOneColumn()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='el' style='margin:0;writing-mode:vertical-rl;height:160pt;white-space:nowrap;font-size:14pt'>" +
                    "short <span>spanned</span> and more text that overflows the container height</div>"));

            var el = LayoutHarness.FindById(root, "el")!;
            Assert.Single(el.LineBoxes);
        }

        [Fact]
        public async Task NowrapBlock_ContentSplitAcrossBold_StaysOnOneColumn()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='el' style='margin:0;writing-mode:vertical-rl;height:160pt;white-space:nowrap;font-size:14pt'>" +
                    "short <b>spanned</b> and more text that overflows the container height</div>"));

            var el = LayoutHarness.FindById(root, "el")!;
            Assert.Single(el.LineBoxes);
        }

        [Fact]
        public async Task NestedNowrapSpan_InOtherwiseWrappingBlock_StillMovesToNextColumnAsUnit()
        {
            // The legitimate scenario the atomic-move mechanism exists for: the containing block wraps
            // normally, but a *nested* nowrap span's own content must not be split mid-run - it moves to
            // the next column as a whole once it stops fitting. This must keep working: the #844 fix only
            // suppresses ordinary column-breaking within a run when the containing block itself is nowrap.
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='el' style='margin:0;writing-mode:vertical-rl;height:210pt;font-size:14pt'>" +
                    "aa bb <span style='white-space:nowrap'>ccccccccccccccccccccccccccccccc</span> dd ee</div>"));

            var el = LayoutHarness.FindById(root, "el")!;
            Assert.True(el.LineBoxes.Count >= 3,
                "expected the nowrap span to be pushed onto its own column, separate from the text before and after it");

            var spanWord = el.LineBoxes[1].Words[0];
            Assert.Equal(el.ClientTop, spanWord.Top, 1);
        }
    }
}
