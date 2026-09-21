using PeachPDF.Layout;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;
using static PeachPDF.Tests.TestSupport.PdfObjectReader;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end tests for <see cref="PdfGenerateConfig.Attachments"/>: what actually lands in the saved
    /// bytes, checked structurally (the same file specification must be reachable from <c>/AF</c> and from
    /// the <c>/EmbeddedFiles</c> name tree) rather than by the mere presence of a token.
    /// </summary>
    public class PdfAttachmentIntegrationTests
    {
        const string SimpleHtml = "<html><body><p>Hello</p></body></html>";

        static readonly byte[] CsvBytes = Encoding.UTF8.GetBytes("a,b\n1,2\n");

        static PdfAttachment Csv(string name = "report.csv") => new()
        {
            FileName = name,
            Data = CsvBytes,
            MimeType = "text/csv",
            Relationship = PdfAttachmentRelationship.Data,
            Description = "The numbers behind the report",
        };

        static PdfGenerateConfig A3Config(params PdfAttachment[] files)
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA3B,
                Metadata = new PdfDocumentMetadata { CreationDate = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero) },
            };

            foreach (var file in files)
                config.Attachments.Add(file);

            return config;
        }

        [Fact]
        public async Task Attachment_UnderPdfA3_IsReachableFromBothAfArrayAndEmbeddedFilesTree()
        {
            var pdf = await GeneratePdf(A3Config(Csv()));

            var catalog = Body(pdf, RootObject(pdf));

            var afRefs = References(Regex.Match(catalog, @"/AF\s*\[([^\]]*)\]").Groups[1].Value);
            var afEntry = Assert.Single(afRefs);

            var namesObject = int.Parse(Regex.Match(catalog, @"/Names\s+(\d+)\s+0\s+R").Groups[1].Value);
            var treeObject = int.Parse(Regex.Match(Body(pdf, namesObject), @"/EmbeddedFiles\s+(\d+)\s+0\s+R").Groups[1].Value);
            var tree = Body(pdf, treeObject);

            // A flat leaf: /Names only - no /Kids, which is valid at any size and needs no /Limits.
            Assert.DoesNotContain("/Kids", tree);
            var treeMatch = Regex.Match(tree, @"/Names\s*\[\s*\(report\.csv\)\s*(\d+)\s+0\s+R\s*\]");
            Assert.True(treeMatch.Success, tree);

            // The point of the structural check: both indexes name the very same file specification.
            Assert.Equal(afEntry, int.Parse(treeMatch.Groups[1].Value));
        }

        [Fact]
        public async Task Attachment_FileSpecificationCarriesFileNamesRelationshipAndDescription()
        {
            var pdf = await GeneratePdf(A3Config(Csv()));
            var fileSpec = Body(pdf, FileSpecObject(pdf));

            Assert.Matches(@"/Type\s*/Filespec", fileSpec);
            Assert.Matches(@"/F\s*\(report\.csv\)", fileSpec);
            Assert.Matches(@"/UF\s*<FEFF[0-9A-F]+>", fileSpec);
            Assert.Matches(@"/AFRelationship\s*/Data", fileSpec);
            Assert.Matches(@"/Desc\s*\(The numbers behind the report\)", fileSpec);
        }

        [Fact]
        public async Task Attachment_EmbeddedStream_HasMimeSubtypeSizeAndModDate_AndBothEfKeysNameOneStream()
        {
            var pdf = await GeneratePdf(A3Config(Csv()));
            var fileSpec = Body(pdf, FileSpecObject(pdf));

            var ef = Regex.Match(fileSpec, @"/EF\s*<<(.*?)>>", RegexOptions.Singleline).Groups[1].Value;
            var fStream = int.Parse(Regex.Match(ef, @"/F\s+(\d+)\s+0\s+R").Groups[1].Value);
            var ufStream = int.Parse(Regex.Match(ef, @"/UF\s+(\d+)\s+0\s+R").Groups[1].Value);
            Assert.Equal(fStream, ufStream);

            var stream = Body(pdf, fStream);
            Assert.Matches(@"/Type\s*/EmbeddedFile", stream);
            // A name: the "/" inside the MIME type is written as #2F.
            Assert.Matches(@"/Subtype\s*/text#2Fcsv", stream);
            Assert.Matches($@"/Size\s+{CsvBytes.Length}\b", stream);
            // The document's resolved creation date (2026-01-15T10:30:00Z) is the ModDate fallback; PdfDate
            // writes it in the local time zone, so the expected calendar date is derived the same way.
            var expected = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero).LocalDateTime;
            Assert.Matches($@"/ModDate\s*\(D:{expected:yyyyMMddHHmmss}", stream);
        }

        [Fact]
        public async Task Attachment_Bytes_RoundTripThroughFlate_AndAreStoredVerbatimWhenCompressionIsOff()
        {
            var compressed = await GeneratePdf(A3Config(Csv()));
            var compressedBytes = StreamBytes(compressed, EmbeddedStreamObject(compressed));
            Assert.Matches(@"/Filter\s*/FlateDecode", Body(compressed, EmbeddedStreamObject(compressed)));
            Assert.Equal(CsvBytes, Inflate(compressedBytes));

            var config = A3Config(Csv());
            config.CompressContentStreams = false;
            var plain = await GeneratePdf(config);
            var plainObject = EmbeddedStreamObject(plain);
            Assert.DoesNotMatch(@"/Filter", Body(plain, plainObject));
            Assert.Equal(CsvBytes, StreamBytes(plain, plainObject));
        }

        [Fact]
        public async Task Attachments_KeepTheirOrder_InAfAndTheNameTree()
        {
            var pdf = await GeneratePdf(A3Config(Csv("b.csv"), Csv("a.csv")));

            var catalog = Body(pdf, RootObject(pdf));
            var afRefs = References(Regex.Match(catalog, @"/AF\s*\[([^\]]*)\]").Groups[1].Value);
            Assert.Equal(2, afRefs.Count);
            Assert.Matches(@"/F\s*\(b\.csv\)", Body(pdf, afRefs[0]));
            Assert.Matches(@"/F\s*\(a\.csv\)", Body(pdf, afRefs[1]));

            // The name tree is a sorted structure regardless of insertion order.
            var namesObject = int.Parse(Regex.Match(catalog, @"/Names\s+(\d+)\s+0\s+R").Groups[1].Value);
            var treeObject = int.Parse(Regex.Match(Body(pdf, namesObject), @"/EmbeddedFiles\s+(\d+)\s+0\s+R").Groups[1].Value);
            Assert.Matches(@"\(a\.csv\)\s*\d+\s+0\s+R\s*\(b\.csv\)", Body(pdf, treeObject));
        }

        [Fact]
        public async Task NonAsciiFileName_IsAnAsciiFallbackInF_AndFullyUnicodeInUf()
        {
            var pdf = await GeneratePdf(A3Config(Csv("café.csv")));
            var fileSpec = Body(pdf, FileSpecObject(pdf));

            Assert.Matches(@"/F\s*\(caf_\.csv\)", fileSpec);
            // UTF-16BE with BOM: FEFF, then 'c' 'a' 'f' 'é' ...
            Assert.Matches(@"/UF\s*<FEFF00630061006600E9", fileSpec);
        }

        [Fact]
        public async Task NonAsciiDescription_IsWrittenAsUnicodeString()
        {
            var attachment = Csv();
            attachment.Description = "Café";
            var pdf = await GeneratePdf(A3Config(attachment));

            Assert.Matches(@"/Desc\s*<FEFF00430061006600E9>", Body(pdf, FileSpecObject(pdf)));
        }

        [Fact]
        public async Task NoAttachments_AddsNothingToTheCatalog()
        {
            var pdf = await GeneratePdf(A3Config());
            var catalog = Body(pdf, RootObject(pdf));

            Assert.DoesNotContain("/AF", catalog);
            Assert.DoesNotContain("/Names", catalog);
            Assert.DoesNotContain("/EmbeddedFiles", pdf);
        }

        [Fact]
        public async Task Attachment_WithoutPdfA_IsAnOrdinaryAttachment()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            config.Attachments.Add(Csv());

            var pdf = await GeneratePdf(config);

            Assert.Contains("/EmbeddedFiles", pdf);
            Assert.Matches(@"/AFRelationship\s*/Data", pdf);
        }

        [Theory]
        [InlineData(PdfAConformance.PdfA1B)]
        [InlineData(PdfAConformance.PdfA1A)]
        [InlineData(PdfAConformance.PdfA2B)]
        [InlineData(PdfAConformance.PdfA2U)]
        [InlineData(PdfAConformance.PdfA2A)]
        public async Task Attachment_UnderPdfA1OrA2_IsRejected(PdfAConformance conformance)
        {
            var config = A3Config(Csv());
            config.PdfAConformance = conformance;
            config.DefaultLanguage = "en";

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => GeneratePdf(config));
            Assert.Contains("PdfA3", ex.Message);
        }

        [Theory]
        [InlineData(PdfAConformance.PdfA3B)]
        [InlineData(PdfAConformance.PdfA3U)]
        [InlineData(PdfAConformance.PdfA3A)]
        public async Task Attachment_UnderEveryPdfA3Level_IsAccepted(PdfAConformance conformance)
        {
            var config = A3Config(Csv());
            config.PdfAConformance = conformance;
            config.DefaultLanguage = "en";

            Assert.Contains("/EmbeddedFiles", await GeneratePdf(config));
        }

        [Fact]
        public async Task DuplicateFileNames_AreRejected()
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => GeneratePdf(A3Config(Csv("x.csv"), Csv("x.csv"))));
            Assert.Contains("more than one file named 'x.csv'", ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task EmptyFileName_IsRejected(string name)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => GeneratePdf(A3Config(Csv(name))));
            Assert.Contains("FileName", ex.Message);
        }

        [Fact]
        public async Task FileNameOutsideLatin1_IsRejected()
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => GeneratePdf(A3Config(Csv("文件.csv"))));
            Assert.Contains("Latin-1", ex.Message);
        }

        [Fact]
        public async Task NullData_IsRejected()
        {
            var attachment = Csv();
            attachment.Data = null!;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => GeneratePdf(A3Config(attachment)));
            Assert.Contains("no Data", ex.Message);
        }

        [Fact]
        public async Task EmptyData_IsAnEmptyEmbeddedFile()
        {
            var attachment = Csv();
            attachment.Data = [];

            var pdf = await GeneratePdf(A3Config(attachment));

            Assert.Matches(@"/Size\s+0\b", Body(pdf, EmbeddedStreamObject(pdf)));
        }

        [Fact]
        public async Task BlankMimeType_IsRejected()
        {
            var attachment = Csv();
            attachment.MimeType = " ";

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => GeneratePdf(A3Config(attachment)));
            Assert.Contains("MimeType", ex.Message);
        }

        [Fact]
        public async Task NullAttachmentEntry_IsRejected()
        {
            var config = A3Config();
            config.Attachments.Add(null!);

            await Assert.ThrowsAsync<ArgumentNullException>(() => GeneratePdf(config));
        }

        [Theory]
        [InlineData(PdfAttachmentRelationship.Source, "/Source")]
        [InlineData(PdfAttachmentRelationship.Data, "/Data")]
        [InlineData(PdfAttachmentRelationship.Alternative, "/Alternative")]
        [InlineData(PdfAttachmentRelationship.Supplement, "/Supplement")]
        [InlineData(PdfAttachmentRelationship.Unspecified, "/Unspecified")]
        public async Task Relationship_IsWrittenAsItsAfRelationshipName(PdfAttachmentRelationship relationship, string expected)
        {
            var attachment = Csv();
            attachment.Relationship = relationship;

            var pdf = await GeneratePdf(A3Config(attachment));

            Assert.Matches($@"/AFRelationship\s*{Regex.Escape(expected)}", Body(pdf, FileSpecObject(pdf)));
        }

        [Fact]
        public async Task ExplicitModificationDate_WinsOverTheCreationDateFallback()
        {
            var attachment = Csv();
            attachment.ModificationDate = new DateTimeOffset(2020, 6, 1, 12, 0, 0, TimeSpan.Zero);

            var pdf = await GeneratePdf(A3Config(attachment));

            Assert.Matches(@"/ModDate\s*\(D:2020", Body(pdf, EmbeddedStreamObject(pdf)));
        }

        [Fact]
        public async Task ModificationDate_FallsBackToNow_WhenThereIsNoCreationDateEither()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            config.Attachments.Add(Csv());

            var pdf = await GeneratePdf(config);

            Assert.Matches($@"/ModDate\s*\(D:{DateTime.Now.Year}", Body(pdf, EmbeddedStreamObject(pdf)));
        }

        // --- Multiple AddPdfPages/AddPages calls, and the declarative path ---

        [Fact]
        public async Task SecondCall_WithTheSameAttachments_EmbedsThemOnlyOnce()
        {
            var generator = new PdfGenerator();
            var document = new PeachPdfDocument(new PeachPDF.PdfSharpCore.Pdf.PdfDocument());

            await generator.AddPdfPages(document, SimpleHtml, A3Config(Csv()));
            await generator.AddPdfPages(document, SimpleHtml, A3Config(Csv()));

            var pdf = Save(document);
            var catalog = Body(pdf, RootObject(pdf));
            Assert.Single(References(Regex.Match(catalog, @"/AF\s*\[([^\]]*)\]").Groups[1].Value));
            Assert.Single(Regex.Matches(pdf, @"/Type\s*/Filespec"));
        }

        [Fact]
        public async Task SecondCall_WithADifferentSet_IsRejected()
        {
            var generator = new PdfGenerator();
            var document = new PeachPdfDocument(new PeachPDF.PdfSharpCore.Pdf.PdfDocument());
            await generator.AddPdfPages(document, SimpleHtml, A3Config(Csv()));

            var different = Csv();
            different.Data = Encoding.UTF8.GetBytes("a,b\n9,9\n");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => generator.AddPdfPages(document, SimpleHtml, A3Config(different)));
            Assert.Contains("Attachments must be the same", ex.Message);
        }

        [Fact]
        public async Task SecondCall_WithNoAttachments_AfterAnEarlierCallHadSome_IsRejected()
        {
            var generator = new PdfGenerator();
            var document = new PeachPdfDocument(new PeachPDF.PdfSharpCore.Pdf.PdfDocument());
            await generator.AddPdfPages(document, SimpleHtml, A3Config(Csv()));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => generator.AddPdfPages(document, SimpleHtml, A3Config()));
        }

        [Theory]
        [InlineData("relationship")]
        [InlineData("mime")]
        [InlineData("description")]
        [InlineData("date")]
        [InlineData("name")]
        [InlineData("count")]
        public async Task SecondCall_DifferingInAnyField_IsRejected(string field)
        {
            var generator = new PdfGenerator();
            var document = new PeachPdfDocument(new PeachPDF.PdfSharpCore.Pdf.PdfDocument());
            await generator.AddPdfPages(document, SimpleHtml, A3Config(Csv()));

            var config = A3Config(Csv());
            var changed = config.Attachments.Single();
            switch (field)
            {
                case "relationship": changed.Relationship = PdfAttachmentRelationship.Source; break;
                case "mime": changed.MimeType = "text/plain"; break;
                case "description": changed.Description = "other"; break;
                case "date": changed.ModificationDate = DateTimeOffset.UnixEpoch; break;
                case "name": changed.FileName = "other.csv"; break;
                case "count": config.Attachments.Add(Csv("second.csv")); break;
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => generator.AddPdfPages(document, SimpleHtml, config));
        }

        [Fact]
        public async Task DeclarativeDocument_WithTwoPages_EmbedsTheAttachmentOnce()
        {
            // A declarative document re-enters the render core once per Page(...), not once per call.
            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Page(page => page.Content(content => content.Text("Page one")));
                doc.Page(page => page.Content(content => content.Text("Page two")));
            }, A3Config(Csv()));

            var pdf = Save(document);
            var catalog = Body(pdf, RootObject(pdf));

            Assert.Single(References(Regex.Match(catalog, @"/AF\s*\[([^\]]*)\]").Groups[1].Value));
            Assert.Single(Regex.Matches(pdf, @"/Type\s*/Filespec"));
            Assert.Single(Regex.Matches(pdf, @"/Type\s*/EmbeddedFile"));
        }

        // --- File names, PDF/X and the PDF version (from the post-change review) ---

        [Theory]
        [InlineData("a/b.csv")]
        [InlineData("..\\x.csv")]
        [InlineData("dir\\x.csv")]
        [InlineData("a\u0000b.csv")]
        [InlineData("line\nbreak.csv")]
        [InlineData(".")]
        [InlineData("..")]
        public async Task FileName_WithAPathSeparatorOrControlCharacter_IsRejected(string name)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => GeneratePdf(A3Config(Csv(name))));

            Assert.Contains(name is "." or ".." ? "directory reference" : "path separator", ex.Message);
        }

        [Theory]
        [InlineData(PdfXConformance.X1a)]
        [InlineData(PdfXConformance.X3)]
        [InlineData(PdfXConformance.X4)]
        public async Task Attachment_WithPdfX_IsRejected(PdfXConformance conformance)
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, PdfXConformance = conformance };
            config.Attachments.Add(Csv());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => GeneratePdf(config));

            Assert.Contains("PDF/X does not allow embedded files", ex.Message);
        }

        [Fact]
        public async Task Attachment_WithoutPdfA_RaisesThePdfVersionTo17_ButNoAttachmentLeavesItAlone()
        {
            var plain = await new PdfGenerator().GeneratePdf(SimpleHtml, PageSize.A4);
            Assert.Equal(14, plain.PdfDocument.Version);

            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            config.Attachments.Add(Csv());
            var withFile = await new PdfGenerator().GeneratePdf(SimpleHtml, config);

            // /UF, /Desc and /AFRelationship are newer than the historical 1.4 header.
            Assert.Equal(17, withFile.PdfDocument.Version);
            Assert.StartsWith("%PDF-1.7", Save(withFile));
        }

        [Fact]
        public async Task Attachment_WithPdf20_KeepsThePdf20Version()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, PdfVersion = PdfVersion.Pdf20 };
            config.Attachments.Add(Csv());

            var document = await new PdfGenerator().GeneratePdf(SimpleHtml, config);

            Assert.Equal(20, document.PdfDocument.Version);
        }

        // --- Helpers ---

        static Task<string> GeneratePdf(PdfGenerateConfig config) => PdfObjectReader.GeneratePdf(SimpleHtml, config);
    }
}
