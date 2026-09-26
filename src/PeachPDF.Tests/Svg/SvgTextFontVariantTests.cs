using PeachDrawing.Text.Shaping;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using PeachDrawing.Text.Internal.Text;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Coverage for SVG <c>&lt;text&gt;</c>'s <c>font-variant-ligatures/-caps/-numeric/-east-asian</c>,
    /// <c>font-feature-settings</c>, and <c>font-kerning</c> support (issue #533) - previously none of
    /// these were read anywhere in <see cref="SvgTreeBuilder"/>, so every run shaped with
    /// <see cref="ShapeSettings.Default"/> regardless of what was authored. Asserts the resolved
    /// <see cref="ShapeSettings"/> actually reaches <see cref="RGraphics.DrawString"/>.
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
            Assert.Equal(LigatureSet.Default, features.Ligatures);
            Assert.Equal(CapsMode.None, features.Caps);
            Assert.Equal(NumeralSet.None, features.Numeric);
            Assert.Equal(EastAsianSet.None, features.EastAsian);
            Assert.Empty(features.ExplicitFeatures ?? []);
            Assert.True(features.Kerning);
        }

        [Fact]
        public void FontVariantLigatures_None_DisablesCommonAndContextual()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-variant-ligatures="none">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(LigatureSet.Required, draw.Features!.Value.Ligatures);
        }

        [Fact]
        public void FontVariantLigatures_DiscretionaryAdditive()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-variant-ligatures="discretionary-ligatures">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            var features = draw.Features!.Value;
            Assert.True((features.Ligatures & LigatureSet.Discretionary) != 0);
            Assert.True((features.Ligatures & LigatureSet.Common) != 0); // additive, not replaced
        }

        [Fact]
        public void FontVariantNumeric_TabularNums_Requested()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-variant-numeric="tabular-nums">12</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(NumeralSet.TabularNums, draw.Features!.Value.Numeric);
        }

        [Fact]
        public void FontVariantEastAsian_JisForms_Requested()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-variant-east-asian="jis78-forms">A</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(EastAsianSet.Jis78, draw.Features!.Value.EastAsian);
        }

        [Fact]
        public void FontFeatureSettings_ExplicitTag_Parsed()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-feature-settings='"ss01" 1'>Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            var settings = draw.Features!.Value.ExplicitFeatures;
            Assert.NotNull(settings);
            Assert.Contains(new FeatureSetting("ss01", 1), settings!);
        }

        [Fact]
        public void FontKerning_None_DisablesKerning()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-kerning="none">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.False(draw.Features!.Value.Kerning);
        }

        [Fact]
        public async Task FontVariantCaps_SmallCaps_RequestedWhenFontSupportsIt()
        {
            // Gated through RFont.SupportsFontVariantCaps, matching HTML's own
            // DerivedStyle.ActualFontVariantCaps - explicitly registers BundledFonts.Ttf (Source Sans 3,
            // confirmed real smcp support - see FontVariantCapsIntegrationTests) rather than relying on
            // whatever font the platform's own default-family resolution happens to pick, which is not
            // guaranteed to support smcp and differs between CI platforms (this test previously depended
            // on the ambient default font and failed on Linux CI while passing on Windows). No small-caps
            // synthesis fallback exists for SVG (a deliberately smaller scope than HTML - see
            // .claude/accepted-gaps) - this only proves real GSUB substitution gets requested when the
            // font can honor it.
            await using var stream = File.OpenRead(BundledFonts.Ttf);
            await Adapter.AddFont(stream, "SmallCapsTest");

            var g = Render("""<text x="10" y="50" font-size="20" font-family="SmallCapsTest" font-variant-caps="small-caps">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(CapsMode.SmallCaps, draw.Features!.Value.Caps);
        }

        [Fact]
        public void FontVariantCaps_Normal_RequestsNoCaps()
        {
            var g = Render("""<text x="10" y="50" font-size="20">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(CapsMode.None, draw.Features!.Value.Caps);
        }

        [Fact]
        public void Inheritance_TspanInheritsAncestorFeatures()
        {
            var g = Render("""<text x="10" y="50" font-size="20" font-variant-numeric="oldstyle-nums"><tspan>Hi</tspan></text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(NumeralSet.OldstyleNums, draw.Features!.Value.Numeric);
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
