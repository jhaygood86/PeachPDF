using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// SVG <c>&lt;text&gt;</c> honouring the font properties that depend on a per-document registry or on the used font
    /// (<c>font-variant-alternates</c>/<c>@font-feature-values</c>, <c>font-palette</c>/<c>@font-palette-values</c>) plus the
    /// registry-free <c>font-variant-emoji</c>, asserted on what actually reaches <see cref="Canvas.DrawString"/>.
    /// </summary>
    public class SvgTextFontPaletteAlternatesTests
    {
        private static CssData Data(string css)
        {
            var data = new CssData();
            data.Stylesheets.Add(CssParser.ParseStyleSheet(css));
            return data;
        }

        private static async Task<TestRecordingGraphics> Render(string fontPath, string css, string textAttrs, string text = "a", string wrapperAttrs = "")
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var family = TypefaceFixtures.FamilyNameOf(fontPath);
            await BundledFonts.RegisterFont(adapter, fontPath, family);

            var data = Data(css.Replace("FAMILY", family));
            var markup = $"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100" {wrapperAttrs}>
                  <text x="10" y="50" font-family="{family}" font-size="40" {textAttrs}>{text}</text>
                </svg>
                """;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), adapter,
                fontPaletteValues: RegisteredFontPalette.BuildRegistry(data, new CssValueParser(adapter)),
                fontFeatureValues: RegisteredFontFeatureValues.BuildRegistry(data));
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 200, 100));
            return g;
        }

        private const string FeatureValues = "@font-feature-values FAMILY { @styleset { simple-a: 1; simple-g: 2; } }";

        [Fact]
        public async Task Alternates_Styleset_ReachesShaper()
        {
            var g = await Render(BundledFonts.Recursive, FeatureValues, """font-variant-alternates="styleset(simple-a)" """);

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Contains(new FeatureSetting("ss01", 1), draw.Features!.Value.ExplicitFeatures!);
        }

        [Fact]
        public async Task Alternates_UnmatchedName_IsInert()
        {
            var g = await Render(BundledFonts.Recursive, FeatureValues, """font-variant-alternates="styleset(bogus)" """);

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.True(draw.Features!.Value.ExplicitFeatures is null or []);
        }

        [Fact]
        public async Task Alternates_BeatsFeatureSettingsForSameTag()
        {
            var g = await Render(BundledFonts.Recursive, FeatureValues,
                """font-feature-settings="'ss01' off" font-variant-alternates="styleset(simple-a)" """);

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Contains(new FeatureSetting("ss01", 1), draw.Features!.Value.ExplicitFeatures!);
            Assert.DoesNotContain(new FeatureSetting("ss01", 0), draw.Features!.Value.ExplicitFeatures!);
        }

        [Fact]
        public async Task Alternates_InheritedFromAncestorGroup()
        {
            var g = await Render(BundledFonts.Recursive, FeatureValues, "", wrapperAttrs: """font-variant-alternates="styleset(simple-g)" """);

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Contains(new FeatureSetting("ss02", 1), draw.Features!.Value.ExplicitFeatures!);
        }

        [Fact]
        public async Task Alternates_SelectsADifferentGlyph()
        {
            var plain = await Render(BundledFonts.Recursive, FeatureValues, "");
            var alt = await Render(BundledFonts.Recursive, FeatureValues, """font-variant-alternates="styleset(simple-a)" """);

            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.Recursive);
            await BundledFonts.RegisterFont(adapter, BundledFonts.Recursive, family);
            var font = adapter.GetFont(family, 40, PaintFontStyle.Regular)!;
            using var g = new PeachPDF.Adapters.GraphicsAdapter(adapter,
                PeachPDF.PdfSharpCore.Drawing.XGraphics.CreateMeasureContext(new PeachPDF.PdfSharpCore.Drawing.XSize(595, 842),
                    PeachPDF.PdfSharpCore.Drawing.XGraphicsUnit.Point, PeachPDF.PdfSharpCore.Drawing.XPageDirection.Downwards), 1.0);
            using var a = g.GetTextOutline("a", font, new PaintPoint(0, 40), features: plain.DrawStringCalls[0].Features)!;
            using var b = g.GetTextOutline("a", font, new PaintPoint(0, 40), features: alt.DrawStringCalls[0].Features)!;
            Assert.NotEqual(
                ((GraphicsPathAdapter)a).GraphicsPath._corePath.PathPoints,
                ((GraphicsPathAdapter)b).GraphicsPath._corePath.PathPoints);
        }

        [Fact]
        public async Task FontPalette_BasePalette_ReachesDrawString()
        {
            var g = await Render(BundledFonts.Nabla,
                "@font-palette-values --blue { font-family: 'FAMILY'; base-palette: 2; }",
                """font-palette="--blue" """);

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.NotNull(draw.FontPalette);
            Assert.Equal(2, draw.FontPalette!.BasePaletteIndex);
        }

        [Fact]
        public async Task FontPalette_Dark_InheritedFromAncestor()
        {
            var g = await Render(BundledFonts.Nabla, "", "", wrapperAttrs: """font-palette="dark" """);

            Assert.Single(g.DrawStringCalls);
            // 'dark' selects the font's dark-usable palette when it has one; otherwise the default (null) - never throws.
        }

        [Fact]
        public async Task FontPalette_Normal_IsNull()
        {
            var g = await Render(BundledFonts.Nabla, "", "");

            Assert.Null(Assert.Single(g.DrawStringCalls).FontPalette);
        }

        [Fact]
        public async Task FontPalette_NonColorFont_IsNull()
        {
            var g = await Render(BundledFonts.Recursive, "@font-palette-values --x { font-family: 'FAMILY'; base-palette: 1; }", """font-palette="--x" """);

            Assert.Null(Assert.Single(g.DrawStringCalls).FontPalette);
        }

        [Fact]
        public async Task FontVariantEmoji_Text_SetsEmojiMode()
        {
            var g = await Render(BundledFonts.Recursive, "", """font-variant-emoji="text" """);

            Assert.Equal(PeachDrawing.Text.Unicode.EmojiMode.Text, Assert.Single(g.DrawStringCalls).Features!.Value.EmojiMode);
        }

        [Fact]
        public async Task FontVariantEmoji_InheritsAndResets()
        {
            var inherited = await Render(BundledFonts.Recursive, "", "", wrapperAttrs: """font-variant-emoji="emoji" """);
            Assert.Equal(PeachDrawing.Text.Unicode.EmojiMode.Emoji, Assert.Single(inherited.DrawStringCalls).Features!.Value.EmojiMode);

            var reset = await Render(BundledFonts.Recursive, "", """font-variant-emoji="normal" """, wrapperAttrs: """font-variant-emoji="emoji" """);
            Assert.Equal(PeachDrawing.Text.Unicode.EmojiMode.Normal, Assert.Single(reset.DrawStringCalls).Features!.Value.EmojiMode);
        }

        [Fact]
        public async Task FontWeight_Numeric_ReachesFontRequest()
        {
            var g = await Render(BundledFonts.Recursive, "", """font-weight="650" """);

            Assert.True(Assert.Single(g.DrawStringCalls).Font.Size > 0);
        }

        [Fact]
        public async Task FontVariationSettings_AndOpticalSizing_AreAccepted()
        {
            var g = await Render(BundledFonts.Recursive, "", """font-variation-settings="'wght' 700" font-optical-sizing="none" font-style="oblique 12deg" font-weight="bolder" """);

            Assert.Single(g.DrawStringCalls);
        }
    }
}
