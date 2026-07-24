using PeachPDF;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for the cross-<see cref="PdfGenerator"/>-instance font cache collision: before
    /// this fix, several static, process-wide caches in the embedded PDFsharp fork - <c>FontFactory</c>'s
    /// <c>FontResolverInfosByName</c>, <c>GlyphTypefaceCache</c>, and (the actual root cause of the
    /// embedded-font-bytes collision this test targets) <c>FontDescriptorCache</c>, independently re-
    /// queried by <c>PdfType0Font</c>/<c>PdfTrueTypeFont</c>/<c>FontHelper</c> instead of reusing an
    /// already-resolved <c>XFont</c>'s own descriptor - were keyed only by family name + style/weight,
    /// with no notion of which resolver instance's bytes actually produced the data. Whichever
    /// <see cref="PdfGenerator"/> resolved a given custom family+style first "won" every one of these
    /// cache slots for EVERY other instance in the process for the rest of its lifetime, even when a
    /// different instance had registered completely different font bytes under the identical CSS family
    /// name (a realistic multi-tenant/multi-request scenario, e.g. two requests each declaring their own
    /// <c>@font-face</c> for the same family name). Fixed by routing custom (non-system) families through
    /// each <c>FontResolver</c>'s own instance-owned caches instead of the shared static ones, and by
    /// having the PDF-embedding classes reuse <c>XFont.Descriptor</c> rather than re-deriving it.
    /// </summary>
    public class FontFactoryCrossInstanceIsolationTests
    {
        [Fact]
        public async Task TwoGenerators_DifferentBytesUnderSameFamilyName_EachEmbedsItsOwnFont()
        {
            var ttfBytes = File.ReadAllBytes(BundledFonts.Ttf);
            var otfBytes = File.ReadAllBytes(BundledFonts.Otf);
            var ttfB64 = Convert.ToBase64String(ttfBytes);
            var otfB64 = Convert.ToBase64String(otfBytes);

            var htmlA = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'SharedFamily'; src: url('data:font/truetype;base64,{ttfB64}') format('truetype'); }}
body {{ font-family: 'SharedFamily'; font-size: 14pt; }}
</style></head>
<body>Generator A: TrueType</body>
</html>";

            var htmlB = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'SharedFamily'; src: url('data:font/opentype;base64,{otfB64}') format('opentype'); }}
body {{ font-family: 'SharedFamily'; font-size: 14pt; }}
</style></head>
<body>Generator B: OpenType/CFF</body>
</html>";

            // Generator A resolves "SharedFamily"/normal/400 FIRST - under the pre-fix bug, this would
            // populate the shared static cache slot that Generator B (a completely separate instance)
            // would then incorrectly also be served from, regardless of its own, different @font-face.
            var generatorA = new PdfGenerator();
            var docA = await generatorA.GeneratePdf(htmlA, PageSize.A4);
            var pdfTextA = GetPdfText(docA);

            var generatorB = new PdfGenerator();
            var docB = await generatorB.GeneratePdf(htmlB, PageSize.A4);
            var pdfTextB = GetPdfText(docB);

            // A's font is TrueType (glyf) -> embeds as /FontFile2.
            Assert.Contains("/FontFile2", pdfTextA);

            // B's font is OpenType/CFF -> must embed as /FontFile3 /OpenType, NOT silently reuse A's
            // /FontFile2 TrueType data (the exact collision this fix closes).
            Assert.Contains("/FontFile3", pdfTextB);
            Assert.Contains("/OpenType", pdfTextB);
        }

        [Fact]
        public async Task GeneratorInstance_IsCollectible_AfterUse()
        {
            // The whole point of routing custom families through the FontResolver's own instance-owned
            // dictionaries (rather than a static one with disambiguated keys) is that the cache cannot
            // outlive the PdfGenerator it belongs to - it's just a plain instance field, no static/global
            // root keeps it (or the custom font bytes it holds) alive once nothing references the
            // generator anymore.
            var weakRef = await CreateAndUseGeneratorAsync();

            for (var i = 0; i < 5 && weakRef.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.False(weakRef.IsAlive);
        }

        // Isolated into its own method (not inlined into the test) so the JIT doesn't keep the
        // PdfGenerator/PdfSharpAdapter/FontResolver chain rooted in a live stack-frame local past the
        // point this method returns.
        private static async Task<WeakReference> CreateAndUseGeneratorAsync()
        {
            var ttfBytes = File.ReadAllBytes(BundledFonts.Ttf);
            var b64 = Convert.ToBase64String(ttfBytes);
            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'CollectibleFamily'; src: url('data:font/truetype;base64,{b64}'); }}
body {{ font-family: 'CollectibleFamily'; font-size: 14pt; }}
</style></head>
<body>Collectible</body>
</html>";

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);
            doc.Save(new MemoryStream());

            return new WeakReference(generator);
        }

        private static string GetPdfText(PeachPdfDocument doc)
        {
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }
    }
}
