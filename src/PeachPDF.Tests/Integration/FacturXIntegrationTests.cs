using PeachPDF.Layout;
using PeachPDF.Tests.TestSupport;
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;
using static PeachPDF.Tests.TestSupport.PdfObjectReader;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end tests for <see cref="PdfGenerateConfig.FacturX"/>, against the shape the Factur-X 1.09.2 /
    /// ZUGFeRD 2.5.2 specification prescribes: the embedded file and its relationship, and the XMP packet -
    /// an extension-schema description plus the fx: values, as separate rdf:Description blocks.
    /// </summary>
    public class FacturXIntegrationTests
    {
        const string SimpleHtml = "<html><body><p>Invoice</p></body></html>";

        static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        static readonly XNamespace Fx = "urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#";
        static readonly XNamespace PdfaExtension = "http://www.aiim.org/pdfa/ns/extension/";
        static readonly XNamespace PdfaSchema = "http://www.aiim.org/pdfa/ns/schema#";
        static readonly XNamespace PdfaProperty = "http://www.aiim.org/pdfa/ns/property#";

        const string MinimumGuideline = "urn:factur-x.eu:1p0:minimum";
        const string BasicWlGuideline = "urn:factur-x.eu:1p0:basicwl";
        const string BasicGuideline = "urn:cen.eu:en16931:2017#compliant#urn:factur-x.eu:1p0:basic";
        const string En16931Guideline = "urn:cen.eu:en16931:2017";
        const string ExtendedGuideline = "urn:cen.eu:en16931:2017#conformant#urn:factur-x.eu:1p0:extended";
        const string XRechnungGuideline = "urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0";

        /// <summary>The smallest Cross Industry Invoice that declares a guideline - all profile derivation reads.</summary>
        static byte[] Cii(string? guideline) => Encoding.UTF8.GetBytes(
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <rsm:CrossIndustryInvoice xmlns:rsm="urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100"
                                      xmlns:ram="urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100">
              <rsm:ExchangedDocumentContext>
                {(guideline is null ? "" : $"<ram:GuidelineSpecifiedDocumentContextParameter><ram:ID>{guideline}</ram:ID></ram:GuidelineSpecifiedDocumentContextParameter>")}
              </rsm:ExchangedDocumentContext>
            </rsm:CrossIndustryInvoice>
            """);

        static PdfGenerateConfig Config(byte[] xml, FacturXProfile? profile = null, PdfAttachmentRelationship? relationship = null)
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA3B,
                Metadata = new PdfDocumentMetadata { CreationDate = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero) },
                FacturX = new FacturXOptions { Xml = xml, Profile = profile, Relationship = relationship },
            };

            return config;
        }

        static Task<string> Generate(PdfGenerateConfig config) => GeneratePdf(SimpleHtml, config);

        // --- Profiles: XMP values, file name, relationship ---

        [Theory]
        [InlineData(MinimumGuideline, "MINIMUM", "factur-x.xml", "/Data")]
        [InlineData(BasicWlGuideline, "BASIC WL", "factur-x.xml", "/Data")]
        [InlineData(BasicGuideline, "BASIC", "factur-x.xml", "/Alternative")]
        [InlineData(En16931Guideline, "EN 16931", "factur-x.xml", "/Alternative")]
        [InlineData(ExtendedGuideline, "EXTENDED", "factur-x.xml", "/Alternative")]
        [InlineData(XRechnungGuideline, "XRECHNUNG", "xrechnung.xml", "/Alternative")]
        [InlineData("urn:cen.eu:en16931:2017#compliant#urn:zugferd.de:2p0:basic", "BASIC", "factur-x.xml", "/Alternative")]
        [InlineData("urn:cen.eu:en16931:2017#conformant#urn:zugferd.de:2p0:extended", "EXTENDED", "factur-x.xml", "/Alternative")]
        [InlineData(XRechnungGuideline + "#conformant#urn:xeinkauf.de:kosit:extension:xrechnung_3.0", "XRECHNUNG", "xrechnung.xml", "/Alternative")]
        public async Task ProfileIsDerivedFromTheXml_AndDrivesXmpFileNameAndRelationship(
            string guideline, string level, string fileName, string relationship)
        {
            var pdf = await Generate(Config(Cii(guideline)));

            var values = FxValues(pdf);
            Assert.Equal("INVOICE", values.DocumentType);
            Assert.Equal(fileName, values.DocumentFileName);
            Assert.Equal("1.0", values.Version);
            Assert.Equal(level, values.ConformanceLevel);

            var fileSpec = Body(pdf, FileSpecObject(pdf));
            Assert.Matches($@"/F\s*\({Regex.Escape(fileName)}\)", fileSpec);
            Assert.Matches($@"/AFRelationship\s*{relationship}", fileSpec);
            Assert.Matches(@"/Subtype\s*/text#2Fxml", Body(pdf, EmbeddedStreamObject(pdf)));
        }

        [Fact]
        public async Task TheEmbeddedXml_IsTheCallersXmlByteForByte()
        {
            var xml = Cii(En16931Guideline);
            var config = Config(xml);
            config.CompressContentStreams = false;

            var pdf = await Generate(config);

            Assert.Equal(xml, StreamBytes(pdf, EmbeddedStreamObject(pdf)));
        }

        // --- The PDF structure the specification asks for ---

        [Fact]
        public async Task TheInvoiceXml_IsReachableFromBothAfAndTheEmbeddedFilesTree_AndHasAModDate()
        {
            var pdf = await Generate(Config(Cii(En16931Guideline)));

            var afEntry = Assert.Single(AssociatedFiles(pdf));

            var catalog = Body(pdf, RootObject(pdf));
            var namesObject = int.Parse(Regex.Match(catalog, @"/Names\s+(\d+)\s+0\s+R").Groups[1].Value);
            var treeObject = int.Parse(Regex.Match(Body(pdf, namesObject), @"/EmbeddedFiles\s+(\d+)\s+0\s+R").Groups[1].Value);
            var treeEntry = Regex.Match(Body(pdf, treeObject), @"\(factur-x\.xml\)\s*(\d+)\s+0\s+R");

            Assert.True(treeEntry.Success);
            Assert.Equal(afEntry, int.Parse(treeEntry.Groups[1].Value));
            Assert.Matches(@"/ModDate\s*\(D:2026", Body(pdf, EmbeddedStreamObject(pdf)));
        }

        [Fact]
        public async Task TheInvoiceXml_IsTheFirstAttachment_AheadOfSupportingDocuments()
        {
            var config = Config(Cii(En16931Guideline));
            config.Attachments.Add(new PdfAttachment { FileName = "terms.txt", MimeType = "text/plain", Data = [1, 2, 3] });
            config.Attachments.Add(new PdfAttachment { FileName = "factur-xubl.xml", MimeType = "text/xml", Data = [4, 5, 6] });

            var pdf = await Generate(config);
            var files = AssociatedFiles(pdf);

            Assert.Equal(3, files.Count);
            Assert.Matches(@"/F\s*\(factur-x\.xml\)", Body(pdf, files[0]));
            Assert.Matches(@"/F\s*\(terms\.txt\)", Body(pdf, files[1]));
            Assert.Matches(@"/F\s*\(factur-xubl\.xml\)", Body(pdf, files[2]));
        }

        [Fact]
        public async Task TheXmpFileNameAgreesWithTheEmbeddedFileName()
        {
            var pdf = await Generate(Config(Cii(XRechnungGuideline)));

            Assert.Equal("xrechnung.xml", FxValues(pdf).DocumentFileName);
            Assert.Matches(@"/F\s*\(xrechnung\.xml\)", Body(pdf, FileSpecObject(pdf)));
            Assert.DoesNotContain("factur-x.xml", pdf);
        }

        // --- The XMP packet's shape ---

        [Fact]
        public async Task Xmp_DeclaresTheFacturXExtensionSchema_LikeTheSpecificationsSample()
        {
            var pdf = await Generate(Config(Cii(En16931Guideline)));
            var packet = XmpPacket(pdf);

            var schemas = packet.Descendants(PdfaExtension + "schemas").Single();
            var schema = schemas.Element(Rdf + "Bag")!.Elements(Rdf + "li").Single();
            Assert.Equal("Resource", (string?)schema.Attribute(Rdf + "parseType"));
            Assert.Equal("Factur-X PDFA Extension Schema", schema.Element(PdfaSchema + "schema")!.Value);
            // The trailing '#' is part of the name - the specification stresses it.
            Assert.Equal("urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#", schema.Element(PdfaSchema + "namespaceURI")!.Value);
            Assert.Equal("fx", schema.Element(PdfaSchema + "prefix")!.Value);

            var properties = schema.Element(PdfaSchema + "property")!.Element(Rdf + "Seq")!.Elements(Rdf + "li").ToList();
            Assert.Equal(["DocumentFileName", "DocumentType", "Version", "ConformanceLevel"],
                properties.Select(p => p.Element(PdfaProperty + "name")!.Value));
            Assert.All(properties, p =>
            {
                Assert.Equal("Resource", (string?)p.Attribute(Rdf + "parseType"));
                Assert.Equal("Text", p.Element(PdfaProperty + "valueType")!.Value);
                Assert.Equal("external", p.Element(PdfaProperty + "category")!.Value);
                Assert.False(string.IsNullOrWhiteSpace(p.Element(PdfaProperty + "description")!.Value));
            });
        }

        [Fact]
        public async Task Xmp_KeepsTheSchemaAndTheValuesInSeparateDescriptions()
        {
            var packet = XmpPacket(await Generate(Config(Cii(En16931Guideline))));

            var schemaDescription = packet.Descendants(PdfaExtension + "schemas").Single().Parent!;
            var valuesDescription = packet.Descendants(Fx + "DocumentType").Single().Parent!;

            Assert.NotSame(schemaDescription, valuesDescription);
            Assert.Equal(Rdf + "Description", schemaDescription.Name);
            Assert.Equal(Rdf + "Description", valuesDescription.Name);
            Assert.Equal("", (string?)schemaDescription.Attribute(Rdf + "about"));
            Assert.Equal("", (string?)valuesDescription.Attribute(Rdf + "about"));
        }

        [Fact]
        public async Task Xmp_UsesTheConventionalRdfAndFxPrefixes()
        {
            var pdf = await Generate(Config(Cii(En16931Guideline)));
            var xmp = Regex.Match(pdf, @"<x:xmpmeta.*?</x:xmpmeta>", RegexOptions.Singleline).Value;

            Assert.Contains("xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"", xmp);
            Assert.Contains("xmlns:fx=\"urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#\"", xmp);
            Assert.Contains("<fx:ConformanceLevel>EN 16931</fx:ConformanceLevel>", xmp);
            Assert.Contains("<rdf:li rdf:parseType=\"Resource\">", xmp);
        }

        [Fact]
        public async Task Xmp_StillCarriesThePdfAIdentification_ForEveryPdfA3Level()
        {
            foreach (var (level, part, conformance) in new[]
            {
                (PdfAConformance.PdfA3B, "3", "B"),
                (PdfAConformance.PdfA3U, "3", "U"),
                (PdfAConformance.PdfA3A, "3", "A"),
            })
            {
                var config = Config(Cii(En16931Guideline));
                config.PdfAConformance = level;
                config.DefaultLanguage = "en";

                var packet = XmpPacket(await Generate(config));

                var pdfaid = XNamespace.Get("http://www.aiim.org/pdfa/ns/id/");
                Assert.Equal(part, packet.Descendants(pdfaid + "part").Single().Value);
                Assert.Equal(conformance, packet.Descendants(pdfaid + "conformance").Single().Value);
                Assert.Single(packet.Descendants(Fx + "ConformanceLevel"));
            }
        }

        [Fact]
        public async Task Xmp_WithoutFacturX_HasNoFxBlock_AndNoExtensionSchema()
        {
            var config = Config(Cii(En16931Guideline));
            config.FacturX = null;

            var packet = XmpPacket(await Generate(config));

            Assert.Empty(packet.Descendants(PdfaExtension + "schemas"));
            Assert.DoesNotContain(packet.Descendants(), e => e.Name.Namespace == Fx);
        }

        // --- Profile agreement ---

        [Theory]
        [InlineData(En16931Guideline, FacturXProfile.En16931)]
        [InlineData(MinimumGuideline, FacturXProfile.Minimum)]
        [InlineData(XRechnungGuideline, FacturXProfile.XRechnung)]
        public async Task AnExplicitProfile_ThatAgreesWithTheXml_IsAccepted(string guideline, FacturXProfile profile)
        {
            var pdf = await Generate(Config(Cii(guideline), profile));

            Assert.Equal(FacturXInvoice.ConformanceLevelOf(profile), FxValues(pdf).ConformanceLevel);
        }

        [Fact]
        public async Task AnExplicitProfile_ThatDisagreesWithTheXml_IsRejected()
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Generate(Config(Cii(BasicGuideline), FacturXProfile.Extended)));

            Assert.Contains("'Extended'", ex.Message);
            Assert.Contains("'Basic'", ex.Message);
        }

        [Fact]
        public async Task AnUnrecognisedGuideline_NeedsAnExplicitProfile()
        {
            var xml = Cii("urn:example:my-own-guideline");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Generate(Config(xml)));
            Assert.Contains("urn:example:my-own-guideline", ex.Message);
            Assert.Contains("FacturXOptions.Profile", ex.Message);

            var pdf = await Generate(Config(xml, FacturXProfile.Extended));
            Assert.Equal("EXTENDED", FxValues(pdf).ConformanceLevel);
        }

        // --- Relationship rules (chapter 6.2.2) ---

        [Theory]
        [InlineData(BasicGuideline, PdfAttachmentRelationship.Source, "/Source")]
        [InlineData(En16931Guideline, PdfAttachmentRelationship.Data, "/Data")]
        [InlineData(ExtendedGuideline, PdfAttachmentRelationship.Alternative, "/Alternative")]
        [InlineData(MinimumGuideline, PdfAttachmentRelationship.Data, "/Data")]
        [InlineData(XRechnungGuideline, PdfAttachmentRelationship.Alternative, "/Alternative")]
        public async Task APermittedRelationshipOverride_IsWritten(string guideline, PdfAttachmentRelationship relationship, string expected)
        {
            var pdf = await Generate(Config(Cii(guideline), relationship: relationship));

            Assert.Matches($@"/AFRelationship\s*{expected}", Body(pdf, FileSpecObject(pdf)));
        }

        [Theory]
        [InlineData(MinimumGuideline, PdfAttachmentRelationship.Alternative)]
        [InlineData(BasicWlGuideline, PdfAttachmentRelationship.Source)]
        [InlineData(BasicGuideline, PdfAttachmentRelationship.Supplement)]
        [InlineData(En16931Guideline, PdfAttachmentRelationship.Unspecified)]
        [InlineData(XRechnungGuideline, PdfAttachmentRelationship.Data)]
        [InlineData(XRechnungGuideline, PdfAttachmentRelationship.Source)]
        public async Task ARelationshipTheSpecificationDoesNotPermit_IsRejected(string guideline, PdfAttachmentRelationship relationship)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Generate(Config(Cii(guideline), relationship: relationship)));

            Assert.Contains("not allowed", ex.Message);
        }

        [Fact]
        public async Task TheDescription_IsWrittenIntoTheFileSpecification()
        {
            var config = Config(Cii(En16931Guideline));
            config.FacturX!.Description = "Invoice 2026-0042";

            var pdf = await Generate(config);

            Assert.Matches(@"/Desc\s*\(Invoice 2026-0042\)", Body(pdf, FileSpecObject(pdf)));
        }

        // --- Requirements ---

        [Theory]
        [InlineData(PdfAConformance.None)]
        [InlineData(PdfAConformance.PdfA1B)]
        [InlineData(PdfAConformance.PdfA2B)]
        [InlineData(PdfAConformance.PdfA2A)]
        public async Task FacturX_RequiresAPdfA3Level(PdfAConformance conformance)
        {
            var config = Config(Cii(En16931Guideline));
            config.PdfAConformance = conformance;
            config.DefaultLanguage = "en";

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Generate(config));

            Assert.Contains("PDF/A-3", ex.Message);
        }

        [Fact]
        public async Task FacturX_NeedsACreationDate_LikeAnyXmpStream()
        {
            var config = Config(Cii(En16931Guideline));
            config.Metadata = null;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Generate(config));

            Assert.Contains("creation date", ex.Message);
        }

        [Fact]
        public async Task FacturX_WithPdfX_IsRejected()
        {
            var config = Config(Cii(En16931Guideline));
            config.PdfAConformance = PdfAConformance.None;
            config.PdfXConformance = PdfXConformance.X4;

            // The invoice needs a PDF/A-3 container, which PDF/X cannot be.
            await Assert.ThrowsAsync<InvalidOperationException>(() => Generate(config));
        }

        [Fact]
        public async Task EmptyXml_IsRejected()
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Generate(Config([])));

            Assert.Contains("FacturXOptions.Xml is empty", ex.Message);
        }

        [Fact]
        public async Task MalformedXml_IsRejected()
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Generate(Config(Encoding.UTF8.GetBytes("<not-closed>"))));

            Assert.Contains("not well-formed", ex.Message);
        }

        [Fact]
        public async Task ADoctypeInTheXml_IsRefused()
        {
            var xml = Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY e \"boom\">]><x>&e;</x>");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Generate(Config(xml)));

            Assert.Contains("not well-formed", ex.Message);
        }

        [Fact]
        public async Task XmlThatIsNotACrossIndustryInvoice_IsRejected()
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Generate(Config(Encoding.UTF8.GetBytes("<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\"/>"))));

            Assert.Contains("not a Cross Industry Invoice", ex.Message);
        }

        [Fact]
        public async Task AnInvoiceWithoutAGuideline_IsRejected()
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Generate(Config(Cii(null))));

            Assert.Contains("BT-24", ex.Message);
        }

        [Theory]
        [InlineData("factur-x.xml")]
        [InlineData("xrechnung.xml")]
        public async Task TheReservedInvoiceNames_CannotBeUsedForAnotherAttachment(string reserved)
        {
            var config = Config(Cii(En16931Guideline));
            config.Attachments.Add(new PdfAttachment { FileName = reserved, MimeType = "text/xml", Data = [1] });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Generate(config));

            Assert.Contains("reserved", ex.Message);
        }

        [Fact]
        public async Task TheReservedInvoiceNames_AreOnlyReservedWhenFacturXIsSet()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA3B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            config.Attachments.Add(new PdfAttachment { FileName = "factur-x.xml", MimeType = "text/xml", Data = [1] });

            Assert.Contains("factur-x.xml", await Generate(config));
        }

        // --- Several render calls, and the declarative path ---

        [Fact]
        public async Task SecondCall_WithTheSameInvoice_EmbedsItOnce_AndKeepsTheXmpBlock()
        {
            var generator = new PdfGenerator();
            var document = new PeachPdfDocument(new PeachPDF.PdfSharpCore.Pdf.PdfDocument());

            await generator.AddPdfPages(document, SimpleHtml, Config(Cii(En16931Guideline)));
            await generator.AddPdfPages(document, SimpleHtml, Config(Cii(En16931Guideline)));

            var pdf = Save(document);
            Assert.Single(AssociatedFiles(pdf));
            Assert.Equal("EN 16931", FxValues(pdf).ConformanceLevel);
        }

        [Fact]
        public async Task SecondCall_ThatDropsTheInvoice_IsRejected_SoTheXmpNeverLosesItsFxBlock()
        {
            var generator = new PdfGenerator();
            var document = new PeachPdfDocument(new PeachPDF.PdfSharpCore.Pdf.PdfDocument());
            await generator.AddPdfPages(document, SimpleHtml, Config(Cii(En16931Guideline)));

            var withoutInvoice = Config(Cii(En16931Guideline));
            withoutInvoice.FacturX = null;

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => generator.AddPdfPages(document, SimpleHtml, withoutInvoice));
        }

        [Fact]
        public async Task SecondCall_WithADifferentProfile_IsRejected()
        {
            var generator = new PdfGenerator();
            var document = new PeachPdfDocument(new PeachPDF.PdfSharpCore.Pdf.PdfDocument());
            await generator.AddPdfPages(document, SimpleHtml, Config(Cii(En16931Guideline)));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => generator.AddPdfPages(document, SimpleHtml, Config(Cii(BasicGuideline))));
        }

        [Fact]
        public async Task DeclarativeDocument_WithTwoPages_HasOneInvoiceAndOneFxBlock()
        {
            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Page(page => page.Content(content => content.Text("Invoice, page one")));
                doc.Page(page => page.Content(content => content.Text("Invoice, page two")));
            }, Config(Cii(En16931Guideline)));

            var pdf = Save(document);

            Assert.Single(AssociatedFiles(pdf));
            Assert.Single(Regex.Matches(pdf, @"/Type\s*/Filespec"));
            Assert.Single(XmpPacket(pdf).Descendants(Fx + "ConformanceLevel"));
        }

        // --- Helpers ---

        static (string DocumentType, string DocumentFileName, string Version, string ConformanceLevel) FxValues(string pdf)
        {
            var packet = XmpPacket(pdf);

            string Value(string name) => packet.Descendants(Fx + name).Single().Value;
            return (Value("DocumentType"), Value("DocumentFileName"), Value("Version"), Value("ConformanceLevel"));
        }
    }
}
