using PeachBrackets.Svg;
using PeachDrawing.Core;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Svg
{
    /// <summary>
    /// CSS animations on the elements of an inline <c>&lt;svg&gt;</c>: what a keyframe that leaves a property out starts from below a <c>&lt;use&gt;</c> or
    /// an ancestor that sets <c>font-size</c> in <c>em</c>, <c>stroke-dasharray</c> lists of different lengths, a <c>d</c> that is mixed shape by shape, and a negative
    /// keyframe value the property cannot take. The numbers are what Chrome computes for the same paused animation (the colour of the pixel, for an instance).
    /// </summary>
    public class SvgCssAnimationInstanceAndListTests
    {
        private const string FillToRed = "@keyframes k { to { fill: #f00 } } .t { animation: k 1s linear }";

        private static readonly PaintColor Purple = PaintColor.FromArgb(255, 128, 0, 128);

        private static SvgElement TargetOf(SvgDocument document, int index = 0) => Assert.IsType<SvgUseElement>(document.Children[index]).Target!;

        // ─── <use> ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task AnInstance_StartsFromTheFillOfItsUse()
        {
            // The instance inherits from the <use>, not from the ancestors of the element it instantiates: blue to red is purple, not black to red.
            var document = await SvgCssAnimationTests.BuildAsync(FillToRed,
                "<defs><rect id=\"r\" class=\"t\" width=\"20\" height=\"20\"/></defs><use href=\"#r\" fill=\"#00f\"/>", 0.5);

            Assert.Equal(Purple, TargetOf(document).Fill.PaintColor);
        }

        [Fact]
        public async Task AnInstance_StartsFromTheFillOfAnAncestorOfItsUse()
        {
            var document = await SvgCssAnimationTests.BuildAsync(FillToRed,
                "<defs><rect id=\"r\" class=\"t\" width=\"20\" height=\"20\"/></defs><g fill=\"#00f\"><use href=\"#r\"/></g>", 0.5);

            var group = Assert.IsType<SvgGroupElement>(document.Children[0]);
            Assert.Equal(Purple, Assert.IsType<SvgUseElement>(group.Children[0]).Target!.Fill.PaintColor);
        }

        [Fact]
        public async Task ADescendantOfAnInstance_StartsFromTheFillOfTheUse()
        {
            var document = await SvgCssAnimationTests.BuildAsync(FillToRed,
                "<defs><g id=\"r\"><rect class=\"t\" width=\"20\" height=\"20\"/></g></defs><use href=\"#r\" fill=\"#00f\"/>", 0.5);

            var group = Assert.IsType<SvgGroupElement>(TargetOf(document));
            Assert.Equal(Purple, group.Children[0].Fill.PaintColor);
        }

        [Fact]
        public async Task ADescendantOfAnInstance_StartsFromTheFillOfItsOwnAncestorInsideTheInstance()
        {
            // The g's own green is inherited by the rect below it, and the <use>'s blue stops at the g: green to red is olive.
            var document = await SvgCssAnimationTests.BuildAsync(FillToRed,
                "<defs><g id=\"r\" fill=\"#0f0\"><rect class=\"t\" width=\"20\" height=\"20\"/></g></defs><use href=\"#r\" fill=\"#00f\"/>", 0.5);

            var group = Assert.IsType<SvgGroupElement>(TargetOf(document));
            Assert.Equal(PaintColor.FromArgb(255, 128, 128, 0), group.Children[0].Fill.PaintColor);
        }

        [Fact]
        public async Task AnInstanceThatDeclaresItsOwnFill_StartsFromIt()
        {
            var document = await SvgCssAnimationTests.BuildAsync(FillToRed,
                "<defs><rect id=\"r\" class=\"t\" width=\"20\" height=\"20\" fill=\"#0f0\"/></defs><use href=\"#r\" fill=\"#00f\"/>", 0.5);

            Assert.Equal(PaintColor.FromArgb(255, 128, 128, 0), TargetOf(document).Fill.PaintColor);
        }

        [Fact]
        public async Task AUseOfAUse_StartsFromTheOuterFill()
        {
            var document = await SvgCssAnimationTests.BuildAsync(FillToRed,
                "<defs><rect id=\"r\" class=\"t\" width=\"20\" height=\"20\"/><use id=\"u\" href=\"#r\"/></defs><use href=\"#u\" fill=\"#00f\"/>", 0.5);

            var inner = Assert.IsType<SvgUseElement>(TargetOf(document));
            Assert.Equal(Purple, inner.Target!.Fill.PaintColor);
        }

        [Fact]
        public async Task ASymbolsContent_StartsFromTheFillOfItsUse()
        {
            var document = await SvgCssAnimationTests.BuildAsync(FillToRed,
                "<defs><symbol id=\"r\"><rect class=\"t\" width=\"20\" height=\"20\"/></symbol></defs><use href=\"#r\" fill=\"#00f\"/>", 0.5);

            var symbol = Assert.IsType<SvgSymbolElement>(TargetOf(document));
            Assert.Equal(Purple, symbol.Children[0].Fill.PaintColor);
        }

        [Fact]
        public async Task AnInheritKeyword_InAnInstance_TakesTheFillOfTheUse()
        {
            var document = await SvgCssAnimationTests.BuildAsync("@keyframes k { from { fill: #f00 } to { fill: inherit } } .t { animation: k 1s linear }",
                "<defs><rect id=\"r\" class=\"t\" width=\"20\" height=\"20\"/></defs><use href=\"#r\" fill=\"#00f\"/>", 0.5);

            Assert.Equal(Purple, TargetOf(document).Fill.PaintColor);
        }

        [Fact]
        public async Task TheElementWhereItIsDeclared_StillStartsFromItsOwnAncestors()
        {
            // The same rect drawn in place, under a green group, mixes from green; only the instance of it, below the blue <use>, mixes from blue.
            var document = await SvgCssAnimationTests.BuildAsync(FillToRed,
                "<g fill=\"#0f0\"><rect id=\"r\" class=\"t\" width=\"20\" height=\"20\"/></g><use href=\"#r\" fill=\"#00f\"/>", 0.5);

            var group = Assert.IsType<SvgGroupElement>(document.Children[0]);
            Assert.Equal(PaintColor.FromArgb(255, 128, 128, 0), group.Children[0].Fill.PaintColor);
            Assert.Equal(Purple, TargetOf(document, 1).Fill.PaintColor);
        }

        [Fact]
        public async Task AnInstance_StartsFromTheStrokeWidthOfItsUse()
        {
            // The use's 10 to 20 at the middle.
            var document = await SvgCssAnimationTests.BuildAsync("@keyframes k { to { stroke-width: 20px } } .t { animation: k 1s linear }",
                "<defs><rect id=\"r\" class=\"t\" width=\"20\" height=\"20\" stroke=\"black\"/></defs><use href=\"#r\" stroke-width=\"10\"/>", 0.5);

            Assert.Equal(15, TargetOf(document).StrokeWidth, 2);
        }

        [Fact]
        public async Task AnInstance_StartsFromTheFontSizeOfItsUse()
        {
            var document = await SvgCssAnimationTests.BuildAsync("@keyframes k { to { font-size: 40px } } .t { animation: k 1s linear }",
                "<defs><text id=\"r\" class=\"t\" x=\"1\" y=\"20\">Ab</text></defs><use href=\"#r\" font-size=\"20\"/>", 0.5);

            Assert.Equal(30, Assert.IsType<SvgTextElement>(TargetOf(document)).Font!.Size, 2);
        }

        // ─── font-size inherited in a font-relative unit ─────────────────────────

        [Theory]
        [InlineData("svg { font-size: 10px } g { font-size: 2em }", "<g><text class=\"t\" x=\"5\" y=\"30\">Ab</text></g>", 0.5, 30)]
        [InlineData("svg { font-size: 10px } g { font-size: 200% }", "<g><text class=\"t\" x=\"5\" y=\"30\">Ab</text></g>", 0.5, 30)]
        [InlineData("svg { font-size: 10px } g { font-size: 2em } g g { font-size: 1.5em }", "<g><g><text class=\"t\" x=\"5\" y=\"30\">Ab</text></g></g>", 0.5, 35)]
        [InlineData("svg { font-size: 10px } g { font-size: 2em }", "<g><text class=\"t\" x=\"5\" y=\"30\">Ab</text></g>", 0.25, 25)]
        public async Task AFontSizeInheritedInEm_IsTheComputedLengthOfItsAncestor(string css, string body, double progress, double expected)
        {
            // Chrome: g is 20px (30px for the inner g of the third row), so the text mixes that to 40px; the ancestor's 2em is not 2em of the text's own size (which would be 40px to 40px).
            var document = await SvgCssAnimationTests.BuildAsync($"{css} @keyframes k {{ to {{ font-size: 40px }} }} .t {{ animation: k 1s linear }}", body, progress);

            Assert.Equal(expected, FirstText(document).Font!.Size, 2);
        }

        [Fact]
        public async Task AnInheritKeyword_OnFontSize_TakesTheComputedLengthOfItsParent()
        {
            var document = await SvgCssAnimationTests.BuildAsync(
                "svg { font-size: 10px } g { font-size: 2em } @keyframes k { from { font-size: inherit } to { font-size: 40px } } .t { animation: k 1s linear }",
                "<g><text class=\"t\" x=\"5\" y=\"30\">Ab</text></g>", 0.5);

            Assert.Equal(30, FirstText(document).Font!.Size, 2);
        }

        private static SvgTextElement FirstText(SvgDocument document)
        {
            SvgElement element = document.Children[0];
            while (element is SvgGroupElement group) element = group.Children[0];
            return Assert.IsType<SvgTextElement>(element);
        }

        // ─── stroke-dasharray lists of different lengths ─────────────────────────

        [Theory]
        [InlineData("2 4", "10 20 30", 0.5, new[] { 6d, 12, 16, 7, 11, 17 })]            // lcm(2, 3) = 6 items
        [InlineData("4 4", "8", 0.5, new[] { 6d, 6 })]
        [InlineData("4", "20 10", 0.25, new[] { 8d, 5.5 })]
        [InlineData("2px, 4px", "10px,20px,30px", 0.5, new[] { 6d, 12, 16, 7, 11, 17 })]  // commas and units
        public async Task DashListsOfDifferentLengths_AreRepeatedToACommonLength(string from, string to, double progress, double[] expected)
        {
            var document = await SvgCssAnimationTests.BuildAsync(
                $"@keyframes k {{ from {{ stroke-dasharray: {from} }} to {{ stroke-dasharray: {to} }} }} .t {{ animation: k 1s linear }}",
                "<rect class=\"t\" width=\"10\" height=\"10\" stroke=\"black\"/>", progress);

            Assert.Equal(expected, document.Children[0].StrokeDashArray);
        }

        [Fact]
        public async Task ADashListInTheAttribute_IsWhatAKeyframeThatLeavesItOutStartsFrom()
        {
            var document = await SvgCssAnimationTests.BuildAsync("@keyframes k { to { stroke-dasharray: 10 20 30 } } .t { animation: k 1s linear }",
                "<rect class=\"t\" width=\"10\" height=\"10\" stroke=\"black\" stroke-dasharray=\"2 4\"/>", 0.5);

            Assert.Equal(new[] { 6d, 12, 16, 7, 11, 17 }, document.Children[0].StrokeDashArray);
        }

        [Fact]
        public async Task DashListsOfDifferentLengths_ThatAnEasingOvershoots_StayAboveZero()
        {
            // Chrome: cubic-bezier(.5, -1, .5, 2) is below zero at the quarter (about -0.18 along), which takes 2 to 10 to 0.54 but 2 to 30 below zero, where a dash stops at 0.
            var document = await SvgCssAnimationTests.BuildAsync(
                "@keyframes k { from { stroke-dasharray: 2 4 } to { stroke-dasharray: 10 20 30 } } .t { animation: k 1s cubic-bezier(.5, -1, .5, 2) }",
                "<rect class=\"t\" width=\"10\" height=\"10\" stroke=\"black\"/>", 0.25);

            var dashes = document.Children[0].StrokeDashArray;
            double[] expected = [0.541421, 1.08284, 0, 2.90607, 0, 0];
            Assert.Equal(expected.Length, dashes.Length);

            for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], dashes[i], 4);
        }

        // ─── d ───────────────────────────────────────────────────────────────────

        private static PathSegment[] SegmentsOf(SvgDocument document) => [.. Assert.IsType<SvgPathElement>(document.Children[0]).Segments!];

        [Theory]
        [InlineData("path('M0 0 L10 10')", "path('M10 10 L30 30')", 0.5, 5, 5, 20, 20)]
        [InlineData("path('m0 0 l10 10')", "path('m10 10 l30 30')", 0.5, 5, 5, 25, 25)]    // relative commands mix as the points they reach
        [InlineData("path('M0 0 L10 10')", "path('M10 10 l30 30')", 0.5, 5, 5, 25, 25)]   // and so does a relative one against an absolute one
        [InlineData("path('M0 0 10 10')", "path('M0 0 L30 30')", 0.5, 0, 0, 20, 20)]      // a coordinate pair after M is the L it stands for
        public async Task TwoPathsOfTheSameCommands_AreMixedNumberByNumber(string from, string to, double progress, double x0, double y0, double x1, double y1)
        {
            var document = await SvgCssAnimationTests.BuildAsync($"@keyframes k {{ from {{ d: {from} }} to {{ d: {to} }} }} .t {{ animation: k 1s linear }}",
                "<path class=\"t\" d=\"M0 0 L5 5\" stroke=\"black\"/>", progress);

            var segments = SegmentsOf(document);
            Assert.Equal(2, segments.Length);
            Assert.Equal((x0, y0, x1, y1), (segments[0].X, segments[0].Y, segments[1].X, segments[1].Y));
        }

        [Fact]
        public async Task AKeyframeThatLeavesDOut_StartsFromTheAttributesData()
        {
            var document = await SvgCssAnimationTests.BuildAsync("@keyframes k { to { d: path('M10 10 L30 30') } } .t { animation: k 1s linear }",
                "<path class=\"t\" d=\"M0 0 L10 10\" stroke=\"black\"/>", 0.5);

            var segments = SegmentsOf(document);
            Assert.Equal((5d, 5d, 20d, 20d), (segments[0].X, segments[0].Y, segments[1].X, segments[1].Y));
        }

        [Fact]
        public async Task ACubicAndAClosePath_AreMixedPointByPoint()
        {
            var document = await SvgCssAnimationTests.BuildAsync(
                "@keyframes k { from { d: path('M0 0 C10 0 20 10 30 10 Z') } to { d: path('M10 10 C20 10 30 20 40 20 Z') } } .t { animation: k 1s linear }",
                "<path class=\"t\" d=\"M0 0 L5 5\" stroke=\"black\"/>", 0.25);

            var segments = SegmentsOf(document);
            Assert.Equal(3, segments.Length);
            Assert.Equal(PathSegmentKind.MoveTo, segments[0].Kind);
            Assert.Equal((2.5, 2.5), (segments[0].X, segments[0].Y));
            Assert.Equal((12.5, 2.5, 22.5, 12.5, 32.5, 12.5), (segments[1].X1, segments[1].Y1, segments[1].X2, segments[1].Y2, segments[1].X, segments[1].Y));
            Assert.Equal(PathSegmentKind.ClosePath, segments[2].Kind);
        }

        [Theory]
        [InlineData(0.25, false, true)]
        [InlineData(0.5, true, true)]      // a flag at the half way mix is the 1
        [InlineData(0.75, true, false)]
        public async Task AnArcsFlags_AreTheOneTheyAreNearer(double progress, bool largeArc, bool sweep)
        {
            var document = await SvgCssAnimationTests.BuildAsync(
                "@keyframes k { from { d: path('M0 0 A10 10 0 0 1 20 0') } to { d: path('M0 0 A10 10 0 1 0 20 0') } } .t { animation: k 1s linear }",
                "<path class=\"t\" d=\"M0 0 L5 5\" stroke=\"black\"/>", progress);

            var arc = SegmentsOf(document)[1];
            Assert.Equal((largeArc, sweep), (arc.IsLargeArc, arc.SweepClockwise));
        }

        [Fact]
        public async Task AnArcsRadiiAndRotation_AreMixedLikeNumbers()
        {
            var document = await SvgCssAnimationTests.BuildAsync(
                "@keyframes k { from { d: path('M0 0 A10 10 0 0 1 20 0') } to { d: path('M0 0 A20 10 90 0 1 40 0') } } .t { animation: k 1s linear }",
                "<path class=\"t\" d=\"M0 0 L5 5\" stroke=\"black\"/>", 0.5);

            var arc = SegmentsOf(document)[1];
            Assert.Equal((15d, 10d, 45d, 30d), (arc.RadiusX, arc.RadiusY, arc.RotationAngle, arc.X));
        }

        [Theory]
        [InlineData("path('M0 0 H10')", "path('M0 0 L20 20')", 20, 20)]                    // an H is not an L: the pair flips (to, at the middle)
        [InlineData("path('M0 0 V10')", "path('M0 0 L0 30')", 0, 30)]
        [InlineData("path('M0 0 C1 1 2 2 3 3 S5 5 6 6')", "path('M0 0 C1 1 2 2 3 3 C4 4 8 8 12 12')", 12, 12)]  // an S is not a C
        [InlineData("path('M0 0 Q5 5 10 0')", "path('M0 0 C5 5 10 5 15 0')", 15, 0)]       // a Q is not a C
        [InlineData("none", "path('M0 0 L30 30')", 30, 30)]
        public async Task PathsOfDifferentCommands_FlipHalfWay(string from, string to, double x, double y)
        {
            var document = await SvgCssAnimationTests.BuildAsync($"@keyframes k {{ from {{ d: {from} }} to {{ d: {to} }} }} .t {{ animation: k 1s linear }}",
                "<path class=\"t\" d=\"M0 0 L5 5\" stroke=\"black\"/>", 0.5);

            var last = SegmentsOf(document)[^1];
            Assert.Equal((x, y), (last.X, last.Y));
        }

        // ─── a negative keyframe value ───────────────────────────────────────────

        [Theory]
        [InlineData("r", "<circle class=\"t\" cx=\"50\" cy=\"50\" r=\"20\"/>", 12.5)]
        [InlineData("width", "<rect class=\"t\" width=\"20\" height=\"10\"/>", 12.5)]
        public async Task ANegativeKeyframeValue_IsDroppedAsInvalid(string property, string body, double expected)
        {
            // Chrome: the keyframe is dropped, so the animation runs from 5px to the attribute's 20px; a mix of 5px and -10px would be negative.
            var document = await SvgCssAnimationTests.BuildAsync($"@keyframes k {{ from {{ {property}: 5px }} to {{ {property}: -10px }} }} .t {{ animation: k 1s linear }}", body, 0.5);

            var element = document.Children[0];
            Assert.Equal(expected, element switch { SvgCircleElement c => c.R, SvgRectElement r => r.Width, _ => double.NaN }, 2);
        }

        [Fact]
        public async Task ANegativeKeyframeValue_OnTheOnlyKeyframe_LeavesTheElementAsItIs()
        {
            var document = await SvgCssAnimationTests.BuildAsync("@keyframes k { to { r: -10px } } .t { animation: k 1s linear }",
                "<circle class=\"t\" cx=\"50\" cy=\"50\" r=\"20\"/>", 0.5);

            Assert.Equal(20, Assert.IsType<SvgCircleElement>(document.Children[0]).R, 2);
        }

        [Fact]
        public async Task ANegativeKeyframeValue_InTheMiddle_IsSkippedForTheNeighbours()
        {
            // Chrome: the 50% keyframe is dropped, so the quarter point is on the way from 10px to 30px.
            var document = await SvgCssAnimationTests.BuildAsync("@keyframes k { 0% { r: 10px } 50% { r: -5px } 100% { r: 30px } } .t { animation: k 1s linear }",
                "<circle class=\"t\" cx=\"50\" cy=\"50\" r=\"20\"/>", 0.25);

            Assert.Equal(15, Assert.IsType<SvgCircleElement>(document.Children[0]).R, 2);
        }

        [Fact]
        public async Task ANegativeStrokeWidthAndRx_AreDroppedToo()
        {
            var strokeWidth = await SvgCssAnimationTests.BuildAsync("@keyframes k { from { stroke-width: 5px } to { stroke-width: -10px } } .t { animation: k 1s linear }",
                "<rect class=\"t\" width=\"20\" height=\"10\" stroke=\"black\"/>", 0.5);
            var rx = await SvgCssAnimationTests.BuildAsync("@keyframes k { from { rx: 5px } to { rx: -10px } } .t { animation: k 1s linear }",
                "<rect class=\"t\" width=\"20\" height=\"10\" rx=\"2\"/>", 0.5);
            var fontSize = await SvgCssAnimationTests.BuildAsync("@keyframes k { from { font-size: 20px } to { font-size: -10px } } .t { animation: k 1s linear }",
                "<text class=\"t\" x=\"5\" y=\"30\" font-size=\"30\">Ab</text>", 0.5);

            Assert.Equal(3, strokeWidth.Children[0].StrokeWidth, 2);
            Assert.Equal(3.5, Assert.IsType<SvgRectElement>(rx.Children[0]).Rx, 2);
            Assert.Equal(25, Assert.IsType<SvgTextElement>(fontSize.Children[0]).Font!.Size, 2);
        }

        [Fact]
        public async Task ADashListWithANegativeItem_IsDroppedAsInvalid()
        {
            // Chrome: the to keyframe is invalid, so the animation runs from 2 4 to the attribute's 8: [5, 6].
            var document = await SvgCssAnimationTests.BuildAsync(
                "@keyframes k { from { stroke-dasharray: 2 4 } to { stroke-dasharray: 10 -20 30 } } .t { animation: k 1s linear }",
                "<rect class=\"t\" width=\"10\" height=\"10\" stroke=\"black\" stroke-dasharray=\"8\"/>", 0.5);

            Assert.Equal(new[] { 5d, 6 }, document.Children[0].StrokeDashArray);
        }
    }
}
