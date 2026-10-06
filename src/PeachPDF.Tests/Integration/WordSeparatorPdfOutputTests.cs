using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// How a word separator reaches the PDF: shown straight after the word before it, from where that
    /// word left the pen, so the following word's <c>Td</c> - relative to the text line, which showing
    /// text does not move - is the one written without separators; and mapped to U+0020 even where the
    /// font draws U+00A0 with the same glyph. The painter-level placement is covered by
    /// <see cref="WordSeparatorGlyphTests"/>.
    /// </summary>
    public class WordSeparatorPdfOutputTests
    {
        // Liberation Sans puts the space at glyph 3.
        private const string SpaceShow = "<0003> Tj";

        private static readonly Regex TextLineMove = new(@"-?[\d.]+ -?[\d.]+ Td", RegexOptions.Compiled);

        [Fact]
        public async Task Separator_IsShownStraightAfterThePreviousWord_WithoutATdOfItsOwn()
        {
            var pdf = await RenderAsync("<p>Premium Widget costs</p>");

            Assert.Equal(2, Count(pdf, SpaceShow));
            Assert.Equal(3, TextLineMove.Matches(pdf).Count);
            Assert.Matches(@"Td <[0-9A-F]+> Tj\n<0003> Tj\n-?[\d.]+ -?[\d.]+ Td <[0-9A-F]+> Tj\n<0003> Tj\n", pdf);
        }

        [Fact]
        public async Task JustifiedLines_ShowEverySeparatorAtThePen()
        {
            // The widened gap is all after the space, so the space still starts where the word ends.
            var pdf = await RenderAsync(
                """<p style="width:150pt; text-align:justify">Pack my box with five dozen liquor jugs before noon today, please.</p>""");

            // Three lines - "Pack my box with five", "dozen liquor jugs before", "noon today, please." -
            // and no separator in front of the word that opens one.
            Assert.Equal(9, Count(pdf, SpaceShow));
            Assert.DoesNotContain("Td " + SpaceShow, pdf);
        }

        [Fact]
        public async Task SeparatorAfterAnInlineBackground_IsPositionedOnItsOwn()
        {
            // The span's background is drawn between "a" and the separator in front of "b", so the
            // pen "a" left is no longer the current text position to continue from.
            var pdf = await RenderAsync("""<p>a <span style="background:yellow">b</span></p>""");

            Assert.Equal(1, Count(pdf, SpaceShow));
            Assert.Contains("Td " + SpaceShow, pdf);
        }

        [Fact]
        public async Task SeparatorAfterAHiddenWord_IsPositionedOnItsOwn()
        {
            // "b" draws nothing, so "a" left the pen in the a|b gap; the space in front of "c" belongs in
            // the b|c gap and must not continue from there.
            var pdf = await RenderAsync("""<p>a <span style="visibility:hidden">bbbb</span> c</p>""");

            Assert.Equal(1, Count(pdf, SpaceShow));
            Assert.Contains("Td " + SpaceShow, pdf);
        }

        [Fact]
        public async Task SeparatorAfterASyntheticItalicSwitch_StaysAtThePen()
        {
            // The first word in and the first word out of synthetic italic each set the text matrix with
            // Tm rather than Td; the pen they leave is still continued from. Source Sans 3 has no italic
            // face anywhere, so the italic is synthesized (its space is glyph 1).
            var pdf = await RenderAsync("<p>a <em>b c</em> d e</p>", BundledFonts.Ttf);

            Assert.Equal(4, Count(pdf, "<0001> Tj"));
            Assert.DoesNotContain("Td <0001> Tj", pdf);
            Assert.Equal(2, Regex.Matches(pdf, @"Tm\n<[0-9A-F]+> Tj\n<0001> Tj\n").Count);
        }

        [Fact]
        public async Task SeparatorBeforeARaisedWord_IsPositionedOnItsOwn()
        {
            // Half an em up is further than a separator may sit from the pen's baseline, so it gets its
            // own position on the raised word's baseline.
            var pdf = await RenderAsync("""<p>a <span style="vertical-align:6pt">b</span></p>""");

            Assert.Contains("Td " + SpaceShow, pdf);
        }

        [Fact]
        public async Task SeparatorBeforeASlightlyRaisedWord_StaysAtThePen()
        {
            // vertical-align: super lifts "b" less than a quarter of an em; an inkless glyph that close
            // still reads as part of the line, and continuing the pen keeps the next Td unchanged.
            var pdf = await RenderAsync("""<p>a <span style="vertical-align:super">b</span></p>""");

            Assert.Matches(@"Td <0044> Tj\n<0003> Tj\n", pdf);
        }

        [Fact]
        public async Task KernedWord_LeavesThePenWhereItsSeparatorStarts()
        {
            // "AV" kerns, so "AVA" is drawn glyph by glyph; the separator after it continues from the
            // last of those glyphs.
            var pdf = await RenderAsync("<p>AVA bb</p>");

            Assert.True(TextLineMove.Matches(pdf).Count > 2, "the kerned word must be drawn glyph by glyph");
            Assert.Matches(@"Td <[0-9A-F]{4}> Tj\n<0003> Tj\n", pdf);
            Assert.DoesNotContain("Td " + SpaceShow, pdf);
        }

        [Theory]
        // the no-break space drawn after the ordinary spaces...
        [InlineData("<p>first second third&nbsp;fourth fifth</p><p>sixth seventh&nbsp;eighth</p>")]
        // ...and before them
        [InlineData("<p>first&nbsp;second third fourth fifth</p><p>sixth seventh eighth</p>")]
        public async Task NoBreakSpaceSharingTheSpaceGlyph_LeavesWordBreaksMappedToSpace(string body)
        {
            // Source Sans 3 draws U+00A0 with its space glyph (glyph 1). That glyph has one ToUnicode
            // destination, and every word separator is shown with it: were it U+00A0, every word break
            // of the document would extract as one.
            var pdf = await RenderAsync(body, BundledFonts.Ttf);

            var toUnicode = Regex.Match(pdf, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline).Groups[1].Value;
            Assert.Contains("<0001><0001><0020>", toUnicode);
            Assert.DoesNotContain("<00A0>", toUnicode);
            // Four word separators and two no-break spaces, all the one glyph.
            Assert.Equal(6, Count(pdf, "<0001> Tj"));
        }

        [Fact]
        public async Task TaggedPdf_ShowsEverySeparatorInsideMarkedContent()
        {
            var pdf = await RenderAsync("<p>a b</p><p>c <em>d</em> e</p>", tagged: true);

            // a␠b, c␠d and d␠e.
            Assert.Equal(3, Count(pdf, SpaceShow));
            var depth = 0;
            foreach (var line in Content(pdf).Split('\n'))
            {
                if (line.EndsWith("BDC")) depth++;
                else if (line == "EMC") depth--;
                else if (line.EndsWith(" Tj")) Assert.True(depth > 0, $"'{line}' is outside marked content");
            }

            Assert.Equal(0, depth);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<string> RenderAsync(string body, string? fontPath = null, bool tagged = false)
        {
            fontPath ??= BundledFonts.LiberationSans;
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(fontPath))
                await generator.AddFontFromStream(stream);
            var family = TypefaceFixtures.FamilyNameOf(fontPath);

            var html = $"<!DOCTYPE html><html><head><style>body {{ font-family: '{family}'; font-size: 12pt }}</style></head><body>{body}</body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false, EnableTaggedPdf = tagged };
            using var output = new MemoryStream();
            (await generator.GeneratePdf(html, config)).Save(output);
            return Encoding.Latin1.GetString(output.ToArray());
        }

        /// <summary>Every page content stream holding text, joined.</summary>
        private static string Content(string pdf) =>
            string.Join("\n", Regex.Matches(pdf, @"stream\r?\n(.*?)endstream", RegexOptions.Singleline)
                .Select(m => m.Groups[1].Value)
                .Where(s => s.Contains("BT\n")));

        private static int Count(string text, string token) => Regex.Matches(text, Regex.Escape(token)).Count;
    }
}
