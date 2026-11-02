using PeachDrawing.Core;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// <c>vertical-align</c> offsets (CSS 2.1 section 10.8.1): <c>sub</c> and <c>super</c> move a box by a
    /// share of the <i>parent's</i> font size, <c>middle</c> centres it on the parent's baseline plus half its
    /// x-height, and a box raised or lowered by any of them makes the line box taller (section 10.8: the line
    /// is as tall as its boxes where they ended up).
    /// </summary>
    public class VerticalAlignOffsetTests
    {
        private const double PointsPerPx = 0.75;

        private static Rect RectOf(CssBox box) => Assert.Single(box.Rectangles).Value;

        private static async Task<(CssBox Root, CssBox Block, double Baseline)> Layout(string inner, string blockStyle = "")
        {
            var (root, _) = await LayoutAsync(Wrap(
                $"<div id='d' style='width:400pt;font:20pt serif;margin:0;padding-top:80pt;{blockStyle}'>{inner}</div>"));
            var block = FindById(root, "d")!;
            return (root, block, block.LineBoxes[0].BaselineY!.Value);
        }

        private static double LineHeight(CssBox block) => block.ActualBottom - block.Location.Y - 80;

        [Theory]
        [InlineData("sub", 4.0, 1)]
        [InlineData("super", 20.0 / 3, -1)]
        public async Task SubAndSuper_MoveByTheParentsFontSize_WhateverTheBoxLooksLike(string keyword, double share, int direction)
        {
            // The same 20pt parent: a tall 30pt-high empty box and a short 10pt one are moved identically.
            var (tall, _, baseline) = await Layout(
                $"xxxx <span id='b' style='display:inline-block;width:20pt;height:30pt;vertical-align:{keyword}'></span>");
            var (short_, _, shortBaseline) = await Layout(
                $"xxxx <span id='b' style='display:inline-block;width:20pt;height:10pt;vertical-align:{keyword}'></span>");

            var expected = direction * (share + PointsPerPx);
            Assert.Equal(baseline + expected, RectOf(FindById(tall, "b")!).Bottom, 0.01);
            Assert.Equal(shortBaseline + expected, RectOf(FindById(short_, "b")!).Bottom, 0.01);
        }

        [Fact]
        public async Task Sub_UsesTheParentsFont_NotTheSpansOwn()
        {
            var (root, _, baseline) = await Layout("xxxx <span id='s' style='font-size:10pt;vertical-align:sub'>s</span>");
            var plain = await Layout("xxxx <span id='s' style='font-size:10pt'>s</span>");

            var raised = RectOf(FindById(root, "s")!).Top - RectOf(FindById(plain.Root, "s")!).Top;
            Assert.Equal(20.0 / 5 + PointsPerPx, raised - (baseline - plain.Baseline), 0.01);
        }

        [Fact]
        public async Task Middle_CentresTheBoxOnTheBaselinePlusHalfTheParentsXHeight()
        {
            var (root, block, baseline) = await Layout(
                "xxxx <span id='b' style='display:inline-block;width:20pt;height:30pt;vertical-align:middle'></span>");
            var rect = RectOf(FindById(root, "b")!);
            var xHeight = FontMetricMeasurement.Ratio(block.ActualFont, FontMetric.Ex) * block.ActualFont.Size;

            Assert.Equal(baseline - xHeight / 2, rect.Top + rect.Height / 2, 0.01);
        }

        // --- the line grows for a raised or lowered box ---

        [Theory]
        [InlineData("vertical-align:20pt")]
        [InlineData("vertical-align:50%")]
        [InlineData("vertical-align:super")]
        public async Task ARaisedBox_MakesItsLineTaller(string align)
        {
            var (_, plain, _) = await Layout("xx<span style='font-size:40pt'>X</span>xx");
            var (_, offset, _) = await Layout($"xx<span id='s' style='font-size:40pt;{align}'>X</span>xx");

            // By how much depends on the system serif font's metrics (how much room the line already had above
            // the baseline), so only that it grew is asserted; the exact amount is ARaisedBox_GrowsTheLine...
            Assert.True(LineHeight(offset) > LineHeight(plain) + 0.5, $"{align}: {LineHeight(offset)} vs {LineHeight(plain)}");
        }

        [Theory]
        [InlineData("vertical-align:-20pt")]
        [InlineData("vertical-align:sub")]
        public async Task ALoweredBox_NeverShortensItsLine_AndStaysInsideIt(string align)
        {
            var (_, plain, _) = await Layout("xx<span style='font-size:40pt'>X</span>xx");
            var (_, offset, _) = await Layout($"xx<span id='s' style='font-size:40pt;{align}'>X</span>xx");

            Assert.True(LineHeight(offset) >= LineHeight(plain) - 0.01);
        }

        [Fact]
        public async Task ALoweredBox_ReachesFurtherBelowTheBaseline()
        {
            // A 10pt box lowered by 30pt hangs well below the 20pt strut, which a baseline-only line would not hold.
            var (_, plain, _) = await Layout("xx<span style='font-size:10pt'>X</span>xx");
            var (_, lowered, _) = await Layout("xx<span id='s' style='font-size:10pt;vertical-align:-30pt'>X</span>xx");

            Assert.True(LineHeight(lowered) > LineHeight(plain) + 10);
        }

        [Fact]
        public async Task ARaisedBox_GrowsTheLineAboveByExactlyItsRaise()
        {
            // 40pt text lowered by 0 versus raised by 20: it reaches 20pt further above the baseline.
            var (_, plain, plainBaseline) = await Layout("xx<span style='font-size:40pt'>X</span>xx");
            var (_, raised, raisedBaseline) = await Layout("xx<span style='font-size:40pt;vertical-align:20pt'>X</span>xx");

            Assert.Equal(20, raisedBaseline - plainBaseline, 0.01);
        }

        [Fact]
        public async Task AnEmptyRaisedInline_SizesTheLineLikeRaisedText()
        {
            var (_, withText, textBaseline) = await Layout("xx<span style='font-size:40pt;vertical-align:20pt'>X</span>xx");
            var (_, empty, emptyBaseline) = await Layout("xx<span style='font-size:40pt;vertical-align:20pt'></span>xx");

            Assert.Equal(textBaseline, emptyBaseline, 0.01);
            Assert.Equal(LineHeight(withText), LineHeight(empty), 0.01);
        }

        [Fact]
        public async Task ARaisedInlineBlock_MakesItsLineTallerAndKeepsTheStrutsDescent()
        {
            var (_, plain, plainBaseline) = await Layout("xx");
            var (root, raised, baseline) = await Layout(
                "xx<span id='b' style='display:inline-block;width:20pt;height:30pt;vertical-align:20pt'></span>xx");

            // The box's bottom is raised 20pt above the baseline and its top 30pt above that.
            Assert.Equal(baseline - 20, RectOf(FindById(root, "b")!).Bottom, 0.01);
            Assert.Equal(baseline - 50, RectOf(FindById(root, "b")!).Top, 0.01);

            // The line still has the strut's descent below the baseline.
            Assert.Equal(plain.ActualBottom - plainBaseline, raised.ActualBottom - baseline, 0.01);
        }

        [Fact]
        public async Task ATallEmptyInlineBlock_KeepsTheStrutsDescentBelowTheBaseline()
        {
            var (_, plain, plainBaseline) = await Layout("xx");
            var (_, tall, baseline) = await Layout("xx<span style='display:inline-block;width:20pt;height:60pt'></span>xx");

            Assert.Equal(plain.ActualBottom - plainBaseline, tall.ActualBottom - baseline, 0.01);
            Assert.Equal(60, baseline - 80 - tall.Location.Y, 0.5);
        }

        [Fact]
        public async Task NestedOffsets_Add()
        {
            var (root, _, baseline) = await Layout(
                "xx<span style='vertical-align:10pt'><span id='i' style='font-size:30pt;vertical-align:10pt'>X</span></span>xx");
            var (plainRoot, _, plainBaseline) = await Layout("xx<span><span id='i' style='font-size:30pt'>X</span></span>xx");

            // The inner box is 20pt above where it would sit with no offsets, measured from each baseline.
            var shift = (RectOf(FindById(plainRoot, "i")!).Top - plainBaseline) - (RectOf(FindById(root, "i")!).Top - baseline);
            Assert.Equal(20, shift, 0.01);
        }

        [Fact]
        public async Task TheEdgeAlignedKeywords_AddNothingToTheLine()
        {
            var (_, plain, _) = await Layout("xx<span style='font-size:20pt'>X</span>xx");
            var (_, top, _) = await Layout("xx<span style='font-size:20pt;vertical-align:top'>X</span>xx");
            var (_, bottom, _) = await Layout("xx<span style='font-size:20pt;vertical-align:bottom'>X</span>xx");

            Assert.Equal(LineHeight(plain), LineHeight(top), 0.01);
            Assert.Equal(LineHeight(plain), LineHeight(bottom), 0.01);
        }

        [Theory]
        [InlineData("text-top")]
        [InlineData("text-bottom")]
        [InlineData("middle")]
        public async Task ATextRelativeKeyword_NeverMakesItsLineShorter(string keyword)
        {
            // Moving a box off the baseline moves its extent up as far as it moves it down, and the line takes
            // each side's maximum on its own, so it can only stay as tall or grow.
            var (_, plain, _) = await Layout("xx<span style='font-size:40pt'>X</span>xx");
            var (_, aligned, _) = await Layout($"xx<span style='font-size:40pt;vertical-align:{keyword}'>X</span>xx");

            Assert.True(LineHeight(aligned) >= LineHeight(plain) - 0.01);
        }
    }
}
