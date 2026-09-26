using PeachDrawing.Text;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.CSS;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>font-variation-settings</c>, <c>font-optical-sizing</c> and the weight and width of a box reaching the axes of a variable font, from
    /// the property grammar to the fonts embedded in the PDF.
    /// </summary>
    public class VariableFontIntegrationTests : CssConstructionFunctions
    {
        // ---- the properties -------------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData("normal")]
        [InlineData("\"wght\" 650")]
        [InlineData("\"wght\" 650, \"wdth\" 80.5")]
        public void FontVariationSettings_Legal(string value)
        {
            var property = ParseDeclaration("font-variation-settings: " + value);

            Assert.IsType<FontVariationSettingsProperty>(property);
            Assert.True(property.HasValue);
        }

        [Theory]
        [InlineData("wght 650")]
        [InlineData("\"wght\"")]
        [InlineData("\"wght\" bold")]
        [InlineData("650")]
        public void FontVariationSettings_Illegal(string value)
        {
            var property = ParseDeclaration("font-variation-settings: " + value);

            Assert.False(property.HasValue);
        }

        [Theory]
        [InlineData("auto")]
        [InlineData("none")]
        public void FontOpticalSizing_Legal(string value)
        {
            var property = ParseDeclaration("font-optical-sizing: " + value);

            Assert.IsType<FontOpticalSizingProperty>(property);
            Assert.True(property.HasValue);
        }

        [Fact]
        public void FontOpticalSizing_Illegal()
        {
            Assert.False(ParseDeclaration("font-optical-sizing: sometimes").HasValue);
        }

        // ---- the resolver ---------------------------------------------------------------------------------------------------------

        [Fact]
        public void TheInitialValues_EncodeToNull()
        {
            Assert.Null(FontVariationSettingsResolver.Encode(FontOpticalSizingMode.Auto, "normal"));
        }

        [Fact]
        public void Settings_EncodeAndComeBackAsAxes_WithTheOpticalSizeFirst()
        {
            var encoded = FontVariationSettingsResolver.Encode(FontOpticalSizingMode.Auto, "\"wght\" 650, \"wdth\" 80.5");
            Assert.Equal("wght=650;wdth=80.5", encoded);

            var axes = FontVariationSettingsResolver.ToAxes(encoded, 16, null);

            Assert.Equal(
                [new AxisSetting("opsz", 16), new AxisSetting("wght", 650), new AxisSetting("wdth", 80.5)],
                axes);
        }

        [Fact]
        public void SwitchingOpticalSizingOff_LeavesTheOpticalSizeOutOfTheAxes()
        {
            var encoded = FontVariationSettingsResolver.Encode(FontOpticalSizingMode.None, "normal");
            Assert.Equal("-opsz", encoded);

            Assert.Empty(FontVariationSettingsResolver.ToAxes(encoded, 16, null));
        }

        [Fact]
        public void AnObliqueAngle_BecomesANegativeSlant()
        {
            var axes = FontVariationSettingsResolver.ToAxes(null, 16, Math.Sin(10 * Math.PI / 180));

            var slant = Assert.Single(axes, a => a.Tag == "slnt");
            Assert.Equal(-10, slant.Value, 6);
        }

        [Theory]
        [InlineData("\"wgh\" 4")]           // too short
        [InlineData("\"wghts\" 4")]         // too long
        public void AMalformedEntry_IsSkipped(string cascaded)
        {
            Assert.Empty(FontVariationSettingsResolver.ParseSettings(cascaded));
        }

        // ---- the PDF --------------------------------------------------------------------------------------------------------------

        private static string Html(string body, string css = "")
        {
            var b64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.VariableTest));
            return $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'VF'; src: url('data:font/truetype;base64,{b64}') format('truetype'); }}
body {{ font-family: 'VF'; font-size: 24pt; }}
{css}
</style></head><body>{body}</body></html>";
        }

        private static async Task<string> RenderAsync(string html)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            var stream = new MemoryStream();
            doc.Save(stream);
            return Encoding.Latin1.GetString(stream.ToArray());
        }

        private static int EmbeddedFonts(string pdf) => Regex.Matches(pdf, "/FontFile2").Count;

        [Fact]
        public async Task OneWeight_EmbedsOneFont()
        {
            var pdf = await RenderAsync(Html("<p>ABAB</p>"));

            Assert.Equal(1, EmbeddedFonts(pdf));
        }

        [Fact]
        public async Task TwoWeightsOfAVariableFont_EmbedTwoInstances_NotOneFontFakedBold()
        {
            var pdf = await RenderAsync(Html("<p>ABAB</p><p style=\"font-weight: 900\">ABAB</p>"));

            Assert.Equal(2, EmbeddedFonts(pdf));
        }

        [Fact]
        public async Task ABoldInstance_IsNotAlsoFakedBold()
        {
            // Faux bold strokes the glyphs (text render mode 2); the instance is already heavy, so nothing is stroked.
            var pdf = await RenderAsync(Html("<p style=\"font-weight: 700\">ABAB</p>"));

            Assert.DoesNotContain(" 2 Tr", pdf);
        }

        [Fact]
        public async Task FontVariationSettings_PickAnInstanceTheWeightDoesNot()
        {
            var pdf = await RenderAsync(Html("<p>ABAB</p><p style=\"font-variation-settings: 'wght' 900\">ABAB</p>"));

            Assert.Equal(2, EmbeddedFonts(pdf));
        }

        [Fact]
        public async Task TheSameLocation_ReachedTwoWays_IsEmbeddedOnce()
        {
            var pdf = await RenderAsync(Html(
                "<p style=\"font-weight: 900\">ABAB</p><p style=\"font-variation-settings: 'wght' 900\">ABAB</p>"));

            Assert.Equal(1, EmbeddedFonts(pdf));
        }

        [Fact]
        public async Task FontVariationSettings_InheritToChildren()
        {
            var pdf = await RenderAsync(Html("<p>ABAB</p><div style=\"font-variation-settings: 'wght' 900\"><p>ABAB</p></div>"));

            Assert.Equal(2, EmbeddedFonts(pdf));
        }

        [Fact]
        public async Task TheFontShorthand_ResetsFontVariationSettings()
        {
            var pdf = await RenderAsync(Html(
                "<div style=\"font-variation-settings: 'wght' 900\"><p style=\"font: 24pt VF\">ABAB</p></div>"));

            Assert.Equal(1, EmbeddedFonts(pdf));
        }
    }
}
