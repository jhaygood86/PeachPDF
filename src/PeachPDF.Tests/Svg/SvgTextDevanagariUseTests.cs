using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text.Shaping.Use;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Coverage for SVG <c>&lt;text&gt;</c>'s Devanagari Universal Shaping Engine (USE) category
    /// resolution/shaping-run wiring (issue #533) - mirrors
    /// <see cref="PeachPDF.Tests.Html.Core.DevanagariUseCharacterizationTests"/>'s own "prove the
    /// wiring reaches real shaping" standard, but for SVG's own independent pipeline
    /// (<c>SvgRenderer.ResolveComplexScriptRuns</c>). Real-font syllable-reordering proof lives in
    /// <see cref="SvgTextDevanagariUseCharacterizationTests"/>.
    /// </summary>
    public class SvgTextDevanagariUseTests
    {
        // Same letters PeachPDF.Tests.Html.Core.DevanagariUseCharacterizationTests uses, for
        // consistency across the Devanagari test fixtures.
        private const string Ka = "क";
        private const string Virama = "्";
        private const string Ssa = "ष";
        private const string VowelSignI = "ि";

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
        public void SimpleSyllable_ResolvesDevanagariScriptTagAndUseCategories()
        {
            var g = Render($"""<text x="10" y="50" font-size="20">{Ka}{VowelSignI}</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(Ka + VowelSignI, draw.Text);
            Assert.Equal("deva", draw.Features!.Value.ScriptTag);
            Assert.Equal([UseCategory.B, UseCategory.VPre], draw.Features.Value.UseCategories);
            Assert.Null(draw.Features.Value.JoiningForms);
        }

        [Fact]
        public void DevanagariRun_NeverRequestsReverseForDisplay()
        {
            // USE (Devanagari) is never display-reversed - only Arabic-family joining is (see
            // ResolveComplexScriptRuns/ApplyBidiReordering's own remarks, mirroring
            // CssRectWord.DisplayOrderReversed's own EffectiveJoiningForms-only gating on the HTML
            // side). Devanagari's own strong-L bidi class keeps it in a left-to-right visual run
            // regardless of the paragraph's own direction, so this can never reach the RTL
            // block-reflect path that sets the flag.
            var g = Render($"""<text x="10" y="50" font-size="20">{Ka}{VowelSignI}</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.False(draw.Features!.Value.ReverseForDisplay);
        }

        [Fact]
        public void ConjunctWithVirama_FormsOneRunAcrossTheWholeSpan()
        {
            var g = Render($"""<text x="10" y="50" font-size="20">{Ka}{Virama}{Ssa}{VowelSignI}</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(Ka + Virama + Ssa + VowelSignI, draw.Text);
            Assert.Equal(4, draw.Features!.Value.UseCategories!.Count);
        }

        [Fact]
        public void MixedArabicAndDevanagari_EachGetsItsOwnCategoryKind()
        {
            var g = Render($"""<text x="10" y="50" font-size="20">ب{Ka}{VowelSignI}</text>""");

            Assert.Equal(2, g.DrawStringCalls.Count);
            Assert.NotNull(g.DrawStringCalls[0].Features!.Value.JoiningForms);
            Assert.Null(g.DrawStringCalls[0].Features!.Value.UseCategories);
            Assert.NotNull(g.DrawStringCalls[1].Features!.Value.UseCategories);
            Assert.Null(g.DrawStringCalls[1].Features!.Value.JoiningForms);
        }

        [Fact]
        public void PlainLatinText_NeverGetsUseCategories()
        {
            var g = Render("""<text x="10" y="50" font-size="20">Hello</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.NotEqual("deva", draw.Features!.Value.ScriptTag);
            Assert.Null(draw.Features.Value.UseCategories);
        }

        [Fact]
        public void MixedDevanagariAndLatinParagraph_LatinWordStaysUnaffected()
        {
            // Regression for the HTML-side equivalent bug this exact scenario caught there (see
            // DevanagariUseCharacterizationTests.EndToEndLayout_MixedDevanagariAndLatinParagraph...):
            // resolving USE categories over the whole flattened stream must not spuriously tag Latin
            // text elsewhere in the same <text> element with a non-null, all-UseCategory.O array.
            var g = Render($"""<text x="10" y="50" font-size="20">Hello {Ka}{VowelSignI}</text>""");

            Assert.Equal(2, g.DrawStringCalls.Count);
            Assert.Equal("Hello ", g.DrawStringCalls[0].Text);
            Assert.Null(g.DrawStringCalls[0].Features!.Value.UseCategories);
            Assert.NotEqual("deva", g.DrawStringCalls[0].Features!.Value.ScriptTag);
        }
    }
}
