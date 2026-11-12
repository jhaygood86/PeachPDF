using PeachDrawing.PDF.Engine;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>PDF/UA (ISO 14289) identification and the requirements the writer enforces for a file that claims it.</summary>
    public class PdfUAConformanceTests
    {
        const string Html = "<html lang='en'><head><title>Accessible page</title></head><body><h1>Heading</h1><p>Text</p></body></html>";

        static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        static readonly XNamespace PdfUaId = "http://www.aiim.org/pdfua/ns/id/";
        static readonly XNamespace PdfaExtension = "http://www.aiim.org/pdfa/ns/extension/";
        static readonly XNamespace PdfaSchema = "http://www.aiim.org/pdfa/ns/schema#";
        static readonly XNamespace PdfaProperty = "http://www.aiim.org/pdfa/ns/property#";

        static PdfGenerateConfig Config(PdfUAConformance level, PdfAConformance pdfA = PdfAConformance.None) => new()
        {
            PageSize = PageSize.A4,
            PdfUAConformance = level,
            PdfAConformance = pdfA,
            Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
        };

        static async Task<(PeachPdfDocument Document, string Text)> RenderAsync(string html, PdfGenerateConfig config)
        {
            var document = await new PdfGenerator().GeneratePdf(html, config);
            using var stream = new MemoryStream();
            document.Save(stream);
            return (document, Encoding.Latin1.GetString(stream.ToArray()));
        }

        static XElement Description(string pdfText)
        {
            var match = Regex.Match(pdfText, @"<x:xmpmeta.*?</x:xmpmeta>", RegexOptions.Singleline);
            Assert.True(match.Success, "No XMP packet found in generated PDF.");
            return XDocument.Parse(match.Value).Descendants(Rdf + "Description").First();
        }

        [Fact]
        public async Task UA1_IdentifiesTheFileInItsXmp_AndWritesAPdf17TaggedFileThatShowsItsTitle()
        {
            var (document, text) = await RenderAsync(Html, Config(PdfUAConformance.UA1));

            var description = Description(text);
            Assert.Equal("1", description.Element(PdfUaId + "part")?.Value);
            Assert.Null(description.Element(PdfUaId + "rev"));

            var file = document.Inspect();
            Assert.Equal("1.7", file.Version);
            Assert.True(file.Catalog.Dict("/MarkInfo")!.Bool("/Marked"));
            Assert.NotNull(file.Catalog.Dict("/StructTreeRoot"));
            Assert.True(file.Catalog.Dict("/ViewerPreferences")!.Bool("/DisplayDocTitle"));
            Assert.Equal("en", file.Language);
        }

        [Fact]
        public async Task UA2_IdentifiesItsPartAndRevision_AndWritesAPdf20File()
        {
            var (document, text) = await RenderAsync(Html, Config(PdfUAConformance.UA2));

            var description = Description(text);
            Assert.Equal("2", description.Element(PdfUaId + "part")?.Value);
            Assert.Equal("2024", description.Element(PdfUaId + "rev")?.Value);
            Assert.Equal("2.0", document.Inspect().Version);
        }

        [Fact]
        public async Task WithoutAClaim_NothingIsIdentified()
        {
            var config = Config(PdfUAConformance.None);
            config.EnableXmpMetadata = true;
            var (_, text) = await RenderAsync(Html, config);

            Assert.Null(Description(text).Element(PdfUaId + "part"));
        }

        [Theory]
        [InlineData(PdfUAConformance.UA1, PdfAConformance.PdfA2A, "1")]
        [InlineData(PdfUAConformance.UA1, PdfAConformance.PdfA1A, "1")]
        [InlineData(PdfUAConformance.UA2, PdfAConformance.PdfA4, "2")]
        public async Task WithPdfA_TheUaSchemaIsDeclaredAsAnExtensionSchema(PdfUAConformance level, PdfAConformance pdfA, string part)
        {
            var (_, text) = await RenderAsync(Html, Config(level, pdfA));

            var packet = XDocument.Parse(Regex.Match(text, @"<x:xmpmeta.*?</x:xmpmeta>", RegexOptions.Singleline).Value);
            var descriptions = packet.Descendants(Rdf + "Description").ToList();
            Assert.Equal(part, descriptions.Select(d => d.Element(PdfUaId + "part")?.Value).Single(v => v is not null));

            var schema = descriptions.SelectMany(d => d.Descendants(PdfaSchema + "namespaceURI")).Single(e => e.Value == PdfUaId.NamespaceName).Parent!;
            Assert.Equal("pdfuaid", schema.Element(PdfaSchema + "prefix")?.Value);
            var properties = schema.Descendants(PdfaProperty + "name").Select(e => e.Value).ToArray();
            Assert.Equal(level == PdfUAConformance.UA2 ? ["part", "rev"] : ["part"], properties);
            Assert.All(schema.Descendants(PdfaProperty + "valueType"), v => Assert.Equal("Integer", v.Value));
        }

        [Fact]
        public async Task WithoutPdfA_NoExtensionSchemaIsWritten()
        {
            var (_, text) = await RenderAsync(Html, Config(PdfUAConformance.UA1));

            Assert.DoesNotContain("pdfaExtension", text);
        }

        [Fact]
        public async Task EveryFigureHasAlternativeText_DecorativeImagesAreArtifacts()
        {
            const string gif = "data:image/gif;base64,R0lGODlhAQABAAAAACw=";
            var (document, _) = await RenderAsync(
                $"<html lang='en'><head><title>T</title></head><body><img src='{gif}' alt='Described'><img src='{gif}' alt=''><img src='{gif}'></body></html>", Config(PdfUAConformance.UA1));

            var figures = document.Inspect().StructureRoot.SelectMany(All).Where(e => e.StructureType == "/Figure").Select(e => e.AlternateText).ToArray();
            Assert.Equal(["Described"], figures);
        }

        static System.Collections.Generic.IEnumerable<StructElem> All(StructElem element) =>
            new[] { element }.Concat(element.Kids.SelectMany(All));

        // ---- what the writer refuses ---------------------------------------------------------------------------------------------------

        [Fact]
        public async Task ATitleIsRequired()
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PdfGenerator().GeneratePdf("<html lang='en'><body><p>No title</p></body></html>", Config(PdfUAConformance.UA1)));

            Assert.Contains("title", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ALanguageIsRequired()
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PdfGenerator().GeneratePdf("<html><head><title>T</title></head><body><p>No language</p></body></html>", Config(PdfUAConformance.UA1)));

            Assert.Contains("language", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ADefaultLanguageWillDo_AndSoWillATitleFromTheMetadata()
        {
            var config = Config(PdfUAConformance.UA1);
            config.DefaultLanguage = "de";
            config.Metadata!.Title = "Titel";
            var (document, _) = await RenderAsync("<html><body><p>Hallo</p></body></html>", config);

            Assert.Equal("de", document.Inspect().Language);
        }

        [Fact]
        public async Task ACreationDateIsRequiredForTheXmpPacket()
        {
            var config = Config(PdfUAConformance.UA1);
            config.Metadata = null;

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(Html, config));
            Assert.Contains("creation date", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(PdfUAConformance.UA1, PdfAConformance.PdfA4)]
        [InlineData(PdfUAConformance.UA2, PdfAConformance.PdfA2A)]
        public async Task ALevelAndAPdfALevelOfADifferentPdfVersionCannotBeClaimedTogether(PdfUAConformance level, PdfAConformance pdfA)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(Html, Config(level, pdfA)));
        }

        [Fact]
        public async Task PdfUaCannotBeCombinedWithPdfX()
        {
            var config = Config(PdfUAConformance.UA1);
            config.PdfXConformance = PdfXConformance.X4;

            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(Html, config));
        }

        [Fact]
        public async Task AnExplicitVersionThatContradictsTheLevelIsRejected()
        {
            var ua1 = Config(PdfUAConformance.UA1);
            ua1.PdfVersion = PdfVersion.Pdf20;
            var ua2 = Config(PdfUAConformance.UA2);
            ua2.PdfVersion = PdfVersion.Pdf17;

            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(Html, ua1));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(Html, ua2));
        }
    }
}
