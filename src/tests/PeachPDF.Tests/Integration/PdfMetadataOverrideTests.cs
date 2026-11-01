namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Tests for <see cref="PdfGenerateConfig.Metadata"/> (per-field overrides of the HTML-extracted
    /// document metadata) and for <see cref="PdfGenerateConfig.DefaultLanguage"/> always populating the
    /// PDF catalog <c>/Lang</c>. Reaches the internal <c>PeachPdfDocument.PdfDocument</c> via
    /// <c>InternalsVisibleTo</c>.
    /// </summary>
    public class PdfMetadataOverrideTests
    {
        private const string HtmlWithMetadata =
            "<html><head><title>HTML Title</title>" +
            "<meta name=\"author\" content=\"HTML Author\">" +
            "<meta name=\"subject\" content=\"HTML Subject\">" +
            "<meta name=\"keywords\" content=\"html,keywords\"></head>" +
            "<body><p>Body</p></body></html>";

        [Fact]
        public async Task Metadata_Null_UsesHtmlExtractedValues()
        {
            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(HtmlWithMetadata, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Equal("HTML Title", document.Inspect().Info.Text("/Title"));
            Assert.Equal("HTML Author", document.Inspect().Info.Text("/Author"));
            Assert.Equal("HTML Subject", document.Inspect().Info.Text("/Subject"));
            Assert.Equal("html,keywords", document.Inspect().Info.Text("/Keywords"));
        }

        [Fact]
        public async Task Metadata_SetFields_OverrideHtmlValues()
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                Metadata = new PdfDocumentMetadata
                {
                    Title = "Override Title",
                    Author = "Override Author",
                    Subject = "Override Subject",
                    Keywords = "override,keywords",
                    Creator = "Override Creator",
                },
            };

            var document = await generator.GeneratePdf(HtmlWithMetadata, config);

            Assert.Equal("Override Title", document.Inspect().Info.Text("/Title"));
            Assert.Equal("Override Author", document.Inspect().Info.Text("/Author"));
            Assert.Equal("Override Subject", document.Inspect().Info.Text("/Subject"));
            Assert.Equal("override,keywords", document.Inspect().Info.Text("/Keywords"));
            Assert.Equal("Override Creator", document.Inspect().Info.Text("/Creator"));
        }

        [Fact]
        public async Task Metadata_NullField_FallsBackToHtmlValue()
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig
            {
                // Only Title is overridden; the other fields stay null and must come from the HTML.
                PageSize = PageSize.A4,
                Metadata = new PdfDocumentMetadata { Title = "Just Title" },
            };

            var document = await generator.GeneratePdf(HtmlWithMetadata, config);

            Assert.Equal("Just Title", document.Inspect().Info.Text("/Title"));
            Assert.Equal("HTML Author", document.Inspect().Info.Text("/Author"));
            Assert.Equal("HTML Subject", document.Inspect().Info.Text("/Subject"));
        }

        [Fact]
        public async Task DefaultLanguage_PopulatesCatalogLang_WhenDocumentDeclaresNone()
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, DefaultLanguage = "de-DE" };

            var document = await generator.GeneratePdf(
                "<html><body><p>x</p></body></html>", config);

            Assert.Equal("de-DE", document.Inspect().Language);
        }

        [Fact]
        public async Task HtmlLang_OverridesDefaultLanguage_ForCatalogLang()
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, DefaultLanguage = "de-DE" };

            var document = await generator.GeneratePdf(
                "<html lang=\"en-US\"><body><p>x</p></body></html>", config);

            Assert.Equal("en-US", document.Inspect().Language);
        }
    }
}
