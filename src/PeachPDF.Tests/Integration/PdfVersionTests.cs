using System;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class PdfVersionTests
    {
        const string SimpleHtml = "<html><body><p>Hello</p></body></html>";

        [Fact]
        public async Task Default_Pdf17_KeepsHistoricalVersion14()
        {
            // PdfVersion.Pdf17 is a no-op relative to PeachPDF's historical default (the document's own
            // constructor already starts at 14, and nothing bumps it unless something explicitly asks
            // for 1.7 or 2.0) - this pins that default down so a future change can't silently regress it.
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var result = await new PdfGenerator().GeneratePdf(SimpleHtml, config);

            Assert.Equal(PdfVersion.Pdf17, config.PdfVersion);
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
