using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Coverage for SVG <c>&lt;text&gt;</c>'s <c>font-variant-ligatures/-caps/-numeric/-east-asian</c>,
    /// <c>font-feature-settings</c>, and <c>font-kerning</c> support (issue #533) - previously none of
    /// these were read anywhere in <see cref="SvgTreeBuilder"/>, so every run shaped with
    /// <see cref="TextShapingFeatures.Default"/> regardless of what was authored. Asserts the resolved
    /// <see cref="TextShapingFeatures"/> actually reaches <see cref="RGraphics.DrawString"/>.
    /// </summary>
    public class SvgTextFontVariantTests
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
        public void Default_ShapesWithDefaultFeatures()
        {
            var g = Render("""<text x="10" y="50" font-size="20">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            var features = draw.Features!.Value;
            Assert.Equal(LigatureFeatures.Default, features.Ligatures);
            Assert.Equal(FontVariantCapsFeature.None, features.Caps);
            Assert.Equal(NumericFeatures.None, features.Numeric);
            Assert.Equal(EastAsianFeatures.None, features.EastAsian);
            Assert.Empty(features.ExplicitFeatures ?? []);
            Assert.True(features.Kerning);
        }

        [Fact]
        public void FontVariantLigatures_None_DisablesCommonAndContextual()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-variant-ligatures="none">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(LigatureFeatures.Required, draw.Features!.Value.Ligatures);
        }

        [Fact]
        public void FontVariantLigatures_DiscretionaryAdditive()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-variant-ligatures="discretionary-ligatures">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            var features = draw.Features!.Value;
            Assert.True((features.Ligatures & LigatureFeatures.Discretionary) != 0);
            Assert.True((features.Ligatures & LigatureFeatures.Common) != 0); // additive, not replaced
        }

        [Fact]
        public void FontVariantNumeric_TabularNums_Requested()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-variant-numeric="tabular-nums">12</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(NumericFeatures.TabularNums, draw.Features!.Value.Numeric);
        }

        [Fact]
        public void FontVariantEastAsian_JisForms_Requested()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-variant-east-asian="jis78-forms">A</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(EastAsianFeatures.Jis78, draw.Features!.Value.EastAsian);
        }

        [Fact]
        public void FontFeatureSettings_ExplicitTag_Parsed()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-feature-settings='"ss01" 1'>Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            var settings = draw.Features!.Value.ExplicitFeatures;
            Assert.NotNull(settings);
            Assert.Contains(("ss01", 1), settings!);
        }

        [Fact]
        public void FontKerning_None_DisablesKerning()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-kerning="none">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.False(draw.Features!.Value.Kerning);
        }

        [Fact]
        public void FontVariantCaps_SmallCaps_RequestedWhenFontSupportsIt()
        {
            // Gated through RFont.SupportsFontVariantCaps, matching HTML's own
            // DerivedStyle.ActualFontVariantCaps - the default resolved test font supports smcp, so the
            // request is forwarded as-is. No small-caps synthesis fallback exists for SVG (a
            // deliberately smaller scope than HTML - see .claude/accepted-gaps) - this only proves real
            // GSUB substitution gets requested when the font can honor it.
            var g = Render("""<text x="10" y="50" font-size="20" font-variant-caps="small-caps">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(FontVariantCapsFeature.SmallCaps, draw.Features!.Value.Caps);
        }

        [Fact]
        public void FontVariantCaps_Normal_RequestsNoCaps()
        {
            var g = Render("""<text x="10" y="50" font-size="20">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(FontVariantCapsFeature.None, draw.Features!.Value.Caps);
        }

        [Fact]
        public void Inheritance_TspanInheritsAncestorFeatures()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-variant-numeric="oldstyle-nums"><tspan>Hi</tspan></text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(NumericFeatures.OldstyleNums, draw.Features!.Value.Numeric);
        }

        [Fact]
        public void TspanCanOverrideAncestorFeatures()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-kerning="none">A<tspan font-kerning="normal">B</tspan></text>""");

            Assert.Equal(2, g.DrawStringCalls.Count);
            Assert.False(g.DrawStringCalls[0].Features!.Value.Kerning);
            Assert.True(g.DrawStringCalls[1].Features!.Value.Kerning);
        }
    }
}
