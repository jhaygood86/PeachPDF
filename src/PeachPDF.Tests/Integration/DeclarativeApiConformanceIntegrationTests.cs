using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.Tests.TestSupport;
using System;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for a bug the post-change review agent found in the CMYK/PDF-X work: the
    /// declarative document-building path (<see cref="PdfGenerator.CreateDocument"/>/<see cref="PdfGenerator.AddPages"/>)
    /// never wired <see cref="PdfGenerateConfig.PdfXConformance"/>/<see cref="PdfGenerateConfig.PdfAConformance"/>/
    /// <see cref="PdfGenerateConfig.ColorOptions"/> into <c>document.PdfDocument.Options</c> - only the HTML
    /// path (<see cref="PdfGenerator.AddPdfPages(PeachPdfDocument,string?,PdfGenerateConfig,PeachPdfCssContent?)"/>)
    /// did. <c>RenderPagesCore</c> still wrote a fully-formed <c>/OutputIntents</c>/XMP conformance claim
    /// from <c>config</c> directly, but the actual paint-time construct guards
    /// (<see cref="PdfXColorSpaceGuard"/>/<see cref="PdfATransparencyGuard"/>) read <c>document.Options</c>
    /// instead, so a declaratively-built PDF/X or PDF/A document would silently claim conformance while
    /// none of its restrictions were enforced. Fixed by extracting <c>PdfGenerator.EstablishDocumentOptions</c>
    /// and calling it from both paths - these tests prove the declarative path now enforces exactly what
    /// the HTML path already does (see <c>PdfXConformanceTests</c>/<c>PdfAConformanceTests</c> for the HTML
    /// -path originals these mirror).
    /// </summary>
    public class DeclarativeApiConformanceIntegrationTests
    {
        [Fact]
        public async Task CreateDocument_X1aConformance_ChromaticRgbBackground_Throws()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                Metadata = new PdfDocumentMetadata { CreationDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                PdfXConformance = PdfXConformance.X1a,
                ColorOptions = new ColorOptions
                {
                    OutputIntentProfile = IccProfileFixture.BuildCmykProfile(),
                    OutputIntentIdentifier = "Test CMYK Profile",
                },
            };

            var thrown = await Assert.ThrowsAnyAsync<PdfConformanceException>(() =>
                new PdfGenerator().CreateDocument(doc =>
                {
                    doc.Page(page => page.Background(PdfColor.FromRgb(255, 0, 0)).Content(_ => { }));
                }, config));

            Assert.IsType<PdfXConformanceException>(thrown);
        }

        [Fact]
        public async Task CreateDocument_X1aConformance_AchromaticRgbBackground_Succeeds()
        {
            // The one exception X1a permits for RGB: an achromatic (gray/black/white) color converts
            // losslessly to CMYK ink (ColorOptions.BlackGeneration) rather than being rejected - proves
            // the declarative path reached the *same* guard behavior as the HTML path, not just "any
            // guard at all".
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                Metadata = new PdfDocumentMetadata { CreationDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                PdfXConformance = PdfXConformance.X1a,
                ColorOptions = new ColorOptions
                {
                    OutputIntentProfile = IccProfileFixture.BuildCmykProfile(),
                    OutputIntentIdentifier = "Test CMYK Profile",
                },
            };

            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Page(page => page.Background(PdfColor.FromRgb(0, 0, 0)).Content(_ => { }));
            }, config);

            Assert.NotNull(document);
        }

        [Fact]
        public async Task CreateDocument_PdfAConformance_LiveTransparency_Throws()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, Metadata = new PdfDocumentMetadata { CreationDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) }, PdfAConformance = PdfAConformance.PdfA1B };

            var thrown = await Assert.ThrowsAnyAsync<PdfConformanceException>(() =>
                new PdfGenerator().CreateDocument(doc =>
                {
                    // Semi-transparent fill forces a live transparency group - forbidden under PDF/A-1.
                    doc.Page(page => page.Background(PdfColor.FromArgb(128, 255, 0, 0)).Content(_ => { }));
                }, config));

            Assert.IsType<PdfAConformanceException>(thrown);
        }

        [Fact]
        public async Task AddPages_RepeatedCall_DifferentPdfXConformance_Throws()
        {
            // Mirrors PdfXConformanceTests.RepeatedAddPdfPages_DifferentConformance_Throws for the
            // declarative path - proves EstablishDocumentOptions's cross-call consistency guard is
            // reached from AddPages too, not just AddPdfPages.
            var document = new PeachPdfDocument(new PdfSharpCore.Pdf.PdfDocument());
            var generator = new PdfGenerator();

            var x1aConfig = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                Metadata = new PdfDocumentMetadata { CreationDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                PdfXConformance = PdfXConformance.X1a,
                ColorOptions = new ColorOptions
                {
                    OutputIntentProfile = IccProfileFixture.BuildCmykProfile(),
                    OutputIntentIdentifier = "Test CMYK Profile",
                },
            };
            await generator.AddPages(document, doc => doc.Page(page => page.Content(_ => { })), x1aConfig);

            var x4Config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                Metadata = new PdfDocumentMetadata { CreationDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                PdfXConformance = PdfXConformance.X4,
                ColorOptions = new ColorOptions
                {
                    OutputIntentProfile = IccProfileFixture.BuildRgbProfile(),
                    OutputIntentIdentifier = "Test RGB Profile",
                },
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                generator.AddPages(document, doc => doc.Page(page => page.Content(_ => { })), x4Config));
        }
    }
}
