using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A font's SVG documents (OpenType SVG) reaching the PDF as vector artwork: shared between occurrences, following the palette and the
    /// text colour, and falling back to the outline when a document cannot be used.
    /// </summary>
    public class SvgGlyphRenderingIntegrationTests
    {
        private static async Task<string> RenderAsync(string body, string css = "")
        {
            var b64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.SvgTest));
            var html = $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'SvgTest'; src: url('data:font/truetype;base64,{b64}') format('truetype'); }}
@font-palette-values --second {{ font-family: 'SvgTest'; base-palette: 1; }}
body {{ font-family: 'SvgTest'; font-size: 40pt; }}
{css}
</style></head><body>{body}</body></html>";
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            var stream = new MemoryStream();
            doc.Save(stream);
            return Encoding.Latin1.GetString(stream.ToArray());
        }

        private static int Forms(string pdf) => Regex.Matches(pdf, @"/Subtype\s*/Form\b").Count;

        [Fact]
        public async Task ARepeatedSvgGlyph_IsOneFormReferencedEachTime()
        {
            var once = await RenderAsync("<p>A</p>");
            var four = await RenderAsync("<p>AAAA</p>");

            Assert.True(Forms(once) >= 1, "the glyph is drawn from a form");
            Assert.Equal(Forms(once), Forms(four));
        }

        [Fact]
        public async Task TheGlyphIsDrawnInTheDocumentsColours_NotTheTextColour()
        {
            var pdf = await RenderAsync("<p>A</p>", "p { color: #123456 }");

            // The rect is the palette's first colour (1 0 0) and the circle the second (0 0 1); the text colour appears in no fill.
            Assert.Matches(@"(?m)^1 0 0 rg", pdf);
            Assert.Matches(@"(?m)^0 0 1 rg", pdf);
        }

        [Fact]
        public async Task ContextFill_FollowsTheTextColour()
        {
            var black = await RenderAsync("<p>B</p>");
            var green = await RenderAsync("<p>B</p>", "p { color: rgb(0, 128, 0) }");

            Assert.DoesNotContain("0 0.5019", black);
            Assert.Matches(@"0 0\.50\d* 0 rg", green);
        }

        [Fact]
        public async Task ARepeatedSvgGlyph_IsSharedAcrossPages()
        {
            var onePage = await RenderAsync("<p>A</p>");
            var pages = await RenderAsync("<p>A</p><div style=\"break-before: page\"><p>A</p></div><div style=\"break-before: page\"><p>A</p></div>");

            Assert.Equal(Forms(onePage), Forms(pages));
            // Three occurrences (one per page), one form.
            Assert.Equal(3, Regex.Matches(pages, "/ActualText").Count);
        }

        [Fact]
        public async Task AnotherPalette_MakesAnotherForm()
        {
            var pdf = await RenderAsync("<p>A</p><p style=\"font-palette: --second\">A</p>");
            var single = await RenderAsync("<p>A</p>");

            Assert.True(Forms(pdf) > Forms(single));
        }

        [Fact]
        public async Task ADocumentThatCannotBeUsed_FallsBackToTheOutline_WithoutFailing()
        {
            // E's document inflates past the limit, so the reader refuses it and the glyph is the font's plain outline.
            var pdf = await RenderAsync("<p>E</p>");

            Assert.DoesNotContain("1 0 0 rg", pdf);
            Assert.DoesNotContain("0 0 1 rg", pdf);
            Assert.Contains("%%EOF", pdf);
        }

        [Fact]
        public async Task TheTextIsStillSelectable_WithItsActualText()
        {
            var pdf = await RenderAsync("<p>AB</p>");

            Assert.Contains("/ActualText", pdf);
        }
    }
}
