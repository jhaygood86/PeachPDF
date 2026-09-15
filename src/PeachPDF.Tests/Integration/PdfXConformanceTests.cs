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
    /// PDF/X (ISO 15930) print-production conformance - <see cref="PeachPDF.PdfXConformance"/>. Covers the
    /// mandatory output intent, X1a's CMYK-only content restriction (with the achromatic-RGB TrueBlack/
    /// RichBlack exception), X1a/X3's transparency rejection, X4 permitting both RGB and transparency, and
    /// mutual exclusivity with <see cref="PdfAConformance"/>.
    /// </summary>
    public class PdfXConformanceTests
    {
        const string SimpleHtml = "<html><body><p>Hello</p></body></html>";

        private static async Task<string> GetPdfText(string html, PdfGenerateConfig config)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        // A resolvable creation date is required on every level now that PdfXConformance also triggers
        // XMP metadata writing (PDF/X-4's GTS_PDFXVersion identification is primarily an XMP property -
        // see PdfMetadataStream's remarks) - SimpleHtml has no extractable date of its own.
        private static readonly PdfDocumentMetadata TestMetadata = new() { CreationDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) };

        private static PdfGenerateConfig X1aConfig(byte[]? profile = null) => new()
        {
            PageSize = PageSize.A4,
            PdfXConformance = PdfXConformance.X1a,
            Metadata = TestMetadata,
            ColorOptions = new ColorOptions
            {
                OutputIntentProfile = profile ?? IccProfileFixture.BuildCmykProfile(),
                OutputIntentIdentifier = "Test CMYK Profile",
            },
        };

        private static PdfGenerateConfig X3Config(byte[]? profile = null) => new()
        {
            PageSize = PageSize.A4,
            PdfXConformance = PdfXConformance.X3,
            Metadata = TestMetadata,
            ColorOptions = new ColorOptions
            {
                OutputIntentProfile = profile ?? IccProfileFixture.BuildRgbProfile(),
                OutputIntentIdentifier = "Test RGB Profile",
            },
        };

        private static PdfGenerateConfig X4Config(byte[]? profile = null) => new()
        {
            PageSize = PageSize.A4,
            PdfXConformance = PdfXConformance.X4,
            Metadata = TestMetadata,
            ColorOptions = new ColorOptions
            {
                OutputIntentProfile = profile ?? IccProfileFixture.BuildRgbProfile(),
                OutputIntentIdentifier = "Test RGB Profile",
            },
        };

        static async Task<XDocument> GetXmpPacket(string html, PdfGenerateConfig config)
        {
            var pdfText = await GetPdfText(html, config);
            var match = Regex.Match(pdfText, @"<x:xmpmeta.*?</x:xmpmeta>", RegexOptions.Singleline);
            Assert.True(match.Success, "No XMP packet found in generated PDF.");
            return XDocument.Parse(match.Value);
        }

        [Fact]
        public async Task None_Default_NoOutputIntent()
        {
            var pdfText = await GetPdfText(SimpleHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });
            Assert.DoesNotContain("/OutputIntents", pdfText);
        }

        [Fact]
        public async Task MissingOutputIntentProfile_Throws()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, PdfXConformance = PdfXConformance.X4 };
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        [Fact]
        public async Task MissingOutputIntentIdentifier_Throws()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfXConformance = PdfXConformance.X4,
                ColorOptions = new ColorOptions { OutputIntentProfile = IccProfileFixture.BuildRgbProfile() },
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        [Fact]
        public async Task X1a_WithRgbOutputIntentProfile_Throws()
        {
            var config = X1aConfig(IccProfileFixture.BuildRgbProfile());
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        [Fact]
        public async Task X4_UnparsableOutputIntentProfile_StillSucceeds_FallsBackToFourChannels()
        {
            // X3/X4 don't validate the output-intent profile's color space (unlike X1a's CMYK
            // requirement), so an unparsable profile still embeds - PdfOutputIntent.ResolveChannelCount
            // falls back to /N 4 rather than throwing when PeachImage.IccColorProfile.TryCreate fails.
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfXConformance = PdfXConformance.X4,
                Metadata = TestMetadata,
                ColorOptions = new ColorOptions
                {
                    OutputIntentProfile = [0x00, 0x01, 0x02, 0x03],
                    OutputIntentIdentifier = "Not a real ICC profile",
                },
            };
            var pdfText = await GetPdfText(SimpleHtml, config);
            Assert.Contains("/OutputIntents", pdfText);
            Assert.Contains("/N 4", pdfText);
        }

        [Fact]
        public async Task X3_WithRgbOutputIntentProfile_Succeeds()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfXConformance = PdfXConformance.X3,
                Metadata = TestMetadata,
                ColorOptions = new ColorOptions
                {
                    OutputIntentProfile = IccProfileFixture.BuildRgbProfile(),
                    OutputIntentIdentifier = "Test RGB Profile",
                },
            };
            var pdfText = await GetPdfText(SimpleHtml, config);
            Assert.Contains("/OutputIntents", pdfText);
            Assert.Contains("/GTS_PDFX", pdfText);
        }

        [Fact]
        public async Task X1a_ValidCmykProfile_EmbedsOutputIntent()
        {
            var pdfText = await GetPdfText(SimpleHtml, X1aConfig());
            Assert.Contains("/OutputIntents", pdfText);
            Assert.Contains("/GTS_PDFX", pdfText);
        }

        [Fact]
        public async Task X1a_ChromaticRgbTextColor_Throws()
        {
            // Thrown as the internal PdfXConformanceException subclass (so FragmentPainter's generic
            // paint-error wrapping doesn't fold it into an HtmlRenderException) - ThrowsAnyAsync checks
            // assignability, matching how a real caller's "catch (InvalidOperationException)" sees it.
            var html = "<html><body><p style=\"color: red\">Hello</p></body></html>";
            var config = X1aConfig();
            var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(html, config));
            Assert.Contains("PDF/X-1a", ex.Message);
        }

        [Fact]
        public async Task X1a_DeviceCmykTextColor_Succeeds()
        {
            var html = "<html><body><p style=\"color: device-cmyk(0 1 1 0)\">Hello</p></body></html>";
            var config = X1aConfig();
            config.CompressContentStreams = false;
            var pdfText = await GetPdfText(html, config);

            Assert.Matches(new Regex(@"0(\.0+)?\s+1(\.0+)?\s+1(\.0+)?\s+0(\.0+)?\s+k\b"), pdfText);
        }

        [Fact]
        public async Task X1a_AchromaticRgbTextColor_ConvertsToTrueBlack()
        {
            var html = "<html><body style=\"margin:0\"><p style=\"color: black; font-size: 20pt\">Hello</p></body></html>";
            var config = X1aConfig();
            config.CompressContentStreams = false;
            var pdfText = await GetPdfText(html, config);

            // black (RGB 0,0,0) -> TrueBlack: C=0 M=0 Y=0 K=1.
            Assert.Matches(new Regex(@"0(\.0+)?\s+0(\.0+)?\s+0(\.0+)?\s+1(\.0+)?\s+k\b"), pdfText);
        }

        [Fact]
        public async Task X1a_AchromaticRgbTextColor_UseRichBlack_AddsCmy()
        {
            var html = "<html><body style=\"margin:0\"><p style=\"color: black; font-size: 20pt\">Hello</p></body></html>";
            var config = X1aConfig();
            config.CompressContentStreams = false;
            config.ColorOptions!.BlackGeneration = ColorBlackGeneration.UseRichBlack;
            var pdfText = await GetPdfText(html, config);

            // Rich black for pure black: C=0.6 M=0.4 Y=0.4 K=1.
            Assert.Matches(new Regex(@"0\.6\d*\s+0\.4\d*\s+0\.4\d*\s+1(\.0+)?\s+k\b"), pdfText);
        }

        [Fact]
        public async Task X1a_SemiTransparentContent_Throws()
        {
            var html = "<html><body><div style=\"width:10px;height:10px;background:device-cmyk(0 0 0 1);opacity:0.5\"></div></body></html>";
            var config = X1aConfig();
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(html, config));
        }

        [Fact]
        public async Task X4_SemiTransparentContent_Succeeds()
        {
            var html = "<html><body><div style=\"width:10px;height:10px;background:red;opacity:0.5\"></div></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfXConformance = PdfXConformance.X4,
                Metadata = TestMetadata,
                ColorOptions = new ColorOptions
                {
                    OutputIntentProfile = IccProfileFixture.BuildCmykProfile(),
                    OutputIntentIdentifier = "Test CMYK Profile",
                },
            };
            var pdfText = await GetPdfText(html, config);
            Assert.Contains("/OutputIntents", pdfText);
        }

        [Fact]
        public async Task BothPdfAAndPdfXConformance_Throws()
        {
            var config = X1aConfig();
            config.PdfAConformance = PdfAConformance.PdfA2B;
            config.Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow };
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        [Fact]
        public async Task RepeatedAddPdfPages_SameConformance_Succeeds()
        {
            var document = new PeachPdfDocument(new PdfSharpCore.Pdf.PdfDocument());
            var generator = new PdfGenerator();
            await generator.AddPdfPages(document, SimpleHtml, X1aConfig());
            await generator.AddPdfPages(document, SimpleHtml, X1aConfig());

            using var ms = new MemoryStream();
            document.Save(ms);
        }

        [Fact]
        public async Task RepeatedAddPdfPages_DifferentConformance_Throws()
        {
            var document = new PeachPdfDocument(new PdfSharpCore.Pdf.PdfDocument());
            var generator = new PdfGenerator();
            await generator.AddPdfPages(document, SimpleHtml, X1aConfig());

            var x4Config = X1aConfig();
            x4Config.PdfXConformance = PdfXConformance.X4;
            await Assert.ThrowsAsync<InvalidOperationException>(() => generator.AddPdfPages(document, SimpleHtml, x4Config));
        }

        // ── Version header + GTS_PDFXVersion/GTS_PDFXConformance identification ────────────────
        // PeachPDF's PdfXConformance levels target the 2003-era ISO revisions (ISO 15930-4/6, PDF 1.4
        // base) for X1a/X3; X4 (ISO 15930-7) targets PDF 1.6 - see PdfMetadataStream.PdfXIdentifiers's
        // remarks for the exact identifier strings and how they were verified (the real ISO 15930 text is
        // paywalled).

        [Fact]
        public async Task X1a_UsesPdf14VersionHeader()
        {
            var result = await new PdfGenerator().GeneratePdf(SimpleHtml, X1aConfig());
            Assert.Equal(14, result.PdfDocument.Version);

            var pdfText = await GetPdfText(SimpleHtml, X1aConfig());
            Assert.StartsWith("%PDF-1.4\n", pdfText);
        }

        [Fact]
        public async Task X1a_InfoDictionaryHasVersionAndConformanceKeys()
        {
            var pdfText = await GetPdfText(SimpleHtml, X1aConfig());

            Assert.Matches(new Regex(@"/GTS_PDFXVersion\s*\(PDF/X-1a:2003\)"), pdfText);
            Assert.Matches(new Regex(@"/GTS_PDFXConformance\s*\(PDF/X-1a:2003\)"), pdfText);
        }

        [Fact]
        public async Task X1a_XmpPacketHasPdfxVersionAndConformance()
        {
            var packet = await GetXmpPacket(SimpleHtml, X1aConfig());
            var pdfxNs = XNamespace.Get("http://ns.adobe.com/pdfx/1.3/");

            Assert.Equal("PDF/X-1a:2003", packet.Descendants(pdfxNs + "GTS_PDFXVersion").Single().Value);
            Assert.Equal("PDF/X-1a:2003", packet.Descendants(pdfxNs + "GTS_PDFXConformance").Single().Value);
        }

        [Fact]
        public async Task X3_UsesPdf14VersionHeader_AndNoGtsPdfxConformanceKey()
        {
            var result = await new PdfGenerator().GeneratePdf(SimpleHtml, X3Config());
            Assert.Equal(14, result.PdfDocument.Version);

            var pdfText = await GetPdfText(SimpleHtml, X3Config());
            Assert.StartsWith("%PDF-1.4\n", pdfText);
            Assert.Matches(new Regex(@"/GTS_PDFXVersion\s*\(PDF/X-3:2003\)"), pdfText);
            Assert.DoesNotContain("/GTS_PDFXConformance", pdfText);
        }

        [Fact]
        public async Task X3_XmpPacketHasPdfxVersionOnly()
        {
            var packet = await GetXmpPacket(SimpleHtml, X3Config());
            var pdfxNs = XNamespace.Get("http://ns.adobe.com/pdfx/1.3/");

            Assert.Equal("PDF/X-3:2003", packet.Descendants(pdfxNs + "GTS_PDFXVersion").Single().Value);
            Assert.Empty(packet.Descendants(pdfxNs + "GTS_PDFXConformance"));
        }

        [Fact]
        public async Task X4_UsesPdf16VersionHeader()
        {
            var result = await new PdfGenerator().GeneratePdf(SimpleHtml, X4Config());
            Assert.Equal(16, result.PdfDocument.Version);

            var pdfText = await GetPdfText(SimpleHtml, X4Config());
            Assert.StartsWith("%PDF-1.6\n", pdfText);
            Assert.Matches(new Regex(@"/GTS_PDFXVersion\s*\(PDF/X-4\)"), pdfText);
            Assert.DoesNotContain("/GTS_PDFXConformance", pdfText);
        }

        [Fact]
        public async Task X4_XmpPacketHasBothPdfxAndPdfxidVersion()
        {
            var packet = await GetXmpPacket(SimpleHtml, X4Config());
            var pdfxNs = XNamespace.Get("http://ns.adobe.com/pdfx/1.3/");
            var pdfxidNs = XNamespace.Get("http://www.npes.org/pdfx/ns/id/");

            Assert.Equal("PDF/X-4", packet.Descendants(pdfxNs + "GTS_PDFXVersion").Single().Value);
            Assert.Equal("PDF/X-4", packet.Descendants(pdfxidNs + "GTS_PDFXVersion").Single().Value);
        }

        [Fact]
        public async Task Pdf20WithPdfXConformance_Throws()
        {
            var config = X1aConfig();
            config.PdfVersion = PdfVersion.Pdf20;
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        // ConversionMode is real, implemented ICC conversion now (Phase B, PeachImage 0.4.4's
        // IccColorProfile.ConvertTo) - see PdfColorConversionTests.cs for its coverage, including
        // GrayscaleViaK_MissingFallbackProfile_Throws (this class's old
        // ConversionMode_NotPreserveAsAuthored_Throws test pinned the earlier Phase A blanket
        // NotSupportedException, which no longer applies).
    }
}
