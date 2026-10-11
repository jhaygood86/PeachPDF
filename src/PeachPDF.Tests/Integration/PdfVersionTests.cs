using System;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class PdfVersionTests
    {
        const string SimpleHtml = "<html><body><p>Hello</p></body></html>";

        [Fact]
        public async Task Default_IsPdf20()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var result = await new PdfGenerator().GeneratePdf(SimpleHtml, config);

            Assert.Equal(PdfVersion.Pdf20, config.PdfVersion);
            Assert.Equal(20, result.PdfDocument.Version);
        }

        [Fact]
        public async Task ExplicitPdf17_KeepsTheHistoricalVersion14()
        {
            // PdfVersion.Pdf17 asks for PeachPDF's pre-PDF-2.0 output: the document's own constructor starts at 14, and
            // nothing bumps it unless a feature or a PDF/A or PDF/X level needs a later one.
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, PdfVersion = PdfVersion.Pdf17 };
            var result = await new PdfGenerator().GeneratePdf(SimpleHtml, config);

            Assert.Equal(14, result.PdfDocument.Version);
        }

        [Fact]
        public async Task Pdf20_SetsDocumentVersionTo20()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, PdfVersion = PdfVersion.Pdf20 };
            var result = await new PdfGenerator().GeneratePdf(SimpleHtml, config);

            Assert.Equal(20, result.PdfDocument.Version);
        }

        [Fact]
        public async Task Pdf20_WritesPdf20FileHeader()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfVersion = PdfVersion.Pdf20,
                CompressContentStreams = false,
            };
            var pdfText = await GetPdfText(SimpleHtml, config);

            Assert.StartsWith("%PDF-2.0\n", pdfText);
        }

        [Fact]
        public async Task Pdf17_WritesPdf17FileHeader()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfVersion = PdfVersion.Pdf17,
                CompressContentStreams = false,
            };
            var pdfText = await GetPdfText(SimpleHtml, config);

            Assert.StartsWith("%PDF-1.4\n", pdfText);
        }

        [Fact]
        public async Task Pdf20_WithPdfAConformance_Throws()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfVersion = PdfVersion.Pdf20,
                PdfAConformance = PdfAConformance.PdfA2B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        [Fact]
        public async Task AddPdfPages_DifferentPdfVersionAcrossCalls_Throws()
        {
            var document = await new PdfGenerator().GeneratePdf(SimpleHtml, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfVersion = PdfVersion.Pdf20,
            });

            var generator = new PdfGenerator();
            await Assert.ThrowsAsync<InvalidOperationException>(() => generator.AddPdfPages(
                document,
                SimpleHtml,
                new PdfGenerateConfig { PageSize = PageSize.A4, PdfVersion = PdfVersion.Pdf17 },
                null));
        }

        [Fact]
        public async Task AddPdfPages_SamePdfVersionAcrossCalls_Succeeds()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, PdfVersion = PdfVersion.Pdf20 };
            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(SimpleHtml, config);

            await generator.AddPdfPages(document, SimpleHtml, config, null);

            Assert.Equal(20, document.PdfDocument.Version);
            Assert.Equal(2, document.PdfDocument.Pages.Count);
        }

        static async Task<string> GetPdfText(string html, PdfGenerateConfig config)
        {
            var document = await new PdfGenerator().GeneratePdf(html, config);
            using var stream = new System.IO.MemoryStream();
            document.Save(stream);
            return System.Text.Encoding.Latin1.GetString(stream.ToArray());
        }
    }
}
