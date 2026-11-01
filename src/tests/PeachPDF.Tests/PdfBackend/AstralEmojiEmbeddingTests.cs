using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;

namespace PeachPDF.Tests.PdfEngineTests
{
    /// <summary>An astral (supplementary-plane) emoji rendered through the generator, embedded and mapped back through ToUnicode.</summary>
    public class AstralEmojiEmbeddingTests
    {
        private const int Grin = 0x1F600;      // GRINNING FACE (astral), in Noto Emoji

        [Fact]
        public async Task Emoji_AstralCodepoint_RendersEmbedsAndRoundTripsToUnicode()
        {
            var b64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Emoji));

            var html = $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'Emoji'; src: url('data:font/truetype;base64,{b64}') format('truetype'); }}
body {{ font-family: 'Emoji'; font-size: 40pt; }}
</style></head><body>{char.ConvertFromUtf32(Grin)}</body></html>";

            var generator = new PdfGenerator();
            // Uncompressed, so the ToUnicode stream stays readable.
            var doc = await generator.GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });

            var ms = new MemoryStream();
            doc.Save(ms);
            var pdf = Encoding.Latin1.GetString(ms.ToArray());

            // The emoji font subset embeds its outline...
            Assert.Contains("/FontFile2", pdf);
            // ...and the ToUnicode map carries the emoji back as its UTF-16 surrogate pair (D83D DE00 =
            // U+1F600), proving the pipeline handled the astral character as ONE codepoint end to end.
            Assert.Contains("D83DDE00", pdf);
        }
    }
}
