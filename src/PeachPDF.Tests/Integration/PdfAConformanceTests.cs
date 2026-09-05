using PeachPDF.PdfSharpCore.Pdf;
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
    public class PdfAConformanceTests
    {
        const string SimpleHtml = "<html><body><p>Hello</p></body></html>";

        [Fact]
        public async Task None_Default_NoOutputIntentNoMetadataNoVersionBump()
        {
            var result = await new PdfGenerator().GeneratePdf(SimpleHtml, PageSize.A4);

            Assert.False(result.PdfDocument.Catalog.Elements.ContainsKey("/OutputIntents"));
            Assert.False(result.PdfDocument.Catalog.Elements.ContainsKey("/Metadata"));
            Assert.Equal(14, result.PdfDocument.Version);
        }

        [Theory]
        [InlineData(PdfAConformance.PdfA2B, 17)]
        [InlineData(PdfAConformance.PdfA2U, 17)]
        [InlineData(PdfAConformance.PdfA2A, 17)]
        [InlineData(PdfAConformance.PdfA3B, 17)]
        [InlineData(PdfAConformance.PdfA3U, 17)]
        [InlineData(PdfAConformance.PdfA3A, 17)]
        [InlineData(PdfAConformance.PdfA1B, 14)]
        [InlineData(PdfAConformance.PdfA1A, 14)]
        public async Task PdfVersion_MatchesConformanceLevel(PdfAConformance conformance, int expectedVersion)
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = conformance,
                DefaultLanguage = "en",
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            var result = await new PdfGenerator().GeneratePdf(SimpleHtml, config);

            Assert.Equal(expectedVersion, result.PdfDocument.Version);
        }

        [Fact]
        public async Task PdfAConformance_WritesOutputIntentWithEmbeddedIccProfile()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            var pdfText = await GetPdfText(SimpleHtml, config);

            Assert.Contains("/OutputIntents", pdfText);
            Assert.Contains("/S /GTS_PDFA1", pdfText);
            Assert.Contains("/OutputConditionIdentifier (sRGB IEC61966-2.1)", pdfText);
            Assert.Contains("/DestOutputProfile", pdfText);
            Assert.Contains("/N 3", pdfText);
        }

        [Theory]
        [InlineData(PdfAConformance.PdfA1B, "1", "B")]
        [InlineData(PdfAConformance.PdfA1A, "1", "A")]
        [InlineData(PdfAConformance.PdfA2B, "2", "B")]
        [InlineData(PdfAConformance.PdfA2U, "2", "U")]
        [InlineData(PdfAConformance.PdfA2A, "2", "A")]
        [InlineData(PdfAConformance.PdfA3B, "3", "B")]
        [InlineData(PdfAConformance.PdfA3U, "3", "U")]
        [InlineData(PdfAConformance.PdfA3A, "3", "A")]
        public async Task XmpPacket_HasCorrectPdfaidPartAndConformance(PdfAConformance conformance, string expectedPart, string expectedLevel)
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = conformance,
                DefaultLanguage = "en",
                Metadata = new PdfDocumentMetadata { Title = "My Title", CreationDate = DateTimeOffset.UtcNow },
            };
            var packet = await GetXmpPacket(SimpleHtml, config);

            var ns = XNamespace.Get("http://www.aiim.org/pdfa/ns/id/");
            var description = packet.Descendants(XNamespace.Get("http://www.w3.org/1999/02/22-rdf-syntax-ns#") + "Description").Single();

            Assert.Equal(expectedPart, description.Element(ns + "part")?.Value);
            Assert.Equal(expectedLevel, description.Element(ns + "conformance")?.Value);

            var dc = XNamespace.Get("http://purl.org/dc/elements/1.1/");
            var titleLi = description
                .Element(dc + "title")?
                .Element(XNamespace.Get("http://www.w3.org/1999/02/22-rdf-syntax-ns#") + "Alt")?
                .Element(XNamespace.Get("http://www.w3.org/1999/02/22-rdf-syntax-ns#") + "li");
            Assert.Equal("My Title", titleLi?.Value);
        }

        [Fact]
        public async Task XmpPacket_IncludesAuthorSubjectAndKeywords()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                EnableXmpMetadata = true,
                Metadata = new PdfDocumentMetadata
                {
                    Author = "Ada Lovelace",
                    Subject = "A test subject",
                    Keywords = "pdf, test",
                    CreationDate = DateTimeOffset.UtcNow,
                },
            };
            var packet = await GetXmpPacket(SimpleHtml, config);
            var rdfNs = XNamespace.Get("http://www.w3.org/1999/02/22-rdf-syntax-ns#");
            var description = packet.Descendants(rdfNs + "Description").Single();

            var dc = XNamespace.Get("http://purl.org/dc/elements/1.1/");
            var pdfNs = XNamespace.Get("http://ns.adobe.com/pdf/1.3/");

            Assert.Equal("Ada Lovelace", description.Element(dc + "creator")?.Element(rdfNs + "Seq")?.Element(rdfNs + "li")?.Value);
            Assert.Equal("A test subject", description.Element(dc + "description")?.Element(rdfNs + "Alt")?.Element(rdfNs + "li")?.Value);
            Assert.Equal("pdf, test", description.Element(pdfNs + "Keywords")?.Value);
        }

        [Fact]
        public async Task EnableXmpMetadata_WithoutPdfA_WritesMetadataWithNoPdfaidAndNoOutputIntent()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                EnableXmpMetadata = true,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            var result = await new PdfGenerator().GeneratePdf(SimpleHtml, config);

            Assert.True(result.PdfDocument.Catalog.Elements.ContainsKey("/Metadata"));
            Assert.False(result.PdfDocument.Catalog.Elements.ContainsKey("/OutputIntents"));

            var packet = await GetXmpPacket(SimpleHtml, config);
            var ns = XNamespace.Get("http://www.aiim.org/pdfa/ns/id/");
            var description = packet.Descendants(XNamespace.Get("http://www.w3.org/1999/02/22-rdf-syntax-ns#") + "Description").Single();
            Assert.Null(description.Element(ns + "part"));
        }

        [Fact]
        public async Task PdfAConformance_ForcesXmpMetadataOn_EvenWhenEnableXmpMetadataFalse()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2B,
                EnableXmpMetadata = false,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            var result = await new PdfGenerator().GeneratePdf(SimpleHtml, config);

            Assert.True(result.PdfDocument.Catalog.Elements.ContainsKey("/Metadata"));
            Assert.False(config.EnableXmpMetadata); // caller's config object is never mutated
        }

        [Fact]
        public async Task CustomXmpProperties_AppearInPacket_AlongsidePdfaidEntries()
        {
            var customNs = XNamespace.Get("urn:example:custom");
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            config.Metadata.CustomXmpProperties.Add(new XElement(customNs + "widget", "42"));

            var packet = await GetXmpPacket(SimpleHtml, config);
            var custom = packet.Descendants(customNs + "widget").SingleOrDefault();

            Assert.NotNull(custom);
            Assert.Equal("42", custom!.Value);

            // Composes with, doesn't replace, the required pdfaid entries.
            var pdfaidNs = XNamespace.Get("http://www.aiim.org/pdfa/ns/id/");
            Assert.NotNull(packet.Descendants(pdfaidNs + "part").SingleOrDefault());
        }

        [Fact]
        public async Task MissingCreationDate_WithXmpMetadataEnabled_Throws()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableXmpMetadata = true };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        [Fact]
        public async Task AccessibleConformance_WithoutLanguage_Throws()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2A,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        [Fact]
        public async Task AccessibleConformance_SetsMarkInfoMarkedAndLanguage_ForcesTaggingOnWithoutMutatingConfig()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2A,
                DefaultLanguage = "en-US",
                EnableTaggedPdf = false,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            var result = await new PdfGenerator().GeneratePdf(SimpleHtml, config);

            Assert.True(result.PdfDocument.Catalog.MarkInfo.Marked);
            Assert.Equal("en-US", result.PdfDocument.Catalog.Language);
            Assert.True(result.PdfDocument.Catalog.Elements.ContainsKey("/StructTreeRoot"));
            Assert.False(config.EnableTaggedPdf); // caller's config object is never mutated
        }

        [Fact]
        public async Task AccessibleConformance_ImageWithoutAlt_GetsEmptyAlt()
        {
            const string tinyPngBase64 =
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
            var html = $"<html><body><img src='data:image/png;base64,{tinyPngBase64}' /></body></html>";

            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2A,
                DefaultLanguage = "en",
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            var result = await new PdfGenerator().GeneratePdf(html, config);

            var documentElement = PeachPDF.PdfSharpCore.Pdf.Structure.PdfStructureElement
                .GetKids(result.PdfDocument.Catalog.StructureTreeRoot.Elements)
                .Cast<PeachPDF.PdfSharpCore.Pdf.Structure.PdfStructureElement>()
                .Single();
            var figure = PeachPDF.PdfSharpCore.Pdf.Structure.PdfStructureElement
                .GetKids(documentElement.Elements)
                .Cast<PeachPDF.PdfSharpCore.Pdf.Structure.PdfStructureElement>()
                .Single();

            Assert.Equal("/Figure", figure.StructureType);
            Assert.Equal(string.Empty, figure.AlternateText);
        }

        [Theory]
        [InlineData("<div style=\"width: 50px; height: 50px; background: #ff0000; opacity: 0.5;\"></div>", "opacity")]
        [InlineData("""<svg viewBox="0 0 100 100" width="100" height="100"><rect width="50" height="50" fill="url(#g)"/><defs><linearGradient id="g"><stop offset="0" stop-color="red"/><stop offset="1" stop-color="blue" stop-opacity="0.5"/></linearGradient></defs></svg>""", "gradient")]
        [InlineData("""<svg viewBox="0 0 100 100" width="100" height="100"><rect width="50" height="50" fill="red" fill-opacity="0.5"/></svg>""", "fill-opacity")]
        [InlineData("<div style=\"width: 50px; height: 50px; outline: 2px solid invert;\"></div>", "outline-color: invert (blend mode)")]
        public async Task PdfA1B_TransparencyRequiringContent_Throws(string bodyHtml, string _)
        {
            var html = $"<html><body>{bodyHtml}</body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA1B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };

            // Thrown deep inside painting (an internal PdfAConformanceException subclass, so
            // FragmentPainter's generic paint-error wrapping doesn't fold it into an
            // HtmlRenderException) - ThrowsAnyAsync checks assignability rather than exact type,
            // matching how a real caller's "catch (InvalidOperationException)" would see it.
            await Assert.ThrowsAnyAsync<InvalidOperationException>(
                () => new PdfGenerator().GeneratePdf(html, config));
        }

        [Fact]
        public async Task PdfA1B_ImageWithAlphaChannel_Throws()
        {
            // A genuinely semi-transparent pixel (not fully 0/255 alpha) is required to reach the
            // /SMask path in PdfImage.cs - a fully-opaque or fully-transparent-only image takes a
            // different (unaffected) code path.
            var pngBytes = RasterPngFixture.MakeSolidRgbaPngBytes(4, 4, 255, 0, 0, a: 128);
            var html = $"<html><body><img src=\"data:image/png;base64,{Convert.ToBase64String(pngBytes)}\" width=\"4\" height=\"4\" /></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA1B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };

            await Assert.ThrowsAnyAsync<InvalidOperationException>(
                () => new PdfGenerator().GeneratePdf(html, config));
        }

        [Fact]
        public async Task AddPdfPages_DifferentPdfAConformanceAcrossCalls_Throws()
        {
            var document = await new PdfGenerator().GeneratePdf(SimpleHtml, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            });

            var generator = new PdfGenerator();
            await Assert.ThrowsAsync<InvalidOperationException>(() => generator.AddPdfPages(
                document,
                SimpleHtml,
                new PdfGenerateConfig { PageSize = PageSize.A4, PdfAConformance = PdfAConformance.None },
                null));
        }

        [Fact]
        public async Task AddPdfPages_SamePdfAConformanceAcrossCalls_Succeeds_WithOneOutputIntent()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(SimpleHtml, config);
            await generator.AddPdfPages(document, SimpleHtml, config, null);

            var outputIntents = document.PdfDocument.Catalog.Elements.GetArray("/OutputIntents");
            Assert.NotNull(outputIntents);
            Assert.Single(outputIntents!.Elements);
            Assert.Equal(2, document.PdfDocument.Pages.Count);
        }

        [Fact]
        public async Task CreationDate_InfoAndXmpAgreeOnTheSameInstant_RegardlessOfLocalTimeZone()
        {
            // A non-round-hour UTC offset makes the bug (Info /CreationDate silently re-labeled with
            // the machine's own local offset) visible regardless of what time zone the test happens to
            // run in - only a machine that happens to be at exactly this same offset would coincidentally
            // pass a buggy implementation.
            var creationDate = new DateTimeOffset(2024, 6, 15, 12, 30, 0, TimeSpan.FromHours(5.5));
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                EnableXmpMetadata = true,
                Metadata = new PdfDocumentMetadata { CreationDate = creationDate },
            };
            var result = await new PdfGenerator().GeneratePdf(SimpleHtml, config);

            Assert.Equal(creationDate.UtcDateTime, result.PdfDocument.Info.CreationDate.ToUniversalTime());

            var packet = await GetXmpPacket(SimpleHtml, config);
            var xmpNs = XNamespace.Get("http://ns.adobe.com/xap/1.0/");
            var rdfNs = XNamespace.Get("http://www.w3.org/1999/02/22-rdf-syntax-ns#");
            var xmpCreateDateText = packet.Descendants(rdfNs + "Description").Single().Element(xmpNs + "CreateDate")!.Value;
            var xmpCreateDate = DateTimeOffset.Parse(xmpCreateDateText, System.Globalization.CultureInfo.InvariantCulture);

            Assert.Equal(creationDate.UtcDateTime, xmpCreateDate.UtcDateTime);
        }

        [Fact]
        public async Task PdfA1B_PlainOpaqueContent_Succeeds_WithNoGroupOnPage()
        {
            var html = "<html><body><div style=\"width: 50px; height: 50px; background: #ff0000;\"></div></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA1B,
                CompressContentStreams = false,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            var result = await new PdfGenerator().GeneratePdf(html, config);

            // /Group is only added to Elements by PdfPage.WriteObject, called from Save() - saving is
            // what actually exercises the fixed (no-longer-unconditional) TransparencyUsed logic.
            using var ms = new MemoryStream();
            result.Save(ms);
            Assert.False(result.PdfDocument.Pages[0].Elements.ContainsKey("/Group"));
        }

        [Fact]
        public async Task PdfA2B_OpacityContent_Succeeds_TransparencyPermitted()
        {
            var html = "<html><body><div style=\"width: 50px; height: 50px; background: #ff0000; opacity: 0.5;\"></div></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };

            // Must not throw - PDF/A-2 permits transparency groups.
            var result = await new PdfGenerator().GeneratePdf(html, config);
            using var ms = new MemoryStream();
            result.Save(ms);
            Assert.True(result.PdfDocument.Pages[0].Elements.ContainsKey("/Group"));
        }

        [Fact]
        public async Task NoEncryption_NoLzw_RegardlessOfPdfAConformance()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            var pdfText = await GetPdfText(SimpleHtml, config);

            Assert.DoesNotContain("/Encrypt", pdfText);
            Assert.DoesNotContain("/LZWDecode", pdfText);
        }

        // --- Helpers ---

        static async Task<string> GetPdfText(string html, PdfGenerateConfig config)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        static async Task<XDocument> GetXmpPacket(string html, PdfGenerateConfig config)
        {
            var pdfText = await GetPdfText(html, config);
            var match = Regex.Match(pdfText, @"<x:xmpmeta.*?</x:xmpmeta>", RegexOptions.Singleline);
            Assert.True(match.Success, "No XMP packet found in generated PDF.");
            return XDocument.Parse(match.Value);
        }
    }
}
