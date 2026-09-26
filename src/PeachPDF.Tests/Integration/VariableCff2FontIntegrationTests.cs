using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A variable font with CFF2 outlines in a document: each distinct location is embedded as its own static CFF font, because a PDF has no
    /// use for a <c>CFF2</c> table. What the embedded fonts draw is checked by <c>Cff2ExportTests</c> in the engine's tests; the pages of
    /// the matching showcase were also rasterized with PDFium and MuPDF.
    /// </summary>
    public class VariableCff2FontIntegrationTests
    {
        private static string Html(string body)
        {
            var b64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.VariableCff2Test));
            return $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'VC'; src: url('data:font/otf;base64,{b64}') format('opentype'); }}
body {{ font-family: 'VC'; font-size: 24pt; }}
</style></head><body>{body}</body></html>";
        }

        private static async Task<byte[]> RenderAsync(string html)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            var stream = new MemoryStream();
            doc.Save(stream);
            return stream.ToArray();
        }

        /// <summary>The embedded font programs of a PDF: every stream, inflated where it can be, that begins like an OpenType file with CFF outlines.</summary>
        private static List<byte[]> EmbeddedCffFonts(byte[] pdf)
        {
            var fonts = new List<byte[]>();
            var text = Encoding.Latin1.GetString(pdf);
            int at = 0;
            while ((at = text.IndexOf("stream", at, StringComparison.Ordinal)) >= 0)
            {
                int start = at + "stream".Length;
                if (text[start] == '\r') start++;
                if (text[start] == '\n') start++;
                int end = text.IndexOf("endstream", start, StringComparison.Ordinal);
                if (end < 0) break;
                at = end + "endstream".Length;

                var raw = pdf.AsSpan(start, end - start).ToArray();
                byte[] data;
                try
                {
                    using var inflated = new MemoryStream();
                    using (var zlib = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress))
                    {
                        zlib.CopyTo(inflated);
                    }

                    data = inflated.ToArray();
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException)
                {
                    data = raw;
                }

                if (data.Length > 12 && Encoding.ASCII.GetString(data, 0, 4) == "OTTO")
                {
                    fonts.Add(data);
                }
            }

            return fonts;
        }

        private static IEnumerable<string> Tables(byte[] font)
        {
            int count = (font[4] << 8) | font[5];
            return Enumerable.Range(0, count).Select(i => Encoding.ASCII.GetString(font, 12 + i * 16, 4)).ToArray();
        }

        [Fact]
        public async Task ACff2Font_IsEmbeddedAsAStaticCffFont()
        {
            var fonts = EmbeddedCffFonts(await RenderAsync(Html("<p>ABCDEF</p>")));

            var font = Assert.Single(fonts);
            var tables = Tables(font).ToArray();
            Assert.Contains("CFF ", tables);
            Assert.DoesNotContain("CFF2", tables);
            Assert.DoesNotContain("fvar", tables);
            Assert.DoesNotContain("HVAR", tables);
        }

        [Fact]
        public async Task TwoWeights_EmbedTwoInstances_AndTheSameLocationReachedTwoWaysOnlyOne()
        {
            var fonts = EmbeddedCffFonts(await RenderAsync(Html(
                "<p>ABCDEF</p><p style=\"font-weight: 900\">ABCDEF</p><p style=\"font-variation-settings: 'wght' 900\">ABCDEF</p>")));

            Assert.Equal(2, fonts.Count);
            Assert.NotEqual(fonts[0], fonts[1]);
            Assert.All(fonts, font => Assert.DoesNotContain("CFF2", Tables(font)));
        }

        [Fact]
        public async Task ABoldInstance_IsNotAlsoFakedBold()
        {
            var pdf = Encoding.Latin1.GetString(await RenderAsync(Html("<p style=\"font-weight: 700\">ABCDEF</p>")));

            Assert.DoesNotContain("2 Tr", pdf);
        }
    }
}
