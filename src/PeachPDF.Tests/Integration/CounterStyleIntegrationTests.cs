using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS Counter Styles Level 3: the <c>symbols()</c> functional notation and author-defined
    /// <c>@counter-style</c> rules, as they reach a list marker or <c>counter()</c>.
    /// </summary>
    public class CounterStyleIntegrationTests
    {
        private static async Task<List<string>> MarkerTexts(string css, string listHtml)
        {
            var root = await BuildAndLayout($"<!DOCTYPE html><html><head><style>{css}</style></head><body>{listHtml}</body></html>");
            var texts = new List<string>();
            Collect(root, texts);
            return texts;
        }

        private static void Collect(CssBox box, List<string> texts)
        {
            foreach (var child in box.Boxes)
            {
                if (child is CssBoxMarker marker) texts.Add(marker.Text ?? "");
                Collect(child, texts);
            }
        }

        private static string Items(int count, string attrs = "") =>
            "<ol" + (attrs.Length > 0 ? " " + attrs : "") + ">" + string.Concat(Enumerable.Range(1, count).Select(_ => "<li>x</li>")) + "</ol>";

        [Fact]
        public async Task SymbolsCyclic_RepeatsTheList()
        {
            var texts = await MarkerTexts("ol { list-style-type: symbols(cyclic '*' '†') }", Items(3));
            Assert.Equal(["*. ", "†. ", "*. "], texts);
        }

        [Fact]
        public async Task SymbolsSymbolic_IsTheDefaultTypeAndRepeatsTheSymbol()
        {
            var texts = await MarkerTexts("ol { list-style-type: symbols('a' 'b') }", Items(3));
            Assert.Equal(["a. ", "b. ", "aa. "], texts);
        }

        [Fact]
        public async Task SymbolsAlphabetic_IsBijective()
        {
            var texts = await MarkerTexts("ol { list-style-type: symbols(alphabetic 'a' 'b') }", Items(4));
            Assert.Equal(["a. ", "b. ", "aa. ", "ab. "], texts);
        }

        [Fact]
        public async Task SymbolsNumeric_IsPositional()
        {
            var texts = await MarkerTexts("ol { list-style-type: symbols(numeric 'o' 'i') }", Items(4));
            Assert.Equal(["i. ", "io. ", "ii. ", "ioo. "], texts);
        }

        [Fact]
        public async Task SymbolsFixed_FallsBackToDecimalPastTheList()
        {
            var texts = await MarkerTexts("ol { list-style-type: symbols(fixed 'A' 'B') }", Items(3));
            Assert.Equal(["A. ", "B. ", "3. "], texts);
        }

        [Fact]
        public async Task InvalidSymbols_IsIgnoredAndInheritsTheDefault()
        {
            // alphabetic needs two symbols, so the declaration is invalid and dropped: the <ol> keeps decimal.
            var texts = await MarkerTexts("ol { list-style-type: symbols(alphabetic 'a') }", Items(2));
            Assert.Equal(["1.", "2."], texts);
        }

        [Fact]
        public async Task CounterStyleRule_Additive_FormatsTallyStyleNumbers()
        {
            const string css = "@counter-style tally { system: additive; additive-symbols: 5 'V', 1 'I'; suffix: ') '; } ol { list-style-type: tally }";
            var texts = await MarkerTexts(css, Items(7));
            Assert.Equal(["I) ", "II) ", "III) ", "IIII) ", "V) ", "VI) ", "VII) "], texts);
        }

        [Fact]
        public async Task CounterStyleRule_PrefixSuffixAndPad()
        {
            const string css = "@counter-style padded { system: numeric; symbols: '0' '1' '2' '3' '4' '5' '6' '7' '8' '9'; pad: 3 '0'; prefix: '['; suffix: '] '; } ol { list-style-type: padded }";
            var texts = await MarkerTexts(css, Items(2));
            Assert.Equal(["[001] ", "[002] "], texts);
        }

        [Fact]
        public async Task CounterStyleRule_RangeFallsBackToTheFallbackStyle()
        {
            const string css = "@counter-style limited { system: cyclic; symbols: 'a'; range: 2 3; fallback: lower-roman; suffix: ' '; } ol { list-style-type: limited }";
            var texts = await MarkerTexts(css, Items(4));
            Assert.Equal(["i ", "a ", "a ", "iv "], texts);
        }

        [Fact]
        public async Task CounterStyleRule_NegativeSign()
        {
            const string css = "@counter-style neg { system: symbolic; symbols: 'x'; negative: '(' ')'; range: infinite infinite; suffix: ' '; } ol { list-style-type: neg }";
            var texts = await MarkerTexts(css, "<ol start='-2'><li>a</li><li>b</li><li>c</li><li>d</li></ol>");

            // -2 and -1 are signed magnitudes; 0 is unrepresentable in symbolic, so it falls back to decimal.
            Assert.Equal(["(xx) ", "(x) ", "0 ", "x "], texts);
        }

        [Fact]
        public async Task CounterStyleRule_ExtendsAnotherRuleAndInheritsItsSymbols()
        {
            const string css = "@counter-style base { system: cyclic; symbols: 'a' 'b'; suffix: ' '; } @counter-style child { system: extends base; prefix: '<'; } ol { list-style-type: child }";
            var texts = await MarkerTexts(css, Items(3));
            Assert.Equal(["<a ", "<b ", "<a "], texts);
        }

        [Fact]
        public async Task CounterStyleRule_ExtendsPredefinedStyle()
        {
            const string css = "@counter-style roman { system: extends lower-roman; suffix: ' - '; } ol { list-style-type: roman }";
            var texts = await MarkerTexts(css, Items(3));
            Assert.Equal(["i - ", "ii - ", "iii - "], texts);
        }

        [Fact]
        public async Task CounterStyleRule_ExtendsCycle_BehavesAsDecimal()
        {
            const string css = "@counter-style a { system: extends b; } @counter-style b { system: extends a; } ol { list-style-type: a }";
            var texts = await MarkerTexts(css, Items(2));
            Assert.Equal(["1. ", "2. "], texts);
        }

        [Fact]
        public async Task CounterStyleRule_CanOverrideAPredefinedStyleButNotTheReservedOnes()
        {
            const string css = "@counter-style upper-alpha { system: cyclic; symbols: '#'; } @counter-style decimal { system: cyclic; symbols: '!'; } ol { list-style-type: upper-alpha } ul { list-style-type: decimal }";
            var texts = await MarkerTexts(css, Items(1) + "<ul><li>x</li></ul>");
            Assert.Equal(["#. ", "1."], texts);
        }

        [Fact]
        public async Task CounterStyleRule_Invalid_IsIgnored()
        {
            const string css = "@counter-style broken { system: cyclic; } ol { list-style-type: broken }";
            var texts = await MarkerTexts(css, Items(2));
            Assert.Equal(["1.", "2."], texts);
        }

        [Fact]
        public async Task UnknownCounterStyleName_FallsBackToDecimal()
        {
            var texts = await MarkerTexts("ol { list-style-type: nope }", Items(2));
            Assert.Equal(["1.", "2."], texts);
        }

        [Fact]
        public async Task CounterStyleRule_InsideMediaRule_IsHonoured()
        {
            const string css = "@media all { @counter-style stars { system: cyclic; symbols: '*'; suffix: ' '; } } ol { list-style-type: stars }";
            var texts = await MarkerTexts(css, Items(1));
            Assert.Equal(["* "], texts);
        }

        [Fact]
        public async Task ListStyleShorthand_AcceptsACustomCounterStyleName()
        {
            const string css = "@counter-style stars { system: cyclic; symbols: '*'; suffix: ' '; } ol { list-style: inside stars }";
            var texts = await MarkerTexts(css, Items(1));
            Assert.Equal(["* "], texts);
        }

        [Fact]
        public async Task CounterFunction_UsesACounterStyleRuleAndSymbols()
        {
            const string css = "@counter-style stars { system: cyclic; symbols: '*' '+'; } li::before { content: counter(list-item, stars) counter(list-item, symbols(alphabetic 'a' 'b')) }";
            var root = await BuildAndLayout($"<!DOCTYPE html><html><head><style>{css}</style></head><body>{Items(2)}</body></html>");
            var contents = new List<string>();
            CollectBefore(root, contents);
            Assert.Equal(["*a", "+b"], contents);
        }

        [Theory]
        [InlineData("system: fixed 5; symbols: 'a' 'b'; suffix: ' '", 4, "4 ")]
        [InlineData("system: fixed 5; symbols: 'a' 'b'; suffix: ' '", 6, "b ")]
        [InlineData("system: cyclic; symbols: 'a'; range: 1 2, 5 infinite; fallback: nope; suffix: ' '", 5, "a ")]
        [InlineData("system: cyclic; symbols: 'a'; range: 1 2, 5 infinite; fallback: nope; suffix: ' '", 3, "3 ")]
        [InlineData("system: cyclic; symbols: 'a'; range: 4 1; suffix: ' '", 3, "a ")]
        [InlineData("system: additive; additive-symbols: 2 'B', 0 'zero'; suffix: ' '", 0, "zero ")]
        [InlineData("system: additive; additive-symbols: 2 'B', 0 'zero'; suffix: ' '", 3, "3 ")]
        [InlineData("system: additive; additive-symbols: 1 'A', 2 'B'; suffix: ' '", 1, "1.")]
        [InlineData("system: bogus; symbols: 'a'", 1, "1.")]
        [InlineData("system: fixed x; symbols: 'a'", 1, "1.")]
        [InlineData("system: cyclic; symbols: 'a'; pad: x y z; suffix: ' '", 1, "a ")]
        [InlineData("system: cyclic; symbols: 'a'; fallback: 1; suffix: ' '", 1, "a ")]
        [InlineData("system: extends; symbols: 'a'", 1, "1.")]
        public async Task CounterStyleRule_DescriptorEdgeCases(string descriptors, int value, string expected)
        {
            var css = "@counter-style t { " + descriptors + " } ol { list-style-type: t }";
            var texts = await MarkerTexts(css, "<ol start='" + value + "'><li>x</li></ol>");
            Assert.Equal([expected], texts);
        }

        [Fact]
        public async Task FallbackLoop_EndsInDecimal()
        {
            const string css = "@counter-style a { system: fixed; symbols: 'x'; fallback: b; } @counter-style b { system: fixed; symbols: 'y'; fallback: a; } ol { list-style-type: a }";
            var texts = await MarkerTexts(css, "<ol start='7'><li>x</li></ol>");
            Assert.Equal(["7. "], texts);
        }

        [Fact]
        public async Task CountersFunction_AndTargetCounter_UseSymbols()
        {
            const string css = "li::before { content: counters(list-item, '.', symbols(cyclic 'a' 'b')) }";
            var root = await BuildAndLayout($"<!DOCTYPE html><html><head><style>{css}</style></head><body>{Items(2)}</body></html>");
            var contents = new List<string>();
            CollectBefore(root, contents);
            Assert.Equal(["a", "b"], contents);
        }

        private static void CollectBefore(CssBox box, List<string> texts)
        {
            foreach (var child in box.Boxes)
            {
                if (child.IsBeforePseudoElement)
                {
                    texts.Add(string.Concat(child.Boxes.Select(b => b.Text)) + (child.Text ?? ""));
                }

                CollectBefore(child, texts);
            }
        }

        private static async Task<CssBox> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }
    }
}
