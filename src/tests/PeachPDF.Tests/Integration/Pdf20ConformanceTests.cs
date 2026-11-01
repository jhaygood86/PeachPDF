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
    /// <summary>
    /// The two conformance levels defined against PDF 2.0: PDF/A-4 (ISO 19005-4, with its e and f variants) and PDF/X-6
    /// (ISO 15930-9). Both pick PDF 2.0 on their own, carry their identification in XMP only, and use ISO filters only.
    /// </summary>
    public class Pdf20ConformanceTests
    {
        const string SimpleHtml = "<html><body><p>Hello</p></body></html>";

        private static readonly PdfDocumentMetadata TestMetadata = new() { CreationDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) };

        private static readonly XNamespace PdfaidNs = "http://www.aiim.org/pdfa/ns/id/";
        private static readonly XNamespace PdfxNs = "http://ns.adobe.com/pdfx/1.3/";
        private static readonly XNamespace PdfxidNs = "http://www.npes.org/pdfx/ns/id/";

        private static PdfGenerateConfig A4Config(PdfAConformance level = PdfAConformance.PdfA4) => new()
        {
            PageSize = PageSize.A4,
            PdfAConformance = level,
            Metadata = TestMetadata,
        };

        private static PdfGenerateConfig X6Config() => new()
        {
            PageSize = PageSize.A4,
            PdfXConformance = PdfXConformance.X6,
            Metadata = TestMetadata,
            ColorOptions = new ColorOptions
            {
                OutputIntentProfile = IccProfileFixture.BuildRgbProfile(),
                OutputIntentIdentifier = "Test RGB Profile",
            },
        };

        private static async Task<(string Text, byte[] Bytes)> RenderAsync(string html, PdfGenerateConfig config)
        {
            var doc = await new PdfGenerator().GeneratePdf(html, config);
            using var ms = new MemoryStream();
            doc.Save(ms);
            return (Encoding.Latin1.GetString(ms.ToArray()), ms.ToArray());
        }

        private static async Task<XElement> XmpAsync(string html, PdfGenerateConfig config)
        {
            var (text, _) = await RenderAsync(html, config);
            return XElement.Parse(Regex.Match(text, @"<x:xmpmeta.*?</x:xmpmeta>", RegexOptions.Singleline).Value);
        }

        // ---------------- PDF/A-4 ----------------

        [Theory]
        [InlineData(PdfAConformance.PdfA4, null)]
        [InlineData(PdfAConformance.PdfA4E, "E")]
        [InlineData(PdfAConformance.PdfA4F, "F")]
        public async Task PdfA4_SelectsPdf20_AndIdentifiesPartRevAndLevelInXmp(PdfAConformance level, string? conformance)
        {
            var config = A4Config(level);
            if (level == PdfAConformance.PdfA4F)
                config.Attachments.Add(new PdfAttachment { FileName = "data.csv", Data = [1], MimeType = "text/csv", Relationship = PdfAttachmentRelationship.Data });
            var (text, _) = await RenderAsync(SimpleHtml, config);
            var packet = await XmpAsync(SimpleHtml, config);

            Assert.StartsWith("%PDF-2.0\n", text);
            Assert.Equal("4", packet.Descendants(PdfaidNs + "part").Single().Value);
            Assert.Equal("2020", packet.Descendants(PdfaidNs + "rev").Single().Value);
            if (conformance is null)
                Assert.Empty(packet.Descendants(PdfaidNs + "conformance"));
            else
                Assert.Equal(conformance, packet.Descendants(PdfaidNs + "conformance").Single().Value);
        }

        [Fact]
        public async Task PdfA4_OmitsTheInfoDictionary_ButKeepsMetadataInXmp()
        {
            var config = A4Config();
            config.Metadata = new PdfDocumentMetadata { CreationDate = TestMetadata.CreationDate, Title = "A Title" };
            var (text, _) = await RenderAsync(SimpleHtml, config);
            var packet = await XmpAsync(SimpleHtml, config);

            // (The output intent dictionary has its own, unrelated /Info key - look at the trailer only.)
            Assert.DoesNotContain("/Info", text[text.LastIndexOf("trailer", StringComparison.Ordinal)..]);
            Assert.DoesNotContain("/Producer", text);
            Assert.DoesNotContain("/CreationDate", text);
            Assert.Contains("A Title", packet.ToString());
            Assert.Contains("/ID", text);
        }

        [Fact]
        public async Task PdfA4_WritesTheGtsPdfa1OutputIntent()
        {
            var (text, _) = await RenderAsync(SimpleHtml, A4Config());

            Assert.Contains("/GTS_PDFA1", text);
        }

        [Fact]
        public async Task PdfA4_ForcesBrotliOff_EvenWhenRequested()
        {
            var config = A4Config();
            config.BrotliCompression = true;
            var (text, _) = await RenderAsync(SimpleHtml, config);

            Assert.DoesNotContain("/BrotliDecode", text);
            Assert.Contains("/FlateDecode", text);
        }

        [Fact]
        public async Task PdfA4_ForcesJxlPassthroughOff_EvenWhenRequested()
        {
            var config = A4Config();
            config.JxlPassthrough = true;
            var (text, _) = await RenderAsync($"<html><body><img src='{JxlFixtures.DataUri("rgb_lossy")}'></body></html>", config);

            Assert.DoesNotContain("/JXLDecode", text);
            Assert.Contains("/Subtype /Image", text);
        }

        [Fact]
        public async Task PdfA4_AllowsTransparency()
        {
            var (text, _) = await RenderAsync("<html><body><div style='opacity:.5;background:red;height:20pt'>x</div></body></html>", A4Config());

            Assert.Contains("%PDF-2.0", text);
        }

        [Fact]
        public async Task PdfA4_ExplicitPdf17_Throws()
        {
            var config = A4Config();
            config.PdfVersion = PdfVersion.Pdf17;

            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        [Fact]
        public async Task PdfA4_ExplicitPdf20_IsFine()
        {
            var config = A4Config();
            config.PdfVersion = PdfVersion.Pdf20;
            var (text, _) = await RenderAsync(SimpleHtml, config);

            Assert.StartsWith("%PDF-2.0\n", text);
        }

        [Fact]
        public async Task PdfA2_WithExplicitPdf20_StillThrows()
        {
            var config = A4Config(PdfAConformance.PdfA2B);
            config.PdfVersion = PdfVersion.Pdf20;

            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        [Theory]
        [InlineData(PdfAConformance.PdfA4)]
        [InlineData(PdfAConformance.PdfA4E)]
        public async Task Attachments_AreRejectedBelowPdfA4F(PdfAConformance level)
        {
            var config = A4Config(level);
            config.Attachments.Add(new PdfAttachment { FileName = "data.csv", Data = [1, 2, 3], MimeType = "text/csv" });

            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        [Fact]
        public async Task PdfA4F_WithoutAnAttachment_Throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(SimpleHtml, A4Config(PdfAConformance.PdfA4F)));
        }

        [Fact]
        public async Task PdfA4F_AllowsAttachments()
        {
            var config = A4Config(PdfAConformance.PdfA4F);
            config.Attachments.Add(new PdfAttachment
            {
                FileName = "data.csv",
                Data = [1, 2, 3],
                MimeType = "text/csv",
                Relationship = PdfAttachmentRelationship.Data,
            });
            var (text, _) = await RenderAsync(SimpleHtml, config);

            Assert.Contains("/EmbeddedFile", text);
            Assert.Contains("/AFRelationship", text);
        }

        // ---------------- PDF/X-6 ----------------

        [Fact]
        public async Task X6_SelectsPdf20_AndIdentifiesItselfOnlyUnderPdfxidInXmp()
        {
            var (text, _) = await RenderAsync(SimpleHtml, X6Config());
            var packet = await XmpAsync(SimpleHtml, X6Config());

            Assert.StartsWith("%PDF-2.0\n", text);
            Assert.Equal("PDF/X-6", packet.Descendants(PdfxidNs + "GTS_PDFXVersion").Single().Value);
            Assert.Empty(packet.Descendants(PdfxNs + "GTS_PDFXVersion"));
        }

        [Fact]
        public async Task X6_WritesNoGtsKeysInTheInfoDictionary()
        {
            var (text, _) = await RenderAsync(SimpleHtml, X6Config());

            Assert.DoesNotContain("/GTS_PDFXVersion", text);
            Assert.DoesNotContain("/GTS_PDFXConformance", text);
            Assert.Contains("/GTS_PDFX", text); // the output intent subtype
        }

        [Fact]
        public async Task X6_AllowsTransparency_AndForcesNonIsoFiltersOff()
        {
            var config = X6Config();
            config.BrotliCompression = true;
            config.JxlPassthrough = true;
            var (text, _) = await RenderAsync(
                $"<html><body><div style='opacity:.5;background:red;height:20pt'>x</div><img src='{JxlFixtures.DataUri("rgb_lossy")}'></body></html>", config);

            Assert.DoesNotContain("/BrotliDecode", text);
            Assert.DoesNotContain("/JXLDecode", text);
        }

        [Fact]
        public async Task X6_ExplicitPdf17_Throws()
        {
            var config = X6Config();
            config.PdfVersion = PdfVersion.Pdf17;

            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        [Fact]
        public async Task X6_WithoutAnOutputIntentProfile_Throws()
        {
            var config = X6Config();
            config.ColorOptions = null;

            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        [Fact]
        public async Task X4_WithExplicitPdf20_StillThrows()
        {
            var config = X6Config();
            config.PdfXConformance = PdfXConformance.X4;
            config.PdfVersion = PdfVersion.Pdf20;

            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }
    }
}
