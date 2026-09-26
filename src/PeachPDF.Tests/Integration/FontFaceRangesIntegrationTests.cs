using PeachDrawing.Text;
using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.CSS;
using PeachPDF.Tests.TestSupport;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The range form of the <c>@font-face</c> descriptors (<c>font-weight: 100 900</c>, <c>font-stretch: 75% 125%</c>,
    /// <c>font-style: oblique 0deg 14deg</c>) and the <c>font-stretch</c> percentage, from the stylesheet to the axes of the variable font
    /// the box is set in: the face covers its range in matching, the weight, width and slant of the box set the axes inside the range, and
    /// what an axis supplies is not faked as well.
    /// </summary>
    public class FontFaceRangesIntegrationTests : CssConstructionFunctions
    {
        private static string Base64(string path) => Convert.ToBase64String(File.ReadAllBytes(path));

        private static string FontFace(string family, string path, string descriptors) =>
            $"@font-face {{ font-family: '{family}'; src: url('data:font/truetype;base64,{Base64(path)}') format('truetype'); {descriptors} }}";

        private static string Document(string css, string body) =>
            $"<!DOCTYPE html><html><head><style>{css} body {{ font-size: 24pt; margin: 0; }}</style></head><body>{body}</body></html>";

        private static async Task<CssBox> BuildBoxTree(string html)
        {
            var adapter = new PdfSharpAdapter();
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

        private static CssBox FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true && boxId == id)
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindByIdOrNull(child, id);
                if (found is not null)
                    return found;
            }

            throw new InvalidOperationException("No box with the id " + id);
        }

        private static CssBox? FindByIdOrNull(CssBox box, string id)
        {
            try
            {
                return FindById(box, id);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>The location in the design space that a box's font was matched at.</summary>
        private static double Axis(CssBox box, string tag) =>
            ((FontAdapter)box.ActualFont).Font.Typeface.AxisSettings.Single(s => s.Tag == tag).Value;

        private static async Task<(string Pdf, PeachPdfDocument Document)> RenderAsync(string html)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            var stream = new MemoryStream();
            doc.Save(stream);
            return (Encoding.Latin1.GetString(stream.ToArray()), doc);
        }

        private static int EmbeddedFonts(string pdf) => Regex.Matches(pdf, "/FontFile2").Count;

        private const string Weights = "font-weight: 300 600;";

        // ---- the descriptors are read as ranges -----------------------------------------------------------------------------------

        [Fact]
        public void TheFontFaceRule_KeepsTheRangesAsWritten()
        {
            var sheet = ParseStyleSheet(
                "@font-face { font-family: F; src: url(a.ttf); font-weight: 100 900; font-stretch: 75% 125%; font-style: oblique 0deg 14deg; }");
            var rule = Assert.IsAssignableFrom<IFontFaceRule>(sheet.Rules[0]);

            Assert.Equal("100 900", rule.Weight);
            Assert.Equal("75% 125%", rule.Stretch);
            Assert.Equal("oblique 0deg 14deg", rule.Style);
        }

        [Theory]
        [InlineData("87.5%")]
        [InlineData("0%")]
        [InlineData("250%")]
        [InlineData("condensed")]
        public void FontStretchTheProperty_TakesAPercentage(string value)
        {
            Assert.True(ParseDeclaration("font-stretch: " + value).HasValue);
        }

        [Theory]
        [InlineData("-5%")]
        [InlineData("75% 125%")]
        [InlineData("87.5")]
        [InlineData("wide")]
        public void FontStretchTheProperty_RejectsWhatIsNotAKeywordOrAPercentage(string value)
        {
            Assert.False(ParseDeclaration("font-stretch: " + value).HasValue);
        }

        [Fact]
        public void TheFontShorthand_StillTakesOnlyTheKeywords()
        {
            Assert.True(ParseDeclaration("font: condensed 12px serif").HasValue);
            Assert.False(ParseDeclaration("font: 87.5% 12px serif").HasValue);
        }

        // ---- a weight range keeps the weight axis inside it -----------------------------------------------------------------------

        [Theory]
        [InlineData(100, 300)]
        [InlineData(450, 450)]
        [InlineData(900, 600)]
        public async Task AWeightRange_SetsTheWeightAxisInsideTheRange(int requested, double expected)
        {
            var html = Document(FontFace("VF", BundledFonts.VariableTest, Weights) + " #el { font-family: VF; font-weight: " + requested + "; }",
                "<p id=\"el\">ABAB</p>");

            var el = FindById(await BuildBoxTree(html), "el");

            Assert.Equal(expected, Axis(el, "wght"));
        }

        [Fact]
        public async Task AWeightRange_IsWhatSeparatesTwoRulesOfOneFamily()
        {
            var css = FontFace("VF", BundledFonts.VariableTest, "font-weight: 100 300;") + FontFace("VF", BundledFonts.VariableTest, "font-weight: 600 900;")
                      + " p { font-family: VF; }";
            var html = Document(css,
                "<p id=\"a\" style=\"font-weight: 200\">ABAB</p><p id=\"b\" style=\"font-weight: 800\">ABAB</p><p id=\"c\" style=\"font-weight: 500\">ABAB</p>");

            var root = await BuildBoxTree(html);

            Assert.Equal(200, Axis(FindById(root, "a"), "wght"));
            Assert.Equal(800, Axis(FindById(root, "b"), "wght"));
            // 500 is between the two ranges: the search goes up to 500, then down, so the lighter range wins and holds it at its upper end.
            Assert.Equal(300, Axis(FindById(root, "c"), "wght"));
        }

        [Fact]
        public async Task AVariableFontWithNoDescriptors_CoversTheRangeOfItsAxes()
        {
            var css = FontFace("VF", BundledFonts.VariableTest, "") + " #el { font-family: VF; font-weight: 250; }";

            var el = FindById(await BuildBoxTree(Document(css, "<p id=\"el\">ABAB</p>")), "el");

            Assert.Equal(250, Axis(el, "wght"));
        }

        [Fact]
        public async Task ADescriptorThatCannotBeRead_IsAuto_AndTheAxesAreCovered()
        {
            var css = FontFace("VF", BundledFonts.VariableTest, "font-weight: bolder; font-stretch: wide;") + " #el { font-family: VF; font-weight: 250; }";

            var el = FindById(await BuildBoxTree(Document(css, "<p id=\"el\">ABAB</p>")), "el");

            Assert.Equal(250, Axis(el, "wght"));
        }

        [Fact]
        public async Task ASingleWeightDescriptor_HoldsTheAxisAtThatWeight()
        {
            var css = FontFace("VF", BundledFonts.VariableTest, "font-weight: 700;") + " #el { font-family: VF; font-weight: 400; }";

            var el = FindById(await BuildBoxTree(Document(css, "<p id=\"el\">ABAB</p>")), "el");

            Assert.Equal(700, Axis(el, "wght"));
        }

        [Fact]
        public async Task FontVariationSettings_StillWinOverTheRange()
        {
            var css = FontFace("VF", BundledFonts.VariableTest, Weights) + " #el { font-family: VF; font-weight: 400; font-variation-settings: 'wght' 900; }";

            var el = FindById(await BuildBoxTree(Document(css, "<p id=\"el\">ABAB</p>")), "el");

            Assert.Equal(900, Axis(el, "wght"));
        }

        // ---- a width range, and the font-stretch percentage -----------------------------------------------------------------------

        [Theory]
        [InlineData("50%", 75)]      // wdth 75 is the axis minimum
        [InlineData("87.5%", 87.5)]
        [InlineData("semi-condensed", 87.5)]
        [InlineData("110%", 110)]
        [InlineData("300%", 125)]    // the axis maximum
        public async Task FontStretchAsAPercentage_SetsTheWidthAxis(string stretch, double expected)
        {
            var css = FontFace("VF", BundledFonts.VariableTest, "") + " #el { font-family: VF; font-stretch: " + stretch + "; }";

            var el = FindById(await BuildBoxTree(Document(css, "<p id=\"el\">ABAB</p>")), "el");

            Assert.Equal(expected, Axis(el, "wdth"));
        }

        [Theory]
        [InlineData("50%", 90)]
        [InlineData("95%", 95)]
        [InlineData("125%", 110)]
        public async Task AWidthRange_SetsTheWidthAxisInsideTheRange(string stretch, double expected)
        {
            var css = FontFace("VF", BundledFonts.VariableTest, "font-stretch: 90% 110%;") + " #el { font-family: VF; font-stretch: " + stretch + "; }";

            var el = FindById(await BuildBoxTree(Document(css, "<p id=\"el\">ABAB</p>")), "el");

            Assert.Equal(expected, Axis(el, "wdth"));
        }

        [Fact]
        public async Task TheWidthOfTheText_FollowsTheStretchPercentage()
        {
            // The width axis makes the glyphs and their advances wider, so the same text takes more room as the percentage grows.
            var css = FontFace("VF", BundledFonts.VariableTest, "") + " div { font-family: VF; float: left; clear: both; }";
            var html = Document(css,
                "<div id=\"n\" style=\"font-stretch: 75%\">ABAB</div><div id=\"m\" style=\"font-stretch: 100%\">ABAB</div>"
                + "<div id=\"w\" style=\"font-stretch: 125%\">ABAB</div>");

            var root = await BuildBoxTree(html);
            double Width(string id)
            {
                var box = FindById(root, id);
                return box.ActualRight - box.Location.X;
            }

            Assert.True(Width("n") < Width("m"), $"75% ({Width("n")}) should be narrower than 100% ({Width("m")})");
            Assert.True(Width("m") < Width("w"), $"100% ({Width("m")}) should be narrower than 125% ({Width("w")})");
        }

        // ---- an oblique range keeps the slant axis inside it ----------------------------------------------------------------------

        [Theory]
        [InlineData("italic", -10)]              // 14 degrees, kept inside 0 to 10
        [InlineData("oblique 5deg", -5)]
        [InlineData("oblique 25deg", -10)]
        [InlineData("normal", 0)]                // the range includes 0, so upright text is served at no slant
        public async Task AnObliqueRange_SetsTheSlantAxisInsideTheRange(string style, double expected)
        {
            var css = FontFace("VS", BundledFonts.VariableSlantTest, "font-style: oblique 0deg 10deg;") + " #el { font-family: VS; font-style: " + style + "; }";

            var el = FindById(await BuildBoxTree(Document(css, "<p id=\"el\">AHIH</p>")), "el");

            Assert.Equal(expected, Axis(el, "slnt"));
        }

        [Theory]
        [InlineData("italic", -14)]
        [InlineData("oblique 8deg", -8)]
        [InlineData("oblique 40deg", -15)]      // the axis minimum
        public async Task ASlantAxisWithNoDescriptor_IsCoveredAsAnObliqueRange(string style, double expected)
        {
            var css = FontFace("VS", BundledFonts.VariableSlantTest, "") + " #el { font-family: VS; font-style: " + style + "; }";

            var el = FindById(await BuildBoxTree(Document(css, "<p id=\"el\">AHIH</p>")), "el");

            Assert.Equal(expected, Axis(el, "slnt"));
        }

        // ---- nothing is faked that an axis supplies -------------------------------------------------------------------------------

        [Fact]
        public async Task ABoldRequest_ThatTheRangeReaches_IsNotFakedBold()
        {
            var css = FontFace("VF", BundledFonts.VariableTest, "font-weight: 100 900;") + " p { font-family: VF; font-weight: bold; }";

            var (pdf, _) = await RenderAsync(Document(css, "<p>ABAB</p>"));

            Assert.DoesNotContain("2 Tr", pdf);
        }

        [Fact]
        public async Task ABoldRequest_ThatTheRangeCannotReach_IsFakedBold()
        {
            var css = FontFace("VF", BundledFonts.VariableTest, "font-weight: 300 500;") + " p { font-family: VF; font-weight: bold; }";

            var (pdf, _) = await RenderAsync(Document(css, "<p>ABAB</p>"));

            Assert.Contains("2 Tr", pdf);
        }

        [Fact]
        public async Task AnItalicRequest_OnAFontWithASlantAxis_IsNotShearedAsWell()
        {
            var variable = FontFace("VS", BundledFonts.VariableSlantTest, "font-style: oblique 0deg 15deg;") + " p { font-family: VS; font-style: italic; }";
            var fixedFont = FontFace("VS", BundledFonts.Ttf, "") + " p { font-family: VS; font-style: italic; }";

            var (withAxis, _) = await RenderAsync(Document(variable, "<p>AHIH</p>"));
            var (faked, _) = await RenderAsync(Document(fixedFont, "<p>AHIH</p>"));

            // The faux italic is a text matrix with a shear term (sin 20 degrees); the slant axis draws the lean itself, so it is not applied too.
            var shear = Math.Sin(20.0 * Math.PI / 180.0).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains(shear, faked);
            Assert.DoesNotContain(shear, withAxis);
            Assert.Equal(1, EmbeddedFonts(withAxis));
        }

        // ---- the font caches are keyed on the location ----------------------------------------------------------------------------

        [Fact]
        public async Task TheSameLocation_IsEmbeddedOnce_AndADifferentOneIsEmbeddedAgain()
        {
            var css = FontFace("VF", BundledFonts.VariableTest, "font-weight: 100 900;") + " p { font-family: VF; }";

            var (same, _) = await RenderAsync(Document(css, "<p style=\"font-weight: 350\">ABAB</p><p style=\"font-weight: 350\">ABAB</p>"));
            var (different, _) = await RenderAsync(Document(css, "<p style=\"font-weight: 350\">ABAB</p><p style=\"font-weight: 360\">ABAB</p>"));
            var (byWidth, _) = await RenderAsync(Document(css, "<p style=\"font-stretch: 90%\">ABAB</p><p style=\"font-stretch: 91%\">ABAB</p>"));

            Assert.Equal(1, EmbeddedFonts(same));
            Assert.Equal(2, EmbeddedFonts(different));
            Assert.Equal(2, EmbeddedFonts(byWidth));
        }

        [Fact]
        public async Task TwoRegistrationsOfOneFile_WithDifferentRanges_AreKeptApartByTheCaches()
        {
            var css = FontFace("VF", BundledFonts.VariableTest, "font-weight: 100 300;") + FontFace("VF", BundledFonts.VariableTest, "font-weight: 700 900;")
                      + " p { font-family: VF; }";
            var html = Document(css, "<p id=\"a\" style=\"font-weight: 100\">A</p><p id=\"b\" style=\"font-weight: 900\">A</p><p id=\"c\" style=\"font-weight: 100\">A</p>");

            var root = await BuildBoxTree(html);

            Assert.Equal(100, Axis(FindById(root, "a"), "wght"));
            Assert.Equal(900, Axis(FindById(root, "b"), "wght"));
            Assert.Same(FindById(root, "a").ActualFont, FindById(root, "c").ActualFont);
        }
    }
}
