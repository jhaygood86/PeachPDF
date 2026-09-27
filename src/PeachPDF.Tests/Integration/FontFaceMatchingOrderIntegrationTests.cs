using PeachDrawing.Text;
using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.IO;
using System.Text;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// How a family's faces are narrowed for a box (CSS Fonts 4 section 5.2), from the stylesheet to the face the box is set in: by width,
    /// then by style, then by weight; among faces that declare an oblique range, by the requested angle; and with a <c>font-weight</c> that is
    /// not a whole number. Every fixture is an <c>@font-face</c> of a bundled font, so what matches does not depend on the machine's fonts.
    /// </summary>
    public class FontFaceMatchingOrderIntegrationTests
    {
        private static string Base64(string path) => Convert.ToBase64String(File.ReadAllBytes(path));

        private static string FontFace(string family, string path, string descriptors, string format = "truetype") =>
            $"@font-face {{ font-family: '{family}'; src: url('data:font/{format};base64,{Base64(path)}') format('{format}'); {descriptors} }}";

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

        private static CssBox? Find(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true && boxId == id)
                return box;

            foreach (var child in box.Boxes)
            {
                if (Find(child, id) is { } found)
                    return found;
            }

            return null;
        }

        private static CssBox FindById(CssBox root, string id) => Find(root, id) ?? throw new InvalidOperationException("No box with the id " + id);

        private static Typeface TypefaceOf(CssBox box) => ((FontAdapter)box.ActualFont).Font.Typeface;

        private static double Axis(CssBox box, string tag) => TypefaceOf(box).AxisSettings.Single(s => s.Tag == tag).Value;

        /// <summary>The full name a bundled font reports, which tells the faces of a family apart.</summary>
        private static string NameOf(string path)
        {
            var family = new FontSet().AddFile(path, new AddOptions { FamilyName = "Name-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface.FullName;
        }

        private static async Task<string> RenderAsync(string html)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            var stream = new MemoryStream();
            doc.Save(stream);
            return Encoding.Latin1.GetString(stream.ToArray());
        }

        // ---- width first, then style ------------------------------------------------------------------------------------------------

        private static string CondensedUprightAndNormalItalic() =>
            FontFace("M", BundledFonts.Ttf, "font-stretch: 75%; font-style: normal;")
            + FontFace("M", BundledFonts.Otf, "font-stretch: 100%; font-style: italic;", "opentype")
            + " p { font-family: M; }";

        [Fact]
        public async Task ACondensedItalicBox_IsSetInTheCondensedFace_NotInTheItalicFaceOfNormalWidth()
        {
            var html = Document(CondensedUprightAndNormalItalic(),
                "<p id=\"a\" style=\"font-stretch: condensed; font-style: italic\">AB</p>"
                + "<p id=\"b\" style=\"font-stretch: normal; font-style: italic\">AB</p>"
                + "<p id=\"c\" style=\"font-stretch: condensed\">AB</p>"
                + "<p id=\"d\">AB</p>");

            var root = await BuildBoxTree(html);

            var condensed = NameOf(BundledFonts.Ttf);
            var italic = NameOf(BundledFonts.Otf);
            Assert.Equal(condensed, TypefaceOf(FindById(root, "a")).FullName);
            Assert.Equal(italic, TypefaceOf(FindById(root, "b")).FullName);
            Assert.Equal(condensed, TypefaceOf(FindById(root, "c")).FullName);
            // Upright text of normal width: the width leaves only the italic face.
            Assert.Equal(italic, TypefaceOf(FindById(root, "d")).FullName);
        }

        [Fact]
        public async Task TheLeanOfACondensedItalicBox_IsFaked_BecauseTheCondensedFaceIsUpright()
        {
            var html = Document(CondensedUprightAndNormalItalic(),
                "<p id=\"a\" style=\"font-stretch: condensed; font-style: italic\">AB</p><p id=\"b\" style=\"font-style: italic\">AB</p>"
                + "<p id=\"c\" style=\"font-stretch: condensed\">AB</p>");

            var root = await BuildBoxTree(html);

            Assert.True(((FontAdapter)FindById(root, "a").ActualFont).Font.Synthesis.HasFlag(SyntheticStyle.Italic));
            Assert.False(((FontAdapter)FindById(root, "b").ActualFont).Font.Synthesis.HasFlag(SyntheticStyle.Italic));
            Assert.Equal(SyntheticStyle.None, ((FontAdapter)FindById(root, "c").ActualFont).Font.Synthesis);
        }

        // ---- the requested angle chooses among oblique ranges -----------------------------------------------------------------------

        private static string TwoObliqueRanges() =>
            FontFace("VS", BundledFonts.VariableSlantTest, "font-style: oblique 0deg 5deg;")
            + FontFace("VS", BundledFonts.VariableSlantTest, "font-style: oblique 10deg 15deg;")
            + " p { font-family: VS; }";

        [Theory]
        [InlineData("oblique 3deg", -3)]      // held by the first range, which was declared first
        [InlineData("oblique 10deg", -10)]    // held by the second range at its lower end: the angle is not lost to rounding on the way
        [InlineData("oblique 11deg", -11)]    // 11 degrees is on the far side of the threshold: the ranges above are searched first
        [InlineData("oblique 8deg", -5)]      // below 11 degrees the range below is searched first: the first, held at its upper end
        [InlineData("oblique 12deg", -12)]    // held by the second
        [InlineData("oblique 25deg", -15)]    // above everything: the second, held at its upper end
        [InlineData("italic", -14)]           // compared as 11 degrees: the second, and 14 is what italic sets the slant to
        public async Task TheRequestedAngle_ChoosesTheObliqueRangeNearestToIt(string style, double expectedSlant)
        {
            var html = Document(TwoObliqueRanges(), $"<p id=\"el\" style=\"font-style: {style}\">AHIH</p>");

            var el = FindById(await BuildBoxTree(html), "el");

            Assert.Equal(expectedSlant, Axis(el, "slnt"), 6);
        }

        [Fact]
        public async Task ADifferentAngle_IsADifferentTypeface_WhereTheAngleChoosesTheFace()
        {
            // Both boxes are set in the same family and weight, so a typeface cache keyed without the angle would serve the first box's face
            // (and slant) to the second.
            var html = Document(TwoObliqueRanges(),
                "<p id=\"a\" style=\"font-style: oblique 3deg\">AHIH</p><p id=\"b\" style=\"font-style: oblique 12deg\">AHIH</p>"
                + "<p id=\"c\" style=\"font-style: oblique 3deg\">AHIH</p>");

            var root = await BuildBoxTree(html);

            Assert.Equal(-3, Axis(FindById(root, "a"), "slnt"), 6);
            Assert.Equal(-12, Axis(FindById(root, "b"), "slnt"), 6);
            Assert.Same(FindById(root, "a").ActualFont, FindById(root, "c").ActualFont);
        }

        // ---- fractional font-weight -------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData("350.5")]
        [InlineData("399.999")]
        [InlineData("1.5")]
        [InlineData("999.9")]
        public async Task AFractionalFontWeight_IsKeptAsWritten(string weight)
        {
            var html = Document("", $"<p id=\"el\" style=\"font-weight: {weight}\">AB</p>");

            var el = FindById(await BuildBoxTree(html), "el");

            var expected = double.Parse(weight, System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(el.FontWeight.Value is { IsValue: true } stored && stored.Value == expected);
            Assert.Equal(expected, el.ActualNumericWeight);
        }

        [Theory]
        [InlineData("0.5")]
        [InlineData("1000.5")]
        [InlineData("-3.5")]
        public async Task AFractionalFontWeightOutsideOneToAThousand_IsRejected(string weight)
        {
            var html = Document("", $"<div style=\"font-weight: 700\"><p id=\"el\" style=\"font-weight: {weight}\">AB</p></div>");

            var el = FindById(await BuildBoxTree(html), "el");

            Assert.Equal(700, el.ActualNumericWeight);
        }

        [Fact]
        public async Task ACalcThatIsAPlainNumber_IsAFontWeight_WithItsFraction()
        {
            var html = Document("", "<p id=\"el\" style=\"font-weight: calc(300 + 50.5)\">AB</p>");

            var el = FindById(await BuildBoxTree(html), "el");

            Assert.Equal(350.5, el.ActualNumericWeight);
        }

        [Fact]
        public async Task TheFontShorthand_TakesAFractionalWeight()
        {
            var html = Document("", "<p id=\"el\" style=\"font: 350.5 12pt serif\">AB</p>");

            var el = FindById(await BuildBoxTree(html), "el");

            Assert.Equal(350.5, el.ActualNumericWeight);
        }

        [Fact]
        public async Task AFractionalWeightInherits_AndBolderAndLighterStepFromIt()
        {
            var html = Document("",
                "<div style=\"font-weight: 450.5\"><p id=\"same\">AB</p><p id=\"up\" style=\"font-weight: bolder\">AB</p></div>"
                + "<div style=\"font-weight: 550.5\"><p id=\"down\" style=\"font-weight: lighter\">AB</p></div>");

            var root = await BuildBoxTree(html);

            Assert.Equal(450.5, FindById(root, "same").ActualNumericWeight);
            Assert.Equal(700, FindById(root, "up").ActualNumericWeight);      // from a weight of 400 to 500: 700
            Assert.Equal(400, FindById(root, "down").ActualNumericWeight);    // from 500 to 700: 400
        }

        [Fact]
        public async Task AFractionalWeight_SetsTheWeightAxisOfAVariableFontToIt()
        {
            var css = FontFace("VF", BundledFonts.VariableTest, "") + " p { font-family: VF; }";
            var html = Document(css,
                "<p id=\"a\" style=\"font-weight: 350.5\">AB</p><p id=\"b\" style=\"font-weight: 350\">AB</p><p id=\"c\" style=\"font-weight: 350.5\">AB</p>");

            var root = await BuildBoxTree(html);

            Assert.Equal(350.5, Axis(FindById(root, "a"), "wght"));
            Assert.Equal(350, Axis(FindById(root, "b"), "wght"));
            // The font caches are keyed on the weight itself, so 350.5 and 350 are two fonts and the same weight is one.
            Assert.NotSame(FindById(root, "a").ActualFont, FindById(root, "b").ActualFont);
            Assert.Same(FindById(root, "a").ActualFont, FindById(root, "c").ActualFont);
        }

        [Fact]
        public async Task AFractionalWeight_ChoosesTheFaceWhoseRangeHoldsIt_AndOnlyIt()
        {
            // 350.5 is inside the first face's range; the whole weight 350 next to it is not, and takes the nearest below.
            var css = FontFace("W", BundledFonts.Ttf, "font-weight: 350.2 350.8;") + FontFace("W", BundledFonts.Otf, "font-weight: 100 300;", "opentype")
                      + " p { font-family: W; }";
            var html = Document(css, "<p id=\"a\" style=\"font-weight: 350.5\">AB</p><p id=\"b\" style=\"font-weight: 350\">AB</p>");

            var root = await BuildBoxTree(html);

            Assert.Equal(NameOf(BundledFonts.Ttf), TypefaceOf(FindById(root, "a")).FullName);
            Assert.Equal(NameOf(BundledFonts.Otf), TypefaceOf(FindById(root, "b")).FullName);
        }

        [Fact]
        public async Task AFractionalWeight_IsEmbeddedAsItsOwnInstance()
        {
            var css = FontFace("VF", BundledFonts.VariableTest, "font-weight: 100 900;") + " p { font-family: VF; }";

            var same = await RenderAsync(Document(css, "<p style=\"font-weight: 350.5\">ABAB</p><p style=\"font-weight: 350.5\">ABAB</p>"));
            var different = await RenderAsync(Document(css, "<p style=\"font-weight: 350.5\">ABAB</p><p style=\"font-weight: 350.6\">ABAB</p>"));

            Assert.Single(System.Text.RegularExpressions.Regex.Matches(same, "/FontFile2"));
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(different, "/FontFile2").Count);
        }
    }
}
