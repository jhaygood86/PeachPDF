using PeachPDF;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Fonts;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end regression tests verifying that each supported font format is correctly
    /// processed through the full pipeline: @font-face → AddFont → PDF font embedding.
    ///
    /// Checks:
    ///   TTF  → /FontFile2 (TrueType key)
    ///   CFF  → /FontFile3 /Subtype /OpenType  (fix in PdfCIDFont.PrepareForSave)
    ///   WOFF → converted to OpenType before parsing (fix in PdfSharpAdapter.AddFont)
    /// </summary>
    public class FontEmbeddingIntegrationTests
    {
        // -------------------------------------------------------------------------
        // TTF regression: must still embed as /FontFile2
        // -------------------------------------------------------------------------

        [Fact]
        public async Task TtfFont_ViaFontFaceDataUri_EmbedsAs_FontFile2()
        {
            var ttfBytes = File.ReadAllBytes(BundledFonts.Ttf);
            var b64 = Convert.ToBase64String(ttfBytes);

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestTtf'; src: url('data:font/truetype;base64,{b64}') format('truetype'); }}
body {{ font-family: 'TestTtf', serif; font-size: 14pt; }}
</style></head>
<body>Hello TrueType</body>
</html>";

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);
            var pdfText = GetPdfText(doc);

            Assert.Contains("/FontFile2", pdfText);
        }

        // -------------------------------------------------------------------------
        // Regression: a @font-face src with no format() hint at all (valid CSS - format() is an
        // optional hint) must still be attempted, not silently dropped. Real-world stylesheets (e.g.
        // css4.pub's Icelandic dictionary page) ship bare `src: url("Font.otf")` with no format().
        // -------------------------------------------------------------------------

        [Fact]
        public async Task TtfFont_ViaFontFaceDataUri_NoFormatHint_StillEmbedsAs_FontFile2()
        {
            var ttfBytes = File.ReadAllBytes(BundledFonts.Ttf);
            var b64 = Convert.ToBase64String(ttfBytes);

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestTtfNoFormat'; src: url('data:font/truetype;base64,{b64}'); }}
body {{ font-family: 'TestTtfNoFormat', serif; font-size: 14pt; }}
</style></head>
<body>Hello TrueType, no format hint</body>
</html>";

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);
            var pdfText = GetPdfText(doc);

            Assert.Contains("/FontFile2", pdfText);
        }

        // -------------------------------------------------------------------------
        // Regression: a @font-face src with more than one url() fallback entry (an extremely common
        // real-world shape - .woff2/.woff/.ttf fallback chains) used to throw InvalidOperationException
        // from CssValueParser.GetFontFacePropertyValue's unqualified .SingleOrDefault() over the whole
        // comma-separated value. Must not throw, and must fall through past a rejected/unusable first
        // candidate to a working later one.
        // -------------------------------------------------------------------------

        [Fact]
        public async Task MultiUrlSrc_DoesNotThrow_AndFallsThroughToWorkingCandidate()
        {
            var ttfBytes = File.ReadAllBytes(BundledFonts.Ttf);
            var b64 = Convert.ToBase64String(ttfBytes);

            // The first candidate declares a format PdfSharpAdapter explicitly rejects
            // (embedded-opentype), so it must be skipped without loading; the second candidate (no
            // format hint, same real TTF bytes) must still be tried and must succeed.
            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestMultiUrl'; src: url('data:font/truetype;base64,{b64}') format('embedded-opentype'), url('data:font/truetype;base64,{b64}'); }}
body {{ font-family: 'TestMultiUrl', serif; font-size: 14pt; }}
</style></head>
<body>Hello multi-url src</body>
</html>";

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);
            var pdfText = GetPdfText(doc);

            Assert.Contains("/FontFile2", pdfText);
        }

        // -------------------------------------------------------------------------
        // Regression: two @font-face rules for the SAME CSS family, each with its own declared
        // font-weight descriptor and its own real (differently-shaped) font file, must resolve
        // per-request to the correct face - the declared descriptor is authoritative, independent of
        // whatever weight each file's own internal tables happen to sniff to (both bundled test fonts
        // sniff as regular/400, so without descriptor-driven overrides both requests would collide on
        // whichever face was registered last).
        // -------------------------------------------------------------------------

        [Fact]
        public async Task TwoFontFaceRules_SameFamily_DifferentDeclaredWeights_EachRequestUsesItsOwnFace()
        {
            var ttfBytes = File.ReadAllBytes(BundledFonts.Ttf);
            var otfBytes = File.ReadAllBytes(BundledFonts.Otf);
            var ttfB64 = Convert.ToBase64String(ttfBytes);
            var otfB64 = Convert.ToBase64String(otfBytes);

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestMultiWeight'; font-weight: 400; src: url('data:font/truetype;base64,{ttfB64}') format('truetype'); }}
@font-face {{ font-family: 'TestMultiWeight'; font-weight: 700; src: url('data:font/opentype;base64,{otfB64}') format('opentype'); }}
body {{ font-family: 'TestMultiWeight', serif; font-size: 14pt; }}
</style></head>
<body>
<p style=""font-weight: 400"">Regular weight text</p>
<p style=""font-weight: 700"">Bold weight text</p>
</body>
</html>";

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);
            var pdfText = GetPdfText(doc);

            // The 400-weight request must use the TTF face (/FontFile2) and the 700-weight request
            // must use the OTF face (/FontFile3 /OpenType) - both present proves each request picked
            // its own declared-weight face rather than colliding on one.
            Assert.Contains("/FontFile2", pdfText);
            Assert.Contains("/FontFile3", pdfText);
            Assert.Contains("/OpenType", pdfText);
        }

        // -------------------------------------------------------------------------
        // Regression: @font-face's src: local(...) must resolve against a genuinely-installed system
        // font end-to-end (DomParser.CascadeApplyStyleFonts -> RAdapter.AddLocalFontFamily), not just
        // parse - dynamically skipped (via Assert.Skip, matching the convention established by
        // PeachPDF.Tests.Network.MimeTypeResolverTests) on any non-Windows host, since it needs a known,
        // real installed font's own internal name to reference via local().
        // -------------------------------------------------------------------------

        [Fact]
        public async Task LocalFontFace_ResolvesToGenuinelyInstalledSystemFont()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Skip("Windows-only: resolves src: local() against a real installed system font.");
                return;
            }

            var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            var arialPath = Path.Combine(fontsDir, "arial.ttf");
            if (!File.Exists(arialPath))
            {
                Assert.Skip("This Windows host has no arial.ttf in its Fonts folder.");
                return;
            }

            var localName = TtfFontDescription.LoadDescription(arialPath).FontNameInvariantCulture;

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestLocalFont'; src: local('{localName}'); }}
body {{ font-family: 'TestLocalFont', serif; font-size: 14pt; }}
</style></head>
<body>Hello local() font</body>
</html>";

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);

            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
        }

        // -------------------------------------------------------------------------
        // CFF embedding fix: must use /FontFile3 with /OpenType subtype
        // -------------------------------------------------------------------------

        [Fact]
        public async Task CffFont_ViaFontFaceDataUri_EmbedsAs_FontFile3_OpenType()
        {
            var otfBytes = File.ReadAllBytes(BundledFonts.Otf);
            var b64 = Convert.ToBase64String(otfBytes);

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestCff'; src: url('data:font/opentype;base64,{b64}') format('opentype'); }}
body {{ font-family: 'TestCff', serif; font-size: 14pt; }}
</style></head>
<body>Hello CFF</body>
</html>";

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);
            var pdfText = GetPdfText(doc);

            Assert.Contains("/FontFile3", pdfText);
            Assert.Contains("/OpenType", pdfText);
        }

        // -------------------------------------------------------------------------
        // Direct regression for the reported bug: a CFF/OTF font (Satyr10.otf's actual shape - OTTO
        // sfnt tag, /CFF table, no glyf/loca) with no format() hint at all must still embed correctly,
        // not silently fall back to a different font.
        // -------------------------------------------------------------------------

        [Fact]
        public async Task CffFont_ViaFontFaceDataUri_NoFormatHint_StillEmbedsAs_FontFile3_OpenType()
        {
            var otfBytes = File.ReadAllBytes(BundledFonts.Otf);
            var b64 = Convert.ToBase64String(otfBytes);

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestCffNoFormat'; src: url('data:font/opentype;base64,{b64}'); }}
body {{ font-family: 'TestCffNoFormat', serif; font-size: 14pt; }}
</style></head>
<body>Hello CFF, no format hint</body>
</html>";

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);
            var pdfText = GetPdfText(doc);

            Assert.Contains("/FontFile3", pdfText);
            Assert.Contains("/OpenType", pdfText);
        }

        // -------------------------------------------------------------------------
        // CFF: /Length1 must NOT appear on a FontFile3 stream (only valid for FontFile2)
        // -------------------------------------------------------------------------

        [Fact]
        public async Task CffFont_EmbeddedStream_HasNo_Length1()
        {
            var otfBytes = File.ReadAllBytes(BundledFonts.Otf);
            var b64 = Convert.ToBase64String(otfBytes);

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestCffL'; src: url('data:font/opentype;base64,{b64}') format('opentype'); }}
body {{ font-family: 'TestCffL', serif; font-size: 14pt; }}
</style></head>
<body>Length1 check</body>
</html>";

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);

            var ms = new MemoryStream();
            doc.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());

            // A CFF font must be embedded under /FontFile3; assert it was actually embedded.
            Assert.Contains("/FontFile3", pdfText);

            // Scan each PDF object block that contains /FontFile3 and assert /Length1 is absent.
            // Use " obj" (with leading space) to avoid matching the "obj" inside "endobj".
            int searchFrom = 0;
            while (true)
            {
                int idx = pdfText.IndexOf("/FontFile3", searchFrom, StringComparison.Ordinal);
                if (idx < 0) break;

                int objStart = pdfText.LastIndexOf(" obj", idx, StringComparison.Ordinal);
                int objEnd   = pdfText.IndexOf("endobj", idx, StringComparison.Ordinal);
                if (objStart >= 0 && objEnd > objStart)
                {
                    var block = pdfText.Substring(objStart, objEnd - objStart);
                    Assert.False(block.Contains("/Length1"),
                        "/Length1 must not appear in a /FontFile3 stream dictionary (it is only valid for /FontFile2).");
                }

                searchFrom = idx + 1;
            }
        }

        // -------------------------------------------------------------------------
        // WOFF: full pipeline — wraps a real system TTF as WOFF, loads via @font-face,
        // verifies the PDF is generated without error (conversion happened transparently)
        // -------------------------------------------------------------------------

        [Fact]
        public async Task WoffFont_ViaFontFaceDataUri_GeneratesPdfSuccessfully()
        {
            var woffBytes = WrapTtfAsWoff(File.ReadAllBytes(BundledFonts.Ttf));
            var b64 = Convert.ToBase64String(woffBytes);

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestWoff'; src: url('data:font/woff;base64,{b64}') format('woff'); }}
body {{ font-family: 'TestWoff', serif; font-size: 14pt; }}
</style></head>
<body>Hello WOFF</body>
</html>";

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);

            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
            // PDF must have font data embedded (the WOFF was converted to OpenType before embedding)
            var pdfText = GetPdfText(doc);
            Assert.Contains("/FontFile2", pdfText);
        }

        // -------------------------------------------------------------------------
        // WOFF via AddFontFromStream: loads WOFF, generates PDF using that family name
        // -------------------------------------------------------------------------

        [Fact]
        public async Task WoffFont_ViaAddFontFromStream_IsUsableInPdf()
        {
            var ttfBytes = File.ReadAllBytes(BundledFonts.Ttf);
            var familyName = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontFamilyInvariantCulture;

            var woffBytes = WrapTtfAsWoff(ttfBytes);

            var generator = new PdfGenerator();
            using var woffStream = new MemoryStream(woffBytes);
            await generator.AddFontFromStream(woffStream);

            var html = $@"<!DOCTYPE html>
<html><head><style>
body {{ font-family: '{familyName}', serif; font-size: 14pt; }}
</style></head>
<body>WOFF via AddFontFromStream</body>
</html>";

            var doc = await generator.GeneratePdf(html, PageSize.A4);

            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
        }

        // -------------------------------------------------------------------------
        // WOFF2: full pipeline using a real, Brotli-compressed WOFF2 font file (this fork's
        // WOFF2 decoder cannot be exercised with a hand-synthesized sample the way the WOFF
        // test above wraps a system TTF, since a valid WOFF2 sample requires real Brotli
        // compression) -- verifies @font-face -> AddFontFamilyFromUrl -> AddFont ->
        // FontFormatConverter.ToOpenType -> Woff2Converter.Convert -> /FontFile2 embedding.
        // -------------------------------------------------------------------------

        [Fact]
        public async Task Woff2Font_ViaFontFaceDataUri_EmbedsAs_FontFile2()
        {
            var woff2Bytes = File.ReadAllBytes(BundledFonts.Woff2);
            var b64 = Convert.ToBase64String(woff2Bytes);

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestWoff2'; src: url('data:font/woff2;base64,{b64}') format('woff2'); }}
body {{ font-family: 'TestWoff2', serif; font-size: 14pt; }}
</style></head>
<body>Hello WOFF2</body>
</html>";

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);
            var pdfText = GetPdfText(doc);

            Assert.Contains("/FontFile2", pdfText);
        }

        [Fact]
        public async Task Woff2Font_ViaAddFontFromStream_IsUsableInPdf()
        {
            var woff2Bytes = File.ReadAllBytes(BundledFonts.Woff2);
            var openTypeBytes = Woff2Converter.Convert(woff2Bytes);
            var familyName = TtfFontDescription.LoadDescription(new MemoryStream(openTypeBytes)).FontFamilyInvariantCulture;

            var generator = new PdfGenerator();
            using var woff2Stream = new MemoryStream(woff2Bytes);
            await generator.AddFontFromStream(woff2Stream);

            var html = $@"<!DOCTYPE html>
<html><head><style>
body {{ font-family: '{familyName}', serif; font-size: 14pt; }}
</style></head>
<body>WOFF2 via AddFontFromStream</body>
</html>";

            var doc = await generator.GeneratePdf(html, PageSize.A4);

            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
            var pdfText = GetPdfText(doc);
            Assert.Contains("/FontFile2", pdfText);
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        private static string GetPdfText(PeachPdfDocument doc)
        {
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        // Wraps raw OpenType/TrueType bytes in a WOFF container without any per-table
        // compression (compLength == origLength), matching the W3C WOFF spec §4.
        private static byte[] WrapTtfAsWoff(byte[] ttf)
        {
            uint sfVersion = ReadUInt32BE(ttf, 0);
            int numTables = ReadUInt16BE(ttf, 4);

            var tags = new uint[numTables];
            var checksums = new uint[numTables];
            var offsets = new uint[numTables];
            var lengths = new uint[numTables];

            for (int i = 0; i < numTables; i++)
            {
                int e = 12 + i * 16;
                tags[i] = ReadUInt32BE(ttf, e);
                checksums[i] = ReadUInt32BE(ttf, e + 4);
                offsets[i] = ReadUInt32BE(ttf, e + 8);
                lengths[i] = ReadUInt32BE(ttf, e + 12);
            }

            int dataStart = 44 + numTables * 20;
            var woffOffsets = new uint[numTables];
            uint woffPos = (uint)dataStart;
            for (int i = 0; i < numTables; i++)
            {
                woffOffsets[i] = woffPos;
                woffPos += lengths[i];
                woffPos = (woffPos + 3u) & ~3u;
            }

            byte[] woff = new byte[checked((int)woffPos)];
            int p = 0;

            WriteUInt32BE(woff, p, 0x774F4646); p += 4;
            WriteUInt32BE(woff, p, sfVersion); p += 4;
            WriteUInt32BE(woff, p, woffPos); p += 4;
            WriteUInt16BE(woff, p, (ushort)numTables); p += 2;
            WriteUInt16BE(woff, p, 0); p += 2;
            WriteUInt32BE(woff, p, (uint)ttf.Length); p += 4;
            WriteUInt16BE(woff, p, 1); p += 2;
            WriteUInt16BE(woff, p, 0); p += 2;
            WriteUInt32BE(woff, p, 0); p += 4;
            WriteUInt32BE(woff, p, 0); p += 4;
            WriteUInt32BE(woff, p, 0); p += 4;
            WriteUInt32BE(woff, p, 0); p += 4;
            WriteUInt32BE(woff, p, 0); p += 4;

            for (int i = 0; i < numTables; i++)
            {
                WriteUInt32BE(woff, p, tags[i]); p += 4;
                WriteUInt32BE(woff, p, woffOffsets[i]); p += 4;
                WriteUInt32BE(woff, p, lengths[i]); p += 4;
                WriteUInt32BE(woff, p, lengths[i]); p += 4;
                WriteUInt32BE(woff, p, checksums[i]); p += 4;
            }

            for (int i = 0; i < numTables; i++)
                Array.Copy(ttf, (int)offsets[i], woff, (int)woffOffsets[i], (int)lengths[i]);

            return woff;
        }

        private static uint ReadUInt32BE(byte[] d, int o) =>
            ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

        private static int ReadUInt16BE(byte[] d, int o) =>
            (d[o] << 8) | d[o + 1];

        private static void WriteUInt32BE(byte[] d, int o, uint v)
        { d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v; }

        private static void WriteUInt16BE(byte[] d, int o, ushort v)
        { d[o] = (byte)(v >> 8); d[o + 1] = (byte)v; }
    }
}
