using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Coverage for SVG <c>&lt;text&gt;</c>'s per-element <c>lang</c>/<c>xml:lang</c>-driven GSUB
    /// language-system selection (issue #533) - previously <see cref="SvgTreeBuilder"/> had no notion of
    /// resolved language at all, so <c>SvgTextElement.ShapingFeatures.Language</c> was always null and
    /// every run resolved each script's default <c>LangSys</c> regardless of what was authored (a gap
    /// this PR's own "SVG requests the same GSUB/GPOS features HTML text does" claim didn't originally
    /// account for - see <c>docs/html-css-support.md</c>'s "Text shaping" section). Mirrors
    /// <c>CssBox.Language</c>'s HTML-side "own value, else nearest ancestor's" resolution. Asserts the
    /// resolved language actually reaches <see cref="RGraphics.DrawString"/> via
    /// <see cref="TextShapingFeatures"/>, not just that it parses.
    /// </summary>
    public class SvgTextLanguageTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static TestRecordingGraphics Render(string body)
        {
            var markup = $$"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
                  {{body}}
                </svg>
                """;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new RRect(0, 0, 200, 100));
            return g;
        }

        [Fact]
        public void Default_NoLangAnywhere_LanguageIsNull()
        {
            var g = Render("""<text x="10" y="50" font-size="20">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Null(draw.Features!.Value.Language);
        }

        [Fact]
        public void Lang_OnTextElement_ReachesShapingFeatures()
        {
            var g = Render("""<text x="10" y="50" font-size="20" lang="tr">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("tr", draw.Features!.Value.Language);
        }

        [Fact]
        public void XmlLang_FallsBackWhenPlainLangIsAbsent()
        {
            var g = Render("""<text x="10" y="50" font-size="20" xml:lang="fr">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("fr", draw.Features!.Value.Language);
        }

        [Fact]
        public void PlainLang_TakesPrecedenceOverXmlLang()
        {
            var g = Render("""<text x="10" y="50" font-size="20" lang="tr" xml:lang="fr">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("tr", draw.Features!.Value.Language);
        }

        [Fact]
        public void InheritsFromAncestor_ThroughNonTextElements()
        {
            // lang inherits from ANY ancestor (<g>, not just a <text>/<tspan> ancestor), per normal
            // SVG/CSS inheritance - same as font-family/-size already do.
            var g = Render("""<g lang="ja"><text x="10" y="50" font-size="20">Hi</text></g>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("ja", draw.Features!.Value.Language);
        }

        [Fact]
        public void TspanOverridesAncestorLang()
        {
            var g = Render("""<text x="10" y="50" font-size="20" lang="tr">A<tspan lang="de">B</tspan></text>""");

            Assert.Equal(2, g.DrawStringCalls.Count);
            Assert.Equal("tr", g.DrawStringCalls[0].Features!.Value.Language);
            Assert.Equal("de", g.DrawStringCalls[1].Features!.Value.Language);
        }

        [Fact]
        public void EmptyLang_FallsThroughToInheritedValue()
        {
            // Matches CssBox.Language's own simplification: lang="" doesn't reset to "no language", it
            // falls through to the nearest ancestor's value (here: none, so null).
            var g = Render("""<text x="10" y="50" font-size="20" lang="">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Null(draw.Features!.Value.Language);
        }

        [Fact]
        public void InlineSvg_FallsBackToSurroundingHtmlDocumentLanguage()
        {
            // An inline <svg> with no lang/xml:lang anywhere in its own subtree falls back to its
            // surrounding HTML ancestor's resolved language (CssBox.Language) - the SVG root's own
            // DocumentLanguageFallback, seeded once at the top of the build.
            var root = HtmlParser.ParseDocument(
                """<html lang="es"><body><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100"><text x="10" y="50" font-size="20">Hi</text></svg></body></html>""");
            var svgBox = DomUtils.GetBoxByTagName(root, "svg");
            Assert.NotNull(svgBox);

            var document = SvgTreeBuilder.Build(new CssBoxSvgSourceNode(svgBox!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new RRect(0, 0, 200, 100));

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("es", draw.Features!.Value.Language);
        }

        [Fact]
        public void InlineSvg_OwnLangOverridesSurroundingHtmlDocumentLanguage()
        {
            var root = HtmlParser.ParseDocument(
                """<html lang="es"><body><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100" lang="ja"><text x="10" y="50" font-size="20">Hi</text></svg></body></html>""");
            var svgBox = DomUtils.GetBoxByTagName(root, "svg");
            Assert.NotNull(svgBox);

            var document = SvgTreeBuilder.Build(new CssBoxSvgSourceNode(svgBox!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new RRect(0, 0, 200, 100));

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("ja", draw.Features!.Value.Language);
        }
    }
}
