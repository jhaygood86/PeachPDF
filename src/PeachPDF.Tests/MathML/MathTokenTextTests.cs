using PeachPDF.Html.Core.Dom;
using PeachPDF.MathML;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.MathML
{
    /// <summary>
    /// A MathML token's (<c>mi</c>, <c>mo</c>, <c>mn</c>, <c>mtext</c>, <c>ms</c>) text lives on its element's box
    /// (<see cref="CssBox.ForeignText"/>) rather than in a child text box. These pin that the rest of the pipeline still sees it
    /// exactly as it saw the box.
    /// </summary>
    public class MathTokenTextTests
    {
        // The math box empties its own children when it is laid out (it keeps only the engine's output), so the tree is read
        // between parsing and layout.
        private static async Task WithMathAsync(string mathMl, Action<CssBox> check)
        {
            var seen = false;

            await LayoutHarness.LayoutAsync(LayoutHarness.Wrap($"<p id='host'>{mathMl}</p>"), prepare: root =>
            {
                check(LayoutHarness.FindById(root, "host")!.Boxes.OfType<CssBoxMath>().Single());
                seen = true;
            });

            Assert.True(seen);
        }

        private static IEnumerable<CssBox> Descendants(CssBox box) =>
            box.Boxes.SelectMany(child => new[] { child }.Concat(Descendants(child)));

        [Fact]
        public async Task ATokensText_IsHeldOnItsElement_NotInATextBox()
        {
            await WithMathAsync("<math><mi>x</mi><mo>+</mo><mn>12</mn></math>", math =>
            {
                var tokens = math.Boxes.Where(b => b.HtmlTag is not null).ToList();

                Assert.Equal(["mi", "mo", "mn"], tokens.Select(t => t.HtmlTag!.Name));
                Assert.Equal(["x", "+", "12"], tokens.Select(t => t.ForeignText));
                Assert.All(tokens, t => Assert.Empty(t.Boxes));
                Assert.DoesNotContain(Descendants(math), b => b.HtmlTag is null && b.Text is not null);
            });
        }

        [Fact]
        public async Task TheSourceNode_ReadsTheTokensText()
        {
            await WithMathAsync("<math><mi>x</mi><mo>&#x2211;</mo></math>", math =>
            {
                var children = new CssBoxMathSourceNode(math).Children.ToList();

                Assert.Equal(["x", "∑"], children.Select(c => c.GetTextContent()));
            });
        }

        [Fact]
        public async Task TextAroundAnElementInsideAToken_KeepsItsOrder()
        {
            await WithMathAsync("<math><mtext>a<b>bold</b>c</mtext></math>", math =>
            {
                var mtext = math.Boxes.Single(b => b.HtmlTag?.Name == "mtext");

                Assert.Null(mtext.ForeignText);
                Assert.Equal(["a", "", "c"], mtext.Boxes.Select(b => b.HtmlTag is null ? b.Text : ""));
                Assert.Equal("ac", new CssBoxMathSourceNode(mtext).GetTextContent());
            });
        }

        [Fact]
        public async Task ATokenWhoseTextArrivesInSeveralPieces_ReadsAsTheirConcatenation()
        {
            await WithMathAsync("<math><mi>a&amp;b</mi></math>", math =>
            {
                var mi = math.Boxes.Single(b => b.HtmlTag?.Name == "mi");

                Assert.Equal("a&b", new CssBoxMathSourceNode(mi).GetTextContent());
            });
        }

        [Fact]
        public async Task TextInANonTokenElement_IsStillATextBox()
        {
            await WithMathAsync("<math><mrow> <mi>x</mi> </mrow></math>", math =>
            {
                var mrow = math.Boxes.Single(b => b.HtmlTag?.Name == "mrow");

                Assert.Null(mrow.ForeignText);
                Assert.Equal(3, mrow.Boxes.Count);
            });
        }

        [Fact]
        public async Task GeneratedContentThatReadsAnElementsText_SeesTheMathTokens()
        {
            await WithMathAsync("<math><mi>x</mi><mo>+</mo><mi>y</mi></math>", math =>
                Assert.Equal("x+y", CssContentEngine.ExtractText(math)));
        }

        [Fact]
        public async Task AnOrdinaryParagraphsTextBoxes_AreUnchanged()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<p id='p'>plain <b>bold</b> text</p>"));

            Assert.All(Descendants(LayoutHarness.FindById(root, "p")!), b => Assert.Null(b.ForeignText));
        }
    }
}
