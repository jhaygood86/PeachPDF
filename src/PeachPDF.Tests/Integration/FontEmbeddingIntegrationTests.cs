using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Utils;
using System;
using System.IO;
using System.Linq;
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
        private static string WindowsFontsDir =>
            Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows", "Fonts");

        // -------------------------------------------------------------------------
        // TTF regression: must still embed as /FontFile2
        // -------------------------------------------------------------------------

        [Fact]
        public async Task TtfFont_ViaFontFaceDataUri_EmbedsAs_FontFile2()
        {
            var ttfPath = FontResolver.SupportedFonts
                .FirstOrDefault(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase));
            if (ttfPath == null) return;

            var ttfBytes = File.ReadAllBytes(ttfPath);
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
        // CFF embedding fix: must use /FontFile3 with /OpenType subtype
        // -------------------------------------------------------------------------

        [Fact]
        public async Task CffFont_ViaFontFaceDataUri_EmbedsAs_FontFile3_OpenType()
        {
            if (!OperatingSystem.IsWindows()) return;
            var otfPath = Directory.GetFiles(WindowsFontsDir, "*.otf").FirstOrDefault();
            if (otfPath == null) return;

            var otfBytes = File.ReadAllBytes(otfPath);
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
        // CFF: /Length1 must NOT appear on a FontFile3 stream (only valid for FontFile2)
        // -------------------------------------------------------------------------

        [Fact]
        public async Task CffFont_EmbeddedStream_HasNo_Length1()
        {
            if (!OperatingSystem.IsWindows()) return;
            var otfPath = Directory.GetFiles(WindowsFontsDir, "*.otf").FirstOrDefault();
            if (otfPath == null) return;

            var otfBytes = File.ReadAllBytes(otfPath);
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
            var ttfPath = FontResolver.SupportedFonts
                .FirstOrDefault(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase));
            if (ttfPath == null) return;

            var woffBytes = WrapTtfAsWoff(File.ReadAllBytes(ttfPath));
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
            var ttfPath = FontResolver.SupportedFonts
                .FirstOrDefault(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase));
            if (ttfPath == null) return;

            var ttfBytes = File.ReadAllBytes(ttfPath);
            var familyName = TtfFontDescription.LoadDescription(ttfPath).FontFamilyInvariantCulture;

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
        // Helpers
        // -------------------------------------------------------------------------

        private static string GetPdfText(PdfDocument doc)
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

            byte[] woff = new byte[woffPos];
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
