using PeachPDF.Adapters;
using PeachPDF.Svg;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Coverage for SVG <c>&lt;text&gt;</c>'s <c>font-stretch</c> support (issue #533) - previously
    /// <c>SvgTreeBuilder.FontContext</c> carried no stretch field at all, and <c>BuildTextRun</c>'s
    /// <c>_adapter.GetFont</c> calls never passed a <c>stretch:</c> argument, so every run resolved the
    /// normal (5) width class regardless of what was authored. <see cref="RFont"/> doesn't expose its
    /// own resolved stretch class back out (the same reason HTML's own font-resolution tests stop at
    /// asserting <c>CssBox.ActualStretch</c>, one layer before <c>GetFont</c> - there is no SVG-side
    /// equivalent of that property to assert against instead), so this proves the keyword→numeric-scale
    /// resolution and inheritance/override wiring exercise every code path without throwing or nulling
    /// out <see cref="SvgTextElement.Font"/> - <see cref="Html.Core.Utils.FontStretchResolverTests"/>
    /// already covers the keyword-to-numeric-scale mapping itself in isolation.
    /// </summary>
    public class SvgTextFontStretchTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static SvgDocument Build(string body)
        {
            var markup = $$"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
                  {{body}}
                </svg>
                """;
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
        }

        private static SvgTextElement TextRoot(SvgDocument document) => Assert.IsType<SvgTextElement>(document.Children[0]);

        [Theory]
        [InlineData("ultra-condensed")]
        [InlineData("extra-condensed")]
        [InlineData("condensed")]
        [InlineData("semi-condensed")]
        [InlineData("normal")]
        [InlineData("semi-expanded")]
        [InlineData("expanded")]
        [InlineData("extra-expanded")]
        [InlineData("ultra-expanded")]
        public void EveryKeyword_ResolvesARealFont_WithoutThrowing(string keyword)
        {
            var document = Build($"""<text x="10" y="50" font-size="20" font-stretch="{keyword}">Hi</text>""");

            Assert.NotNull(TextRoot(document).Font);
        }

        [Fact]
        public void InheritsFromAncestor_ToTspan()
        {
            var document = Build("""<text x="10" y="50" font-size="20" font-stretch="expanded"><tspan>Hi</tspan></text>""");

            var tspan = Assert.IsType<SvgTextSpan>(TextRoot(document).Content[0]);
            Assert.NotNull(tspan.Run.Font);
        }

        [Fact]
        public void TspanOverridesAncestorStretch_StillResolves()
        {
            var document = Build("""<text x="10" y="50" font-size="20" font-stretch="expanded"><tspan font-stretch="condensed">Hi</tspan></text>""");

            var tspan = Assert.IsType<SvgTextSpan>(TextRoot(document).Content[0]);
            Assert.NotNull(tspan.Run.Font);
        }

        [Fact]
        public void StyleAttributeForm_AlsoResolves()
        {
            var document = Build("""<text x="10" y="50" font-size="20" style="font-stretch: semi-condensed">Hi</text>""");

            Assert.NotNull(TextRoot(document).Font);
        }

        /// <summary>Proves the resolved <c>stretch</c> value actually reaches font resolution, rather
        /// than being a complete no-op (the exact failure mode CLAUDE.md's gradient <c>spreadMethod</c>
        /// precedent warns a "resolves without throwing" test alone can't catch). <c>FontsHandler</c>'s
        /// font cache (<c>Html.Core.Handlers.FontsHandler._fontsCache</c>) is keyed by
        /// <c>(style, weight, stretch, obliqueSkewSinus)</c> per family+size, so two tspans differing only
        /// in <c>font-stretch</c> resolve to distinct cached <see cref="RFont"/> instances if and only if
        /// the <c>stretch:</c> argument genuinely reaches <c>_adapter.GetFont</c> - if it were dropped,
        /// both would collapse onto the same (always-normal) cache entry and come back reference-equal.</summary>
        [Fact]
        public void DifferentStretchKeywords_ResolveDistinctCachedFontInstances()
        {
            var document = Build(
                """<text x="10" y="50" font-size="20"><tspan font-stretch="condensed">A</tspan><tspan font-stretch="expanded">B</tspan></text>""");

            var spans = TextRoot(document).Content.OfType<SvgTextSpan>().ToList();
            Assert.Equal(2, spans.Count);

            var condensedFont = spans[0].Run.Font;
            var expandedFont = spans[1].Run.Font;

            Assert.NotNull(condensedFont);
            Assert.NotNull(expandedFont);
            Assert.NotSame(condensedFont, expandedFont);
        }
    }
}
