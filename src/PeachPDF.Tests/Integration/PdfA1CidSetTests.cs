using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// ISO 19005-1 (6.3.5) alone requires a <c>/CIDSet</c> bitmap on every CIDFont subset's font descriptor; later parts made it
    /// optional, so only PDF/A-1 writes it. Found by running veraPDF over real output.
    /// </summary>
    public class PdfA1CidSetTests
    {
        private static async Task<string> RenderAsync(PdfAConformance level)
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = level,
                Metadata = new PdfDocumentMetadata { CreationDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            };
            var doc = await new PdfGenerator().GeneratePdf("<html lang=\"en\"><body><p>Hello CIDSet</p></body></html>", config);
            using var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Theory]
        [InlineData(PdfAConformance.PdfA1B)]
        [InlineData(PdfAConformance.PdfA1A)]
        public async Task PdfA1_WritesACidSetForEachCidFontSubset(PdfAConformance level)
        {
            var text = await RenderAsync(level);

            Assert.Contains("/CIDSet", text);
        }

        [Theory]
        [InlineData(PdfAConformance.PdfA2B)]
        [InlineData(PdfAConformance.PdfA4)]
        public async Task LaterParts_DoNotWriteACidSet(PdfAConformance level)
        {
            var text = await RenderAsync(level);

            Assert.DoesNotContain("/CIDSet", text);
        }
    }
}
