using PeachDrawing.Core;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// An empty <c>inline-block</c>'s baseline is its bottom margin edge (CSS 2.1 section 10.8.1), so it
    /// rests on the line's baseline with its declared height reaching above it, and an empty positioned
    /// inline is aligned by its own <c>vertical-align</c> like any other inline box, which moves the
    /// containing block it gives an absolutely positioned child. Both are measured against the line's own
    /// baseline.
    /// </summary>
    public class EmptyBoxVerticalAlignTests
    {
        private static Rect PaintedRectOf(CssBox box) => Assert.Single(box.Rectangles).Value;

        private static async Task<(CssBox Root, double Baseline)> Line(string inner)
        {
            var (root, _) = await LayoutAsync(Wrap(
                $"<div id='d' style='width:400pt;font:20pt serif;margin:0;padding-top:80pt'>{inner}</div>"));
            var line = FindById(root, "d")!.LineBoxes[0];
            return (root, line.BaselineY!.Value);
        }

        [Theory]
        [InlineData("height:30pt", 0, 30)]
        [InlineData("height:30pt;border:2pt solid", 0, 34)]
        [InlineData("height:30pt;margin:5pt 0 7pt", 7, 30)]
        [InlineData("min-height:30pt", 0, 30)]
        [InlineData("height:30pt;overflow:hidden", 0, 30)]
        [InlineData("height:30pt;overflow:hidden;margin-bottom:4pt", 4, 30)]
        public async Task AnEmptyBox_RestsItsBottomMarginEdgeOnTheBaseline(string style, double marginBottom, double height)
        {
            var (root, baseline) = await Line($"xxxx <span id='b' style='display:inline-block;width:20pt;{style}'></span> HHHH");
            var rect = PaintedRectOf(FindById(root, "b")!);

            Assert.Equal(height, rect.Height, 0.01);
            Assert.Equal(baseline, rect.Bottom + marginBottom, 0.01);
        }

        [Fact]
        public async Task TheTallerOfTwoEmptyBoxes_RisesHigherAndBothRestOnTheBaseline()
        {
            var (root, baseline) = await Line(
                "xxxx <span id='a' style='display:inline-block;width:20pt;height:9.7pt'></span>" +
                "<span id='b' style='display:inline-block;width:20pt;height:19pt'></span> HHHH");
            var a = PaintedRectOf(FindById(root, "a")!);
            var b = PaintedRectOf(FindById(root, "b")!);

            Assert.Equal(baseline, a.Bottom, 0.01);
            Assert.Equal(baseline, b.Bottom, 0.01);
            Assert.True(b.Top < a.Top);
        }

        [Fact]
        public async Task ABoxTallerThanTheLineStrut_GrowsTheLineAboveTheBaseline()
        {
            var (root, baseline) = await Line("xxxx <span id='b' style='display:inline-block;width:20pt;height:60pt'></span>");
            var line = FindById(root, "d")!.LineBoxes[0];
            var rect = PaintedRectOf(FindById(root, "b")!);

            Assert.Equal(baseline, rect.Bottom, 0.01);
            Assert.True(rect.Top >= line.FlowTop!.Value - 0.01, $"box top {rect.Top} above the line top {line.FlowTop}");
        }

        [Theory]
        [InlineData("vertical-align:8pt", -8)]
        [InlineData("vertical-align:-8pt", 8)]
        public async Task ALengthOffset_IsMeasuredFromTheBottomMarginEdge(string align, double expectedBelowBaseline)
        {
            var (root, baseline) = await Line($"xxxx <span id='b' style='display:inline-block;width:20pt;height:30pt;{align}'></span> HHHH");

            Assert.Equal(baseline + expectedBelowBaseline, PaintedRectOf(FindById(root, "b")!).Bottom, 0.01);
        }

        [Fact]
        public async Task ASuperEmptyBox_SitsAboveTheBaselineOneAndASubBelowIt()
        {
            var (root, baseline) = await Line(
                "xxxx <span id='u' style='display:inline-block;width:20pt;height:30pt;vertical-align:super'></span>" +
                " <span id='s' style='display:inline-block;width:20pt;height:30pt;vertical-align:sub'></span> HHHH");

            Assert.True(PaintedRectOf(FindById(root, "u")!).Bottom < baseline);
            Assert.True(PaintedRectOf(FindById(root, "s")!).Bottom > baseline);
        }

        [Theory]
        [InlineData("top")]
        [InlineData("text-top")]
        public async Task TheEdgeAlignedKeywords_StillAnchorTheTopOfTheBox(string keyword)
        {
            var (empty, _) = await Line($"xxxx <span id='b' style='display:inline-block;width:20pt;height:30pt;vertical-align:{keyword}'></span>");
            var (withText, _) = await Line($"xxxx <span id='b' style='display:inline-block;width:20pt;height:30pt;vertical-align:{keyword}'>t</span>");

            Assert.Equal(30, PaintedRectOf(FindById(empty, "b")!).Height, 0.01);
            Assert.Equal(PaintedRectOf(FindById(withText, "b")!).Top, PaintedRectOf(FindById(empty, "b")!).Top, 0.5);
        }

        [Fact]
        public async Task ABoxWithRealContent_IsNotChanged()
        {
            var (root, baseline) = await Line("xxxx <span id='b' style='display:inline-block;width:20pt;height:30pt'>t</span> HHHH");
            var rect = PaintedRectOf(FindById(root, "b")!);

            // Its own text sits on the line's baseline, so the box hangs below it.
            Assert.True(rect.Bottom > baseline);
        }

        // --- an empty positioned inline's own vertical-align (the containing block of its abs child) ---

        private static async Task<double> AbsoluteChildTop(string align, string extra = "")
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<p style='margin:0;padding-top:80pt;font-size:20pt'>text" +
                $"<span style='position:relative;{(align.Length > 0 ? "vertical-align:" + align + ";" : "")}{extra}'>" +
                "<span id='a' style='position:absolute;left:0;top:0;width:6pt;height:6pt'></span></span>more</p>"));
            return FindById(root, "a")!.Location.Y;
        }

        [Fact]
        public async Task ABaselineEmptyInline_IsTheBaselineReference()
        {
            Assert.Equal(await AbsoluteChildTop(""), await AbsoluteChildTop("baseline"), 0.01);
        }

        [Theory]
        [InlineData("8pt", -8)]
        [InlineData("-8pt", 8)]
        public async Task ALengthOffset_MovesTheContainingBlockOfAnAbsoluteChild(string align, double shift)
        {
            Assert.Equal(await AbsoluteChildTop("") + shift, await AbsoluteChildTop(align), 0.01);
        }

        [Fact]
        public async Task APercentageOffset_IsOfTheInlinesLineHeight()
        {
            var baseline = await AbsoluteChildTop("");
            var raised = await AbsoluteChildTop("50%");
            Assert.True(raised < baseline - 5);
        }

        [Fact]
        public async Task SuperRaisesAndSubLowersTheContainingBlock()
        {
            var baseline = await AbsoluteChildTop("");
            Assert.True(await AbsoluteChildTop("super") < baseline);
            Assert.True(await AbsoluteChildTop("sub") > baseline);
        }

        [Fact]
        public async Task ARaisedInlineWithItsOwnPadding_KeepsThePaddingEdge()
        {
            var plain = await AbsoluteChildTop("8pt");
            var padded = await AbsoluteChildTop("8pt", "padding:3pt 0;");
            Assert.Equal(plain - 3, padded, 0.01);
        }
    }
}
