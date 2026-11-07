using PeachBrackets.Html.Dom;
using PeachBrackets.MathML;
using PeachPDF.Rendering;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.MathML
{
    /// <summary><c>math-style</c> and <c>math-depth</c> (MathML Core section 3.3) reach the math builder's context and win over <c>displaystyle</c>/<c>scriptlevel</c>.</summary>
    public class MathCssPropertyTests
    {
        // The math box empties itself when it is laid out, so the builder is run between parsing and layout.
        private static async Task<MathNode> BuildRootAsync(string html)
        {
            MathNode? root = null;
            await LayoutHarness.LayoutAsync(html, prepare: tree =>
            {
                var math = Find(tree);
                Assert.NotNull(math);
                root = MathTreeBuilder.Build(math!.Source is { } source ? source : new CssBoxMathSourceNode(math), new HtmlRenderContext()).Root;
            });

            return Assert.IsAssignableFrom<MathNode>(root);
        }

        private static CssBoxMath? Find(CssBox box)
        {
            if (box is CssBoxMath math) return math;
            foreach (var child in box.Boxes)
            {
                if (Find(child) is { } found) return found;
            }

            return null;
        }

        private static string Page(string css, string math) => LayoutHarness.Wrap($"<style>{css}</style><p>{math}</p>");

        private const string Fraction = "<mfrac><mi>a</mi><mi>b</mi></mfrac>";

        [Theory]
        [InlineData("", "<math>", false)]
        [InlineData("", "<math display='block'>", true)]
        [InlineData("math { math-style: normal }", "<math>", true)]
        [InlineData("math { math-style: compact }", "<math display='block'>", false)]
        [InlineData("math { math-style: compact }", "<math>", false)]
        [InlineData("math { math-style: normal }", "<math display='block'>", true)]
        public async Task TheRootsDisplayStyleIsItsMathStyle(string css, string open, bool expected)
        {
            var root = await BuildRootAsync(Page(css, $"{open}{Fraction}</math>"));

            Assert.Equal(expected, root.DisplayStyle);
        }

        [Fact]
        public async Task MathStyleBeatsTheDisplaystyleAttributeOnMstyle()
        {
            var plain = await BuildRootAsync(Page("", $"<math><mstyle displaystyle='true'>{Fraction}</mstyle></math>"));
            var overridden = await BuildRootAsync(Page("math { math-style: normal } mstyle { math-style: compact }", $"<math><mstyle displaystyle='true'>{Fraction}</mstyle></math>"));
            var raised = await BuildRootAsync(Page("mstyle { math-style: normal }", $"<math><mstyle>{Fraction}</mstyle></math>"));

            Assert.True(plain.DisplayStyle);
            Assert.False(overridden.DisplayStyle);
            Assert.True(raised.DisplayStyle);
        }

        [Fact]
        public async Task MathStyleOnAnyElementReachesIt_AndItsDescendants()
        {
            var root = await BuildRootAsync(Page("mrow { math-style: normal }", "<math><mrow><mi>x</mi></mrow></math>"));

            var row = Assert.IsType<MathRowNode>(root);
            Assert.True(row.DisplayStyle);
        }

        [Theory]
        [InlineData("2", 2)]
        [InlineData("add(2)", 2)]
        [InlineData("add(-1)", -1)]
        [InlineData("auto-add", 0)]
        [InlineData("0", 0)]
        public async Task MathDepthSetsOrChangesTheScriptLevel(string depth, int expected)
        {
            var root = await BuildRootAsync(Page($"mstyle {{ math-depth: {depth} }}", "<math><mstyle><mi>x</mi></mstyle></math>"));

            Assert.Equal(expected, root.ScriptLevel);
        }

        [Fact]
        public async Task MathDepthBeatsTheScriptlevelAttribute_AndAnAbsoluteDepthRestartsTheCount()
        {
            var attribute = await BuildRootAsync(Page("", "<math><mstyle scriptlevel='3'><mi>x</mi></mstyle></math>"));
            var overridden = await BuildRootAsync(Page("mstyle { math-depth: 1 }", "<math><mstyle scriptlevel='3'><mi>x</mi></mstyle></math>"));
            var nested = await BuildRootAsync(Page("#outer { math-depth: 3 } #inner { math-depth: 0 }",
                "<math><mstyle id='outer'><mstyle id='inner'><mi>x</mi></mstyle></mstyle></math>"));

            Assert.Equal(3, attribute.ScriptLevel);
            Assert.Equal(1, overridden.ScriptLevel);
            Assert.Equal(0, nested.ScriptLevel);
        }

        [Fact]
        public async Task MathDepthIsNotAppliedTwiceToTheChildrenThatInheritIt()
        {
            var root = await BuildRootAsync(Page("mrow { math-depth: add(1) }", "<math><mrow><mrow><mi>x</mi></mrow></mrow></math>"));

            var outer = Assert.IsType<MathRowNode>(root);
            var inner = Assert.IsType<MathRowNode>(Assert.Single(outer.Children));
            Assert.Equal(1, outer.ScriptLevel);
            Assert.Equal(1, inner.ScriptLevel);
        }

        [Theory]
        [InlineData(null, 1, 1)]
        [InlineData("", 1, 1)]
        [InlineData("add(2)", 1, 3)]
        [InlineData("ADD( -3 )", 1, -2)]
        [InlineData("add(x)", 1, 1)]
        [InlineData("5", 1, 5)]
        [InlineData("-2", 1, -2)]
        [InlineData("auto-add", 4, 4)]
        [InlineData("bogus", 4, 4)]
        public void ParseMathDepthReadsTheGrammar(string? value, int inherited, int expected)
        {
            Assert.Equal(expected, MathAttributeParser.ParseMathDepth(value, inherited));
        }
    }
}
