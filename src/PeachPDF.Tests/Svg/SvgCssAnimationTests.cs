using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF;
using PeachPDF.Svg;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// CSS animations on the elements of an inline <c>&lt;svg&gt;</c>, sampled at <c>PdfGenerateConfig.AnimationProgress</c>: the value of each animated property
    /// against Chrome (a paused animation with a negative delay, the computed style of the element).
    /// </summary>
    public class SvgCssAnimationTests
    {
        internal static async Task<SvgDocument> BuildAsync(string css, string svgBody, double? progress, string svgAttributes = "width=\"200\" height=\"100\"")
        {
            var html = $"<!DOCTYPE html><html><head><style>{css}</style></head><body><svg {svgAttributes}>{svgBody}</svg></body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { AnimationProgress = progress };
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);

            var measure = PdfGraphics.CreateMeasureContext(size);
            using var graphics = new PdfCanvas(renderContext, measure, 1.0);
            await container.PerformLayout(graphics);

            var svgBox = Assert.IsType<CssBoxSvg>(DomUtils.GetBoxByTagName(container.Root!, "svg"));
            return svgBox.Document!;
        }

        [Fact]
        public async Task AFillAnimation_IsMixedAtTheSampledPoint()
        {
            var document = await BuildAsync("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } } rect { animation: k 1s linear }",
                "<rect width=\"10\" height=\"10\"/>", 0.5);

            var rect = Assert.IsType<SvgRectElement>(document.Children[0]);
            Assert.Equal(PaintColor.FromArgb(255, 128, 0, 128), rect.Fill.PaintColor);
        }

        [Fact]
        public async Task WithoutAnimationProgress_NothingIsAnimated()
        {
            var document = await BuildAsync("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } } rect { animation: k 1s linear }",
                "<rect width=\"10\" height=\"10\" fill=\"#00ff00\"/>", null);

            Assert.Equal(PaintColor.FromArgb(255, 0, 255, 0), document.Children[0].Fill.PaintColor);
        }

        [Fact]
        public void AStandaloneSvg_IsNotAnimated()
        {
            // No container, no progress: the keyframes in its own <style> are never sampled.
            var markup = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\"><style>@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } } rect { animation: k 1s linear }</style><rect width=\"10\" height=\"10\" fill=\"#00ff00\"/></svg>";
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root), new PeachPDF.Rendering.HtmlRenderContext());

            var node = new XElementSvgSourceNode(root, root, cssData, "print");
            var document = SvgTreeBuilder.Build(node, new HtmlRenderContext());

            Assert.Equal(PaintColor.FromArgb(255, 0, 255, 0), document.Children[0].Fill.PaintColor);

            ISvgSourceNode source = node;
            Assert.False(source.IsAnimated);
            Assert.Null(source.Parent);
            Assert.Null(source.GetAnimatedValue("fill", () => "red", () => "blue", () => "black", true));
        }

        [Fact]
        public async Task AnAnimationOnAGroup_IsInheritedByItsChildrenAtTheSampledPoint()
        {
            var document = await BuildAsync("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } } g { animation: k 1s linear }",
                "<g><rect width=\"10\" height=\"10\"/><circle r=\"3\" fill=\"#00ff00\"/></g>", 0.5);

            var group = Assert.IsType<SvgGroupElement>(document.Children[0]);

            Assert.Equal(PaintColor.FromArgb(255, 128, 0, 128), group.Children[0].Fill.PaintColor);
            // The circle's own fill attribute is below the inherited animated value only where it declares nothing: it declares, so it stays.
            Assert.Equal(PaintColor.FromArgb(255, 0, 255, 0), group.Children[1].Fill.PaintColor);
        }

        [Fact]
        public async Task AnInheritKeywordDeclaredOnTheElement_IsTheValueTheAnimationMixesFrom()
        {
            // Chrome: fill inherit (the group's blue) to red, a quarter of the way in, is 64,0,191.
            var document = await BuildAsync("@keyframes k { to { fill: #ff0000 } } rect { animation: k 1s linear }",
                "<g fill=\"#0000ff\"><rect width=\"10\" height=\"10\" fill=\"inherit\"/></g>", 0.25);

            var rect = Assert.IsType<SvgGroupElement>(document.Children[0]).Children[0];
            Assert.Equal(PaintColor.FromArgb(255, 64, 0, 191), rect.Fill.PaintColor);
        }

        [Theory]
        [InlineData("initial", 128, 0)]
        [InlineData("unset", 128, 128)]
        public async Task AGlobalKeywordDeclaredInTheStyleAttribute_IsResolvedBeforeTheAnimationMixesFrom(string keyword, int red, int blue)
        {
            // fill is inherited, so unset is the group's blue; initial is black. Each mixed halfway to #ff0000.
            var document = await BuildAsync("@keyframes k { to { fill: #ff0000 } } rect { animation: k 1s linear }",
                $"<g fill=\"#0000ff\"><rect width=\"10\" height=\"10\" style=\"fill:{keyword}\"/></g>", 0.5);

            var fill = Assert.IsType<SvgGroupElement>(document.Children[0]).Children[0].Fill.PaintColor;
            Assert.InRange(fill.R, red - 1, red + 1);
            Assert.InRange(fill.B, blue - 1, blue + 1);
        }

        [Theory]
        [InlineData("<circle cx=\"20\" cy=\"20\" r=\"10\"/>", "r", 0.5, 20)]
        [InlineData("<circle cx=\"20\" cy=\"20\" r=\"10\"/>", "r", 1, 30)]
        public async Task ABareNumberGeometryAttribute_MixesWithALengthInPixels(string shape, string property, double progress, double expected)
        {
            // Chrome: r="10" to r: 30px is 15/20/25 across the run, a bare number being a length of user units.
            var document = await BuildAsync($"@keyframes k {{ to {{ {property}: 30px }} }} circle {{ animation: k 1s linear }}", shape, progress);

            Assert.Equal(expected, Assert.IsType<SvgCircleElement>(document.Children[0]).R, 2);
        }

        [Fact]
        public async Task AGeometryAnimationWithLengthsOnBothEnds_IsMixed()
        {
            var document = await BuildAsync("@keyframes k { from { x: 10px; width: 20px } to { x: 40px; width: 60px } } rect { animation: k 1s linear }",
                "<rect y=\"0\" height=\"10\"/>", 0.5);

            var rect = Assert.IsType<SvgRectElement>(document.Children[0]);
            Assert.Equal(25, rect.X, 2);
            Assert.Equal(40, rect.Width, 2);
        }

        [Fact(Timeout = 20000)]
        public async Task NestedAnimatedElementsThatInherit_AreResolvedInTimeLinearInTheirDepth()
        {
            // Each level asks its parent for the value it inherits; asked once per keyword it doubled the work per level (n = 20 took seconds, n = 40 never finished).
            const int depth = 40;
            var document = await Task.Run(() => BuildAsync("@keyframes k { 0%, 50%, 100% { fill: inherit } } g { animation: k 1s linear }",
                string.Concat(Enumerable.Repeat("<g>", depth)) + "<rect width=\"10\" height=\"10\"/>" + string.Concat(Enumerable.Repeat("</g>", depth)), 0.5), TestContext.Current.CancellationToken);

            var node = document.Children[0];
            while (node is SvgGroupElement group) node = group.Children[0];

            Assert.IsType<SvgRectElement>(node);
            Assert.Equal(PaintColor.FromArgb(255, 0, 0, 0), node.Fill.PaintColor);
        }

        [Fact]
        public async Task AnAnimationWhoseStepsAllFail_LeavesTheDeclaredValueAlone()
        {
            // A var() with no value drops every step: the element keeps what it declares. the inherited 200% would be applied a second time to the text if the raw value leaked through.
            var document = await BuildAsync("@keyframes k { from { font-size: var(--missing) } to { font-size: var(--missing) } } text { animation: k 1s linear }",
                "<g font-size=\"20\"><g font-size=\"200%\"><text>a</text></g></g>", 0.5);

            var text = Assert.IsType<SvgTextElement>(Assert.IsType<SvgGroupElement>(Assert.IsType<SvgGroupElement>(document.Children[0]).Children[0]).Children[0]);
            Assert.Equal(40, text.Font!.Size, 2);
        }

        [Theory]
        [InlineData("fill:inherit")]
        [InlineData("fill:unset")]
        public async Task AKeywordDeclaredOnAnAncestor_IsResolvedBeforeAChildMixesFromIt(string declaration)
        {
            // Chrome: the middle group's fill is the outer group's blue, so the rect's fill goes from blue to red (a quarter in: 64,0,191).
            var document = await BuildAsync("@keyframes k { to { fill: #ff0000 } } rect { animation: k 1s linear }",
                $"<g fill=\"#0000ff\"><g style=\"{declaration}\"><rect width=\"10\" height=\"10\"/></g></g>", 0.25);

            var rect = Assert.IsType<SvgGroupElement>(Assert.IsType<SvgGroupElement>(document.Children[0]).Children[0]).Children[0];
            Assert.Equal(PaintColor.FromArgb(255, 64, 0, 191), rect.Fill.PaintColor);
        }

        [Fact]
        public async Task AGeometryPropertyNothingDeclares_MixesFromZero()
        {
            // Chrome: a rect with no x and to { x: 40px } is at 10, 20, 30 a quarter, half and three quarters of the way.
            var document = await BuildAsync("@keyframes k { to { x: 40px; y: 20px; height: 30px } } rect { animation: k 1s linear }",
                "<rect width=\"10\" height=\"10\"/>", 0.5);

            var rect = Assert.IsType<SvgRectElement>(document.Children[0]);
            Assert.Equal(20, rect.X, 2);
            Assert.Equal(10, rect.Y, 2);
            Assert.Equal(20, rect.Height, 2);
        }

        [Fact]
        public async Task EllipseCentreAndRadii_AreMixed()
        {
            var document = await BuildAsync("@keyframes k { to { cx: 40px; cy: 60px; rx: 30px; ry: 50px } } ellipse { animation: k 1s linear }",
                "<ellipse cx=\"20\" cy=\"20\" rx=\"10\" ry=\"10\"/>", 0.5);

            var ellipse = Assert.IsType<SvgEllipseElement>(document.Children[0]);
            Assert.Equal(30, ellipse.Cx, 2);
            Assert.Equal(40, ellipse.Cy, 2);
            Assert.Equal(20, ellipse.Rx, 2);
            Assert.Equal(30, ellipse.Ry, 2);
        }

        [Theory]
        [InlineData(0.25, 0)]
        [InlineData(0.75, 30)]
        public async Task AnAutoLengthNothingDeclares_FlipsAtTheMidpoint(double progress, double height)
        {
            // Chrome: <rect width="10"> with to { height: 30px } shows height 0 before the midpoint (auto does not mix with a length) and 30 after.
            var document = await BuildAsync("@keyframes k { to { height: 30px } } rect { animation: k 1s linear }", "<rect width=\"10\"/>", progress);

            Assert.Equal(height, Assert.IsType<SvgRectElement>(document.Children[0]).Height, 2);
        }

        [Fact]
        public async Task DisplayNoneToInline_ShowsTheElementFromTheFirstInstantOnward()
        {
            // CSS Display 4: between none and another value the other one applies for the whole of the animation, but not at its very start.
            var css = "@keyframes k { from { display: none } to { display: inline } } rect { animation: k 1s linear }";
            var before = await BuildAsync(css, "<rect width=\"10\" height=\"10\"/>", 0);
            var after = await BuildAsync(css, "<rect width=\"10\" height=\"10\"/>", 0.25);

            Assert.Empty(before.Children);
            Assert.Single(after.Children);
        }

        [Fact]
        public async Task AStrokeDasharrayAgainstNone_FlipsAtTheMidpoint()
        {
            var css = "@keyframes k { to { stroke-dasharray: none } } rect { animation: k 1s linear }";
            const string body = "<rect width=\"10\" height=\"10\" stroke=\"black\" stroke-dasharray=\"2 4\"/>";

            var early = await BuildAsync(css, body, 0.25);
            var late = await BuildAsync(css, body, 0.75);

            Assert.Equal(new[] { 2d, 4d }, early.Children[0].StrokeDashArray);
            Assert.Empty(late.Children[0].StrokeDashArray);
        }

        [Theory]
        [InlineData("fill-opacity", 0.75)]
        [InlineData("stroke-opacity", 0.75)]
        [InlineData("stop-opacity", 0.75)]
        [InlineData("opacity", 0.75)]
        public async Task AnOpacityPercentageInAKeyframe_MixesAsTheNumberItStandsFor(string property, double expected)
        {
            // Chrome: 1 to 50% is 0.75 halfway. The percentage used to be dropped (fill-opacity, stroke-opacity) or flipped (the rest).
            var document = await BuildAsync($"@keyframes k {{ to {{ {property}: 50% }} }} rect, stop {{ animation: k 1s linear }}",
                "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"red\"/></linearGradient></defs><rect width=\"10\" height=\"10\" stroke=\"black\"/>", 0.5);

            if (property == "stop-opacity")
            {
                Assert.Equal(expected, document.Gradients["g"].Stops[0].PaintColor.A / 255.0, 2);
                return;
            }

            var rect = document.Children[0];
            Assert.Equal(expected, property switch { "fill-opacity" => rect.FillOpacity, "stroke-opacity" => rect.StrokeOpacity, _ => rect.Opacity }, 2);
        }

        [Fact]
        public async Task AnOpacityPercentageInAStylesheetRule_IsRead()
        {
            // fill-opacity and stroke-opacity took a number only, so the rule was dropped before anything animated.
            var document = await BuildAsync("rect { fill-opacity: 25%; stroke-opacity: 75% }", "<rect width=\"10\" height=\"10\" stroke=\"black\"/>", null);

            Assert.Equal(0.25, document.Children[0].FillOpacity, 2);
            Assert.Equal(0.75, document.Children[0].StrokeOpacity, 2);
        }

        [Fact]
        public async Task ATextLengthFromABareNumberAttribute_MixesWithALengthInPixels()
        {
            // Chrome: font-size="20" to 60px is 40 halfway; read as 20 against 60px it would flip, or mix as a calc() of another unit.
            var document = await BuildAsync("@keyframes k { to { font-size: 60px; letter-spacing: 10px } } text { animation: k 1s linear }",
                "<text x=\"0\" y=\"20\" font-size=\"20\" letter-spacing=\"2\">a</text>", 0.5);

            var text = Assert.IsType<SvgTextElement>(document.Children[0]);
            Assert.Equal(40, text.Font!.Size, 2);
            Assert.Equal(6, text.LetterSpacing, 2);
        }

        [Fact]
        public async Task ARadiusNothingDeclares_MixesFromZero()
        {
            var document = await BuildAsync("@keyframes k { to { r: 40px } } circle { animation: k 1s linear }", "<circle cx=\"50\" cy=\"50\"/>", 0.5);

            Assert.Equal(20, Assert.IsType<SvgCircleElement>(document.Children[0]).R, 2);
        }

        [Theory]
        [InlineData(0.25, 1)]
        [InlineData(1, 0)]
        public async Task DisplayNothingDeclares_MixesFromInline(double progress, int expectedChildren)
        {
            // The omitted 0% is display: inline, so the element shows from the first instant until the discrete step to none at the end.
            var document = await BuildAsync("@keyframes k { to { display: none } } rect { animation: k 1s linear }", "<rect width=\"10\" height=\"10\"/>", progress);

            Assert.Equal(expectedChildren, document.Children.Count);
        }

        [Theory]
        [InlineData("forwards", 255, 0, 0)]
        [InlineData("none", 0, 255, 0)]
        public async Task AZeroDurationAnimation_AppliesOnlyWithAForwardsFill(string fill, int red, int green, int blue)
        {
            var document = await BuildAsync($"@keyframes k {{ to {{ fill: #ff0000 }} }} rect {{ animation: k 0s linear {fill} }}",
                "<rect width=\"10\" height=\"10\" fill=\"#00ff00\"/>", 0.5);

            Assert.Equal(PaintColor.FromArgb(255, (byte)red, (byte)green, (byte)blue), document.Children[0].Fill.PaintColor);
        }

        [Fact]
        public async Task AStrokeDasharrayWithBareNumbers_MixesWithLengthsInPixels()
        {
            var document = await BuildAsync("@keyframes k { to { stroke-dasharray: 10px 20px } } rect { animation: k 1s linear }",
                "<rect width=\"10\" height=\"10\" stroke=\"black\" stroke-dasharray=\"2 4\"/>", 0.5);

            Assert.Equal([6d, 12d], document.Children[0].StrokeDashArray);
        }

        [Fact]
        public async Task ATextPropertyNothingDeclares_MixesFromItsInitialValue()
        {
            // Chrome: letter-spacing 0 to 10px is 5 halfway; font-size from the resting size (11px) to 41px is 26.
            var document = await BuildAsync("@keyframes k { to { letter-spacing: 10px; font-size: 41px } } text { animation: k 1s linear }",
                "<text x=\"0\" y=\"20\">a</text>", 0.5);

            var text = Assert.IsType<SvgTextElement>(document.Children[0]);
            Assert.Equal(5, text.LetterSpacing, 2);
            Assert.Equal(26, text.Font!.Size, 2);
        }
    }
}
