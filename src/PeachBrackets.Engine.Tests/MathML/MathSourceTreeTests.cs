using System.Text;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;
using PeachBrackets.MathML;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.MathML
{
    /// <summary>
    /// An inline <c>&lt;math&gt;</c> keeps its markup as <see cref="MathSourceElement"/>s while no rule can change what the math builder
    /// reads of an element, and turns it into the boxes the parser builds otherwise. Both must hold the same tree.
    /// </summary>
    public class MathSourceTreeTests
    {
        private const string Formula = "<math><mrow><mi>x</mi><mo>+</mo><mn>12</mn></mrow></math>";

        // The math box empties itself when it is laid out (it keeps only the engine's output), so its tree is read between
        // parsing and layout, and everything a test looks at has to be read inside the callback.
        private static async Task WithMathsAsync(string html, Action<List<CssBoxMath>> check, bool asBoxes = false)
        {
            var seen = false;

            HtmlParser.ParseMathAsBoxes.Value = asBoxes;
            try
            {
                await LayoutHarness.LayoutAsync(html, prepare: root =>
                {
                    var found = new List<CssBoxMath>();
                    Collect(root, found);
                    check(found);
                    seen = true;
                });
            }
            finally
            {
                HtmlParser.ParseMathAsBoxes.Value = false;
            }

            Assert.True(seen);
        }

        private static Task WithMathAsync(string html, Action<CssBoxMath> check, bool asBoxes = false) =>
            WithMathsAsync(html, maths => check(maths.Single()), asBoxes);

        private static void Collect(CssBox box, List<CssBoxMath> found)
        {
            if (box is CssBoxMath math) found.Add(math);

            foreach (var child in box.Boxes) Collect(child, found);
        }

        // ---- what the tree is, written out so the source elements and the boxes can be compared

        private static string Dump(MathSourceElement node)
        {
            if (node.Tag is null) return $"\"{node.Text}\"";

            return $"{node.Name}{(node.Text is null ? "" : "=" + node.Text)}({string.Join(",", node.ChildNodes.Select(Dump))})";
        }

        private static string Dump(CssBox box)
        {
            if (box.HtmlTag is null) return $"\"{box.Text}\"";

            return $"{box.HtmlTag.Name}{(box.ForeignText is null ? "" : "=" + box.ForeignText)}({string.Join(",", box.Boxes.Select(Dump))})";
        }

        // The math element's own ::before and ::after boxes are boxes either way; the source elements stand between them.
        private static string Dump(CssBoxMath math)
        {
            if (math.Source is not { } source) return string.Join(",", math.Boxes.Select(Dump));

            var before = math.Boxes.Where(b => b.IsBeforePseudoElement).Select(Dump);
            var after = math.Boxes.Where(b => !b.IsBeforePseudoElement).Select(Dump);

            return string.Join(",", before.Concat(source.ChildNodes.Select(Dump)).Concat(after));
        }

        [Theory]
        [MemberData(nameof(MathSourceParityTests.Documents), MemberType = typeof(MathSourceParityTests))]
        public async Task TheSourceElementsAndTheBoxes_HoldTheSameTree(string name, string html)
        {
            var document = $"<!DOCTYPE html><html lang='en'><body style='margin: 0'>{html}</body></html>";
            List<string> asSource = [], asBoxes = [];

            await WithMathsAsync(document, maths => asSource = maths.Select(Dump).ToList());
            await WithMathsAsync(document, maths => asBoxes = maths.Select(Dump).ToList(), asBoxes: true);

            Assert.True(asBoxes.SequenceEqual(asSource), $"'{name}': {string.Join(" | ", asBoxes)} != {string.Join(" | ", asSource)}");
        }

        // ---- when the markup stays as source elements

        [Fact]
        public async Task MathWithNoRuleReachingIt_KeepsItsMarkupAsSourceElements()
        {
            await WithMathAsync(LayoutHarness.Wrap($"<p>{Formula}</p>"), math =>
            {
                Assert.NotNull(math.Source);
                Assert.Empty(math.Boxes);
                Assert.Equal("mrow(mi=x(),mo=+(),mn=12())", Dump(math));
            });
        }

        [Theory]
        [InlineData("mi { color: red }")]
        [InlineData("math * { color: red }")]
        [InlineData("* { color: red }")]
        [InlineData("math > mrow { all: unset }")]
        [InlineData("mi::before { content: 'a' }")]
        [InlineData("mi:before { content: 'a' }")]
        [InlineData("math mi::after { content: 'a' }")]
        [InlineData("mi, mn::marker { content: 'a' }")]
        [InlineData("mn { animation-name: x }")]
        [InlineData("mi { string-set: a content(text) }")]
        [InlineData("mi { counter-increment: n }")]
        [InlineData("mi { bookmark-level: 1 }")]
        [InlineData("mrow { display: inline-block }")]
        [InlineData("mi { float: left }")]
        [InlineData("mi:not(:empty) { color: red }")]
        [InlineData("mrow > mi:first-child { color: red }")]
        [InlineData("@media print { mn { color: red } }")]
        [InlineData("mrow:has(mi) { color: red }")]
        public async Task ARuleThatCouldChangeWhatAnElementReads_MakesTheMarkupBoxes(string rule)
        {
            await WithMathAsync(LayoutHarness.Wrap($"<style>{rule}</style><p>{Formula}</p>"), math =>
            {
                Assert.Null(math.Source);
                Assert.NotEmpty(math.Boxes);
            });
        }

        [Theory]
        [InlineData("<math><mrow><mi style='color:red'>x</mi></mrow></math>")]
        [InlineData("<math><mi color='red'>x</mi></math>")]
        [InlineData("<math><mi COLOR='red'>x</mi></math>")]
        [InlineData("<math><mi id='a'>x</mi></math>")]
        public async Task AnAttributeThatCouldChangeWhatAnElementReads_MakesTheMarkupBoxes(string markup)
        {
            await WithMathAsync(LayoutHarness.Wrap($"<p>{markup}</p>"), math =>
            {
                Assert.Null(math.Source);
                Assert.NotEmpty(math.Boxes);
            });
        }

        [Theory]
        [InlineData("p { color: red }")]
        [InlineData("* { box-sizing: border-box; margin: 0 }")]
        [InlineData("mi { font-weight: bold }")]
        [InlineData("mi { font: italic 12pt serif; line-height: 1; text-decoration: underline }")]
        [InlineData("p mi { background: red; border: 1px solid }")]
        [InlineData("mi { padding: 0 1pt; margin: 0 2pt; width: 1em; min-width: 1em; max-height: 2em }")]
        [InlineData("mi { --c: red }")]
        [InlineData("mi { unknown-property: 1 }")]
        [InlineData("mi:empty { color: red }")]
        [InlineData("br::before { content: 'a' }")]
        [InlineData(".k { color: red }")]
        [InlineData("#k { color: red }")]
        [InlineData("div mi { color: red }")]
        [InlineData("mi::selection { color: red }")]
        [InlineData("@media screen { mn { color: red } }")]
        [InlineData("mi.k { color: red }")]
        public async Task ARuleThatCannotChangeWhatAnElementReads_LeavesTheMarkupAsSourceElements(string rule)
        {
            await WithMathAsync(LayoutHarness.Wrap($"<style>{rule}</style><p>{Formula}</p>"), math => Assert.NotNull(math.Source));
        }

        [Fact]
        public async Task ARuleByClass_ReachesAnElementThatHasItAndNoOther()
        {
            await WithMathAsync(
                LayoutHarness.Wrap("<style>.k { color: red }</style><p><math><mi class='a k'>x</mi></math></p>"), math => Assert.Null(math.Source));
            await WithMathAsync(
                LayoutHarness.Wrap("<style>.k { color: red }</style><p><math><mi class='a'>x</mi></math></p>"), math => Assert.NotNull(math.Source));
        }

        [Fact]
        public async Task AMathInsideAnotherMath_IsParsedIntoBoxes()
        {
            await WithMathsAsync(LayoutHarness.Wrap("<p><math><mrow><math><mi>y</mi></math></mrow></math></p>"), maths =>
            {
                Assert.Equal(2, maths.Count);
                Assert.All(maths, math => Assert.Null(math.Source));
            });
        }

        [Fact]
        public async Task AToken_ThatHandsBackToTheOrdinaryPath_ContinuesInTheBoxesItBuilt()
        {
            await WithMathAsync(LayoutHarness.Wrap("<p><math><mi>x</mi><mrow><mtext>a<b>bold</b>c</mtext></mrow><mn>2</mn></math> after</p>"), math =>
            {
                Assert.Null(math.Source);
                Assert.Equal("mi=x(),mrow(mtext(\"a\",b(\"bold\"),\"c\")),mn=2()", Dump(math));
                Assert.Equal("p", math.ParentBox!.HtmlTag!.Name);
            });
        }

        // ---- the source elements as nodes a selector sees

        [Fact]
        public async Task TheSourceElements_AreNodesOfTheDocumentTree()
        {
            await WithMathAsync(LayoutHarness.Wrap("<p><math display='block'><mrow><mi Class='k'>x</mi> <mo/></mrow></math></p>"), math =>
            {
                var mrow = math.Source!.ElementChildren.Single();
                var mi = mrow.ElementChildren[0];
                var mo = mrow.ElementChildren[1];

                ICssDomNode node = mi;

                Assert.Equal("mi", node.TagName);
                Assert.Same(mrow, node.Parent);
                Assert.Same(math, ((ICssDomNode)mrow).Parent);
                Assert.False(node.IsRoot);
                Assert.Equal(StringComparison.Ordinal, node.NameComparison);
                Assert.Equal(["mi", null, "mo"], ((ICssDomNode)mrow).Children.Select(c => c.TagName));
                Assert.Equal(["mrow"], ((ICssDomNode)math).Children.Select(c => c.TagName));
                Assert.Equal("x", mi.GetTextContent());
                Assert.Equal([mi, mo], mrow.ElementChildren);

                // Attribute names are case-sensitive in foreign content, which is not what the tag's own lookup does.
                Assert.Equal("k", node.GetAttribute("Class"));
                Assert.Null(node.GetAttribute("class"));
                Assert.Equal("k", mi.GetAttribute("class"));
                Assert.Null(((ICssDomNode)mo).GetAttribute("Class"));

                node.CustomProperties = new Dictionary<string, string> { ["--a"] = "b" };
                Assert.Null(node.CustomProperties);
            });
        }

        [Theory]
        [InlineData("<math></math>", true)]
        [InlineData("<math> \n </math>", true)]
        [InlineData("<math><mi>x</mi></math>", false)]
        [InlineData("<math>loose</math>", false)]
        public async Task TheMathElement_IsEmptyForASelectorWhenItHoldsNothing(string markup, bool empty)
        {
            await WithMathAsync(LayoutHarness.Wrap($"<p>{markup}</p>"), math =>
            {
                Assert.NotNull(math.Source);
                Assert.Equal(empty, ((ICssDomNode)math).IsEmpty);
            });
        }

        [Fact]
        public async Task ATokenIsEmptyForASelectorOnlyWhenItsTextIsWhiteSpace()
        {
            await WithMathAsync(LayoutHarness.Wrap("<p><math><mi> </mi><mi>x</mi><mi/><mrow><mi>y</mi></mrow><mtext> <b>a</b> </mtext></math></p>"), math =>
            {
                var elements = math.Source is { } source ? source.ElementChildren : [];

                // <b> is not a MathML element, so the markup went down the ordinary path and these are boxes.
                Assert.Null(math.Source);
                Assert.Equal([true, false, true, false, false], math.Boxes.Where(b => b.HtmlTag is not null).Select(b => CssBox.IsEmptyElement(b)).Select(e => e));
                Assert.Empty(elements);
            });

            await WithMathAsync(LayoutHarness.Wrap("<p><math><mi> </mi><mi>x</mi><mi/><mrow><mi>y</mi></mrow></math></p>"), math =>
                Assert.Equal([true, false, true, false], math.Source!.ElementChildren.Select(e => ((ICssDomNode)e).IsEmpty)));
        }

        [Fact]
        public async Task ATokenHoldingText_IsNotEmptyAsABox()
        {
            await WithMathAsync(LayoutHarness.Wrap("<p><math><mi>x</mi><mi/></math></p>"), math =>
                Assert.Equal([false, true], math.Boxes.Where(b => b.HtmlTag?.Name == "mi").Select(CssBox.IsEmptyElement)), asBoxes: true);
        }

        [Fact]
        public async Task TheTextOfARun_IsAppendedInOrder_WithAnOptionalSeparator()
        {
            await WithMathAsync(LayoutHarness.Wrap("<p><math>a<mrow><mi>x</mi>b<mo>+</mo></mrow>c</math></p>"), math =>
            {
                var plain = new StringBuilder();
                var separated = new StringBuilder();
                math.Source!.AppendRuns(plain, null);
                math.Source.AppendRuns(separated, "|");

                Assert.Equal("axb+c", plain.ToString());
                Assert.Equal("a|x|b|+|c|", separated.ToString());
            });
        }

        [Fact]
        public async Task TheTextContent_OfAnElementWithMixedContent_IsItsRunsOnly()
        {
            await WithMathAsync(LayoutHarness.Wrap("<p><math>one<mrow><mi>x</mi></mrow>two</math></p>"), math =>
            {
                Assert.Equal("onetwo", math.Source!.GetTextContent());
                Assert.Equal("x", math.Source.ElementChildren.Single().ElementChildren.Single().GetTextContent());
                Assert.Equal("", math.Source.ElementChildren.Single().GetTextContent());
            });
        }

        // ---- what the rest of the pipeline reads of the elements

        [Theory]
        [InlineData(true, "<math><mi>א</mi><mi>x</mi></math>", "rtl")]
        [InlineData(false, "<math><mi>א</mi><mi>x</mi></math>", "rtl")]
        [InlineData(true, "<math><mrow>א<mi>x</mi></mrow></math>", "rtl")]
        [InlineData(false, "<math><mrow>א<mi>x</mi></mrow></math>", "rtl")]
        [InlineData(true, "<math><mi>x</mi><mi>א</mi></math>", "ltr")]
        [InlineData(false, "<math><mi>x</mi><mi>א</mi></math>", "ltr")]
        [InlineData(true, "<math><mn>1</mn></math>", null)]
        [InlineData(false, "<math><mn>1</mn></math>", null)]
        public async Task TheFirstStrongCharacterOfAMathsText_DecidesDirAuto(bool asBoxes, string markup, string? expected)
        {
            await WithMathAsync(LayoutHarness.Wrap($"<p dir='auto'>{markup}</p>"),
                math => Assert.Equal(expected, BidiDirectionalityResolver.ScanForStrongDirection(math)), asBoxes);
        }

        [Fact]
        public async Task TheChildrenOfAnElementThatHadAnAfterBox_StayAheadOfIt()
        {
            await WithMathAsync(LayoutHarness.Wrap("<style>math::before { content: '[' } math::after { content: ']' } mi { color: red }</style><p><math><mi>x</mi></math></p>"), math =>
            {
                Assert.Null(math.Source);
                Assert.Equal(["\"[\"", "mi=x()", "\"]\""], math.Boxes.Select(Dump));
            });
        }

        // ---- what the math builder reads

        [Fact]
        public async Task TheSourceElements_AnswerTheBuildersQuestionsLikeTheBoxesDo()
        {
            var document = LayoutHarness.Wrap("<p style='color: #123456; font-size: 18pt'><math><mi mathvariant='bold'>x</mi></math></p>");
            IMathSourceNode? fromSource = null, fromBoxes = null;

            await WithMathAsync(document, math => fromSource = math.Source!.ElementChildren.Single());
            await WithMathAsync(document, math => fromBoxes = new CssBoxMathSourceNode(math).Children.Single(), asBoxes: true);

            Assert.Equal(fromBoxes!.Name, fromSource!.Name);
            Assert.Equal(fromBoxes.PaintColor, fromSource.PaintColor);
            Assert.Equal(fromBoxes.FontSizePt, fromSource.FontSizePt);
            Assert.Equal(fromBoxes.GetAttribute("mathvariant"), fromSource.GetAttribute("mathvariant"));
            Assert.Equal(fromBoxes.Attributes.ToList(), fromSource.Attributes.ToList());
            Assert.Equal(fromBoxes.GetFontAtSize(10).Size, fromSource.GetFontAtSize(10).Size);
            Assert.Equal(fromBoxes.GetTextContent(), fromSource.GetTextContent());
        }

        [Fact]
        public async Task ALaidOutMath_HoldsNeitherBoxesNorSourceElements()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap($"<p id='p'>{Formula}</p>"));

            var math = LayoutHarness.FindById(root, "p")!.Boxes.OfType<CssBoxMath>().Single();

            Assert.Null(math.Source);
            Assert.Empty(math.Boxes);
            Assert.NotNull(math.Layout);
            Assert.Empty(((ICssDomNode)math).Children);
        }
    }
}
