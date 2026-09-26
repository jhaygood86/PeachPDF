using System.IO;
using System.Text;
using System.Threading.Tasks;
using PeachPDF;
using PeachPDF.Tests.TestSupport;
using Xunit;
using PeachDrawing.Text;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Paint-level proof that the emoji/text presentation choice reaches the PDF: a colour glyph (Noto Color
    /// Emoji) is drawn as vector artwork carrying a per-occurrence <c>/ActualText</c>, while the outline glyph
    /// (Source Sans 3) is an ordinary text show with none. Both fonts cover U+2764, so only the requested
    /// presentation can decide - and the layout-level tests alone would not notice a resolved font that then
    /// failed to paint.
    /// </summary>
    public class FontVariantEmojiRenderingIntegrationTests
    {
        private static async Task<string> Render(string style, string body)
        {
            var colourFamily = TypefaceFixtures.FamilyNameOf(BundledFonts.ColorEmoji);
            var textFamily = TypefaceFixtures.FamilyNameOf(BundledFonts.Ttf);

            var generator = new PdfGenerator();
            foreach (var path in new[] { BundledFonts.ColorEmoji, BundledFonts.Ttf })
            {
                await using var stream = File.OpenRead(path);
                await generator.AddFontFromStream(stream);
            }

            // The colour font comes first, so without a presentation request it always wins.
            var html = "<!DOCTYPE html><html><head><style>" +
                       $"body {{ font-family: '{colourFamily}', '{textFamily}'; font-size: 60pt; color: black; {style} }}" +
                       $"</style></head><body>{body}</body></html>";

            var doc = await generator.GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            using var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task Baseline_TheColourFontComesFirst_SoABareHeartIsDrawnAsColourArtwork()
        {
            var pdf = await Render("", "&#x2764;");

            Assert.Contains("/ActualText <FEFF2764>", pdf);
        }

        [Fact]
        public async Task Fe0e_DrawsTheOutlineHeart_WithNoColourArtworkOrActualText()
        {
            var pdf = await Render("", "&#x2764;&#xFE0E;");

            Assert.DoesNotContain("/ActualText", pdf);
            Assert.Contains(" Tj", pdf);
        }

        [Fact]
        public async Task Fe0f_DrawsTheColourHeart_KeepingTheSelectorInItsActualText()
        {
            var pdf = await Render("", "&#x2764;&#xFE0F;");

            Assert.Contains("/ActualText <FEFF2764FE0F>", pdf);
        }

        [Fact]
        public async Task FontVariantEmojiText_DrawsTheOutlineHeart()
        {
            var pdf = await Render("font-variant-emoji: text;", "&#x2764;");

            Assert.DoesNotContain("/ActualText", pdf);
        }

        [Fact]
        public async Task FontVariantEmojiUnicode_DrawsATextDefaultCharacterAsOutline()
        {
            var pdf = await Render("font-variant-emoji: unicode;", "&#x2764;");

            Assert.DoesNotContain("/ActualText", pdf);
        }

        [Fact]
        public async Task ExplicitFe0f_OverridesFontVariantEmojiText()
        {
            var pdf = await Render("font-variant-emoji: text;", "&#x2764;&#xFE0F;");

            Assert.Contains("/ActualText <FEFF2764FE0F>", pdf);
        }
    }
}
