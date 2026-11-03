using PeachBrackets.Svg;
using PeachDrawing.Core;
using PeachPDF.Rendering;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PeachBrackets.Engine.Tests.Svg
{
    /// <summary>
    /// The font size an inline <c>&lt;svg&gt;</c> starts from: the one it inherits from its HTML parent (CSS Fonts 4 section 2.5), and the root element's for <c>rem</c>, not the svg's own.
    /// The numbers are what headless Chrome computes for the same documents.
    /// </summary>
    public class SvgInheritedFontSizeTests
    {
        private static async Task<SvgDocument> BuildAsync(string css, string body, double? progress = null) =>
            await SvgCssAnimationTests.BuildAsync(css, body, progress);

        private static double FontSizeOf(SvgDocument document) =>
            Assert.IsType<SvgTextElement>(Descend(document.Children[0])).Font!.Size;

        private static SvgElement Descend(SvgElement element) => element is SvgGroupElement { Children.Count: > 0 } group ? Descend(group.Children[0]) : element;

        private static string TextWith(string attributes = "") => $"<text class=\"t\" x=\"5\" y=\"60\"{(attributes.Length == 0 ? "" : " " + attributes)}>Ab</text>";

        [Fact]
        public async Task TextThatDeclaresNoSize_IsTheSizeOfItsHtmlParent() =>
            Assert.Equal(12, FontSizeOf(await BuildAsync("body { font-size: 12px }", TextWith())), 2);

        [Theory]
        [InlineData("2em", 24)]
        [InlineData("150%", 18)]
        [InlineData("1.5em", 18)]
        public async Task ARelativeSizeOnTheText_IsRelativeToTheHtmlParent(string size, double expected) =>
            Assert.Equal(expected, FontSizeOf(await BuildAsync("body { font-size: 12px }", TextWith($"font-size=\"{size}\""))), 2);

        [Fact]
        public async Task ARelativeSizeOnAGroup_IsRelativeToTheHtmlParentAndInheritedByTheText() =>
            Assert.Equal(24, FontSizeOf(await BuildAsync("body { font-size: 12px }", $"<g font-size=\"2em\">{TextWith()}</g>")), 2);

        [Fact]
        public async Task ASizeTheSvgDeclaresItself_IsRelativeToTheHtmlParent() =>
            Assert.Equal(24, FontSizeOf(await BuildAsync("body { font-size: 12px } svg { font-size: 2em }", TextWith())), 2);

        [Fact]
        public async Task ASizeTheSvgDeclaresInAbsoluteUnits_WinsOverTheHtmlParent() =>
            Assert.Equal(10, FontSizeOf(await BuildAsync("body { font-size: 12px } svg { font-size: 10px }", TextWith())), 2);

        [Theory]
        [InlineData("2em", 24)]
        [InlineData("1.5em", 18)]
        public async Task AnEmLengthOfAnElementThatDeclaresNoSize_IsOfTheHtmlParent(string radius, double expected)
        {
            var document = await BuildAsync("body { font-size: 12px }", $"<circle cx=\"50\" cy=\"50\" r=\"{radius}\"/>");

            Assert.Equal(expected, Assert.IsType<SvgCircleElement>(document.Children[0]).R, 2);
        }

        [Fact]
        public async Task TheSizeIsInheritedAtEveryDepth_AndBelowAUseInstance()
        {
            var document = await BuildAsync("body { font-size: 12px }",
                "<defs><text id=\"r\" x=\"1\" y=\"20\" font-size=\"2em\">Ab</text></defs><g><g><use href=\"#r\"/></g></g>");

            var use = Assert.IsType<SvgUseElement>(Descend(document.Children[0]));
            Assert.Equal(24, Assert.IsType<SvgTextElement>(use.Target).Font!.Size, 2);
        }

        // ─── rem is the root element's ───────────────────────────────────────────

        [Fact]
        public async Task Rem_IsTheFontSizeOfTheHtmlRoot_NotOfTheHtmlParent() =>
            Assert.Equal(40, FontSizeOf(await BuildAsync("html { font-size: 20px } body { font-size: 12px }", TextWith("font-size=\"2rem\""))), 2);

        [Fact]
        public async Task Rem_IsTheFontSizeOfTheHtmlRoot_NotOfTheSvg() =>
            Assert.Equal(32, FontSizeOf(await BuildAsync("html { font-size: 16px } body { font-size: 12px } svg { font-size: 10px }", TextWith("font-size=\"2rem\""))), 2);

        [Fact]
        public async Task Rem_OnTheSvgItself_IsTheFontSizeOfTheHtmlRoot() =>
            Assert.Equal(40, FontSizeOf(await BuildAsync("html { font-size: 20px } body { font-size: 12px } svg { font-size: 2rem }", TextWith())), 2);

        [Fact]
        public async Task Rem_OnAGroup_ReachesTheText() =>
            Assert.Equal(40, FontSizeOf(await BuildAsync("html { font-size: 20px } body { font-size: 12px }", $"<g font-size=\"2rem\">{TextWith()}</g>")), 2);

        [Fact]
        public async Task Rem_InAGeometryLength_IsTheFontSizeOfTheHtmlRoot()
        {
            var document = await BuildAsync("html { font-size: 20px } body { font-size: 12px } svg { font-size: 10px }", "<circle cx=\"50\" cy=\"50\" r=\"2rem\"/>");

            Assert.Equal(40, Assert.IsType<SvgCircleElement>(document.Children[0]).R, 2);
        }

        [Fact]
        public async Task TheSizeTheSvgDeclaresAsAnAttribute_WinsOverTheHtmlParent()
        {
            var document = await SvgCssAnimationTests.BuildAsync("body { font-size: 12px }", TextWith("font-size=\"2em\""), null, "width=\"200\" height=\"100\" font-size=\"20\"");

            Assert.Equal(40, FontSizeOf(document), 2);
        }

        [Theory]
        [InlineData("letter-spacing", 20)]
        [InlineData("word-spacing", 20)]
        public async Task Rem_InASpacing_IsTheFontSizeOfTheHtmlRoot(string property, double expected)
        {
            // Chrome: 1rem of the 20px root, whatever the svg's own 10px.
            var document = await BuildAsync("html { font-size: 20px } body { font-size: 12px } svg { font-size: 10px }", TextWith($"{property}=\"1rem\""));

            var text = Assert.IsType<SvgTextElement>(document.Children[0]);
            Assert.Equal(expected, property == "letter-spacing" ? text.LetterSpacing : text.WordSpacing, 2);
        }

        // ─── what an animation starts from ───────────────────────────────────────

        [Fact]
        public async Task AKeyframeThatLeavesFontSizeOut_StartsFromTheSizeTheSvgInherits()
        {
            // Chrome: 12px to 40px is 26 halfway.
            var document = await BuildAsync("body { font-size: 12px } @keyframes k { to { font-size: 40px } } .t { animation: k 1s linear }", TextWith(), 0.5);

            Assert.Equal(26, FontSizeOf(document), 2);
        }

        [Fact]
        public async Task AKeyframeThatLeavesFontSizeOut_StartsFromTheSizeTheSvgDeclares()
        {
            // Chrome: the svg's 2em of 12px is 24px, and 24px to 50px is 37 halfway.
            var document = await BuildAsync("body { font-size: 12px } svg { font-size: 2em } @keyframes k { to { font-size: 50px } } .t { animation: k 1s linear }", TextWith(), 0.5);

            Assert.Equal(37, FontSizeOf(document), 2);
        }

        [Fact]
        public async Task AnAncestorsEmSize_IsOfTheHtmlParentForAnAnimationBelowIt()
        {
            // Chrome: <g font-size=2em> is 24px, and 24px to 40px is 32 halfway.
            var document = await BuildAsync("body { font-size: 12px } @keyframes k { to { font-size: 40px } } .t { animation: k 1s linear }", $"<g font-size=\"2em\">{TextWith()}</g>", 0.5);

            Assert.Equal(32, FontSizeOf(document), 2);
        }

        [Fact]
        public async Task AnEmSizeBelowARemSize_IsOfTheRemForAnAnimationBelowBoth()
        {
            // Chrome: 2rem is 40px (root 20px), the 2em inside it 80px, and 80px to 100px is 90 halfway.
            var document = await BuildAsync("html { font-size: 20px } body { font-size: 12px } @keyframes k { to { font-size: 100px } } .t { animation: k 1s linear }",
                $"<g font-size=\"2rem\"><g font-size=\"2em\">{TextWith()}</g></g>", 0.5);

            Assert.Equal(90, FontSizeOf(document), 2);
        }

        [Fact]
        public async Task AnInheritedRemLength_IsOfTheHtmlRootForAnAnimationBelowIt()
        {
            // Chrome: the group's stroke-width of 2rem is 40px (root 20px, the svg's own 10px does not count) where the rect's animation starts from it.
            var document = await BuildAsync("html { font-size: 20px } body { font-size: 12px } svg { font-size: 10px } @keyframes k { to { stroke-width: 100px } } .t { animation: k 1s linear }",
                "<g stroke-width=\"2rem\"><rect class=\"t\" width=\"20\" height=\"12\" stroke=\"black\"/></g>", 0);

            Assert.Equal(40, Descend(document.Children[0]).StrokeWidth, 2);
        }

        [Fact]
        public async Task AnAnimationOfTheSvgsOwnFontSize_StartsFromTheSizeItInherits()
        {
            // Chrome: 12px to 40px is 26 halfway, for the text below it.
            var document = await BuildAsync("body { font-size: 12px } @keyframes k { to { font-size: 40px } } svg { animation: k 1s linear }", TextWith(), 0.5);

            Assert.Equal(26, FontSizeOf(document), 2);
        }

        [Fact]
        public async Task TheInheritKeywordInAKeyframe_IsTheParentsSize()
        {
            // Chrome: the group's 20px to 60px is 40 halfway.
            var document = await BuildAsync("body { font-size: 12px } @keyframes k { from { font-size: inherit } to { font-size: 60px } } .t { animation: k 1s linear }",
                $"<g font-size=\"20\">{TextWith()}</g>", 0.5);

            Assert.Equal(40, FontSizeOf(document), 2);
        }

        [Fact]
        public async Task AnAncestorsRemSize_IsOfTheHtmlRootForAnAnimationBelowIt()
        {
            // Chrome: <g font-size=2rem> is 40px (root 20px), and 40px to 60px is 50 halfway.
            var document = await BuildAsync("html { font-size: 20px } body { font-size: 12px } @keyframes k { to { font-size: 60px } } .t { animation: k 1s linear }",
                $"<g font-size=\"2rem\">{TextWith()}</g>", 0.5);

            Assert.Equal(50, FontSizeOf(document), 2);
        }

        [Theory]
        [InlineData("@keyframes k { from { font-size: initial } to { font-size: 60px } } .t { animation: k 1s linear }", "")]      // the keyword in a keyframe
        [InlineData("@keyframes k { to { font-size: 60px } } .t { animation: k 1s linear }", "style=\"font-size: initial\"")]       // the keyword declared on the text
        public async Task TheInitialKeyword_IsTheMediumSizeNotTheInheritedOne(string keyframes, string attributes)
        {
            // Chrome: initial is medium (16px) whatever the page says, so 16px to 60px is 38 halfway.
            var document = await BuildAsync("body { font-size: 12px } " + keyframes, TextWith(attributes), 0.5);

            Assert.Equal(38, FontSizeOf(document), 2);
        }

        // ─── a standalone svg has nothing to inherit ─────────────────────────────

        [Fact]
        public void AStandaloneSvg_KeepsTheInitialSize()
        {
            var root = XDocument.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\"><circle cx=\"50\" cy=\"50\" r=\"2em\"/><text x=\"1\" y=\"20\">Ab</text></svg>").Root!;
            var node = new XElementSvgSourceNode(root, root, null, "print");
            Assert.Null(((ISvgSourceNode)node).InheritedFontSizePx);
            Assert.Null(((ISvgSourceNode)node).RootFontSizePx);

            var document = SvgTreeBuilder.Build(node, new HtmlRenderContext());

            Assert.Equal(32, Assert.IsType<SvgCircleElement>(document.Children[0]).R, 2);
            // The resting text size, as it always was: nothing outside the document sets one.
            Assert.Equal(11, Assert.IsType<SvgTextElement>(document.Children[1]).Font!.Size, 2);
        }
    }
}
