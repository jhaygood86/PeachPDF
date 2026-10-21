using System.Text;
using System.Text.RegularExpressions;
using PeachPDF.Layout;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The logical structure of a file drawn with <see cref="IDocumentBuilder.OverlayFile"/> into a tagged document: its headings, lists and
    /// figures join the document's structure tree, and whatever has no structure is marked as an artifact.
    /// </summary>
    public class FileLayerTaggingTests
    {
        /// <summary>
        /// A one-page file whose content is tagged: an artifact, a heading, a list item's paragraph, a figure with alternative text, a paragraph
        /// in a role the file maps to P, and a square drawn outside any sequence.
        /// </summary>
        private static byte[] TaggedFile(string content = null!)
        {
            content ??=
                "/Artifact BMC 0.9 g 0 0 200 200 re f EMC\n" +
                "/H1 <</MCID 0>> BDC 1 0 0 rg 10 150 100 30 re f EMC\n" +
                "/P <</MCID 1>> BDC 0 0 1 rg 10 100 100 30 re f EMC\n" +
                "/Figure <</MCID 2>> BDC 0 1 0 rg 10 50 30 30 re f EMC\n" +
                "/Foo <</MCID 3>> BDC 0 1 1 rg 100 50 30 30 re f EMC\n" +
                "1 1 0 rg 150 150 20 20 re f\n";

            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 5 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R /Resources << >> /StructParents 0 >>",
                $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
                "<< /Type /StructTreeRoot /K 6 0 R /ParentTree 12 0 R /RoleMap << /Foo /P >> >>",
                "<< /Type /StructElem /S /Document /P 5 0 R /K [7 0 R 8 0 R 11 0 R 14 0 R] >>",
                "<< /Type /StructElem /S /H1 /P 6 0 R /Pg 3 0 R /K 0 >>",
                "<< /Type /StructElem /S /L /P 6 0 R /K 9 0 R >>",
                "<< /Type /StructElem /S /LI /P 8 0 R /K 10 0 R >>",
                "<< /Type /StructElem /S /LBody /P 9 0 R /K 13 0 R >>",
                "<< /Type /StructElem /S /Figure /P 6 0 R /Pg 3 0 R /K 2 /Alt (A green square) >>",
                "<< /Nums [0 [7 0 R 13 0 R 11 0 R 14 0 R]] >>",
                "<< /Type /StructElem /S /P /P 10 0 R /Pg 3 0 R /K 1 >>",
                "<< /Type /StructElem /S /Foo /P 6 0 R /Pg 3 0 R /K 3 >>",
            };
            var sb = new StringBuilder("%PDF-1.7\n");
            var offsets = new List<int>();
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(sb.Length);
                sb.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
            }

            var xref = sb.Length;
            sb.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
            foreach (var offset in offsets)
                sb.Append(offset.ToString("D10")).Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            return Encoding.Latin1.GetBytes(sb.ToString());
        }

        private static async Task<PeachPdfDocument> BuildAsync(bool tagged, byte[] file, int stamps = 1)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { EnableTaggedPdf = tagged, CompressContentStreams = false };
            return await generator.CreateDocument(doc =>
            {
                doc.Page(page => page.Size(200, 200).Margin(0).Content(container => container.Text("Body text")));
                for (var i = 0; i < stamps; i++)
                    doc.OverlayFile(layer => layer.File(file).SourcePages("1"));
            }, config);
        }

        private static string Content(PeachPdfDocument document)
        {
            var file = document.Inspect();
            return file.ContentText(file.Pages[0]);
        }

        private static IEnumerable<StructElem> Walk(IEnumerable<StructElem> elements)
        {
            foreach (var element in elements)
            {
                yield return element;
                foreach (var child in Walk(element.Kids))
                    yield return child;
            }
        }

        [Fact]
        public async Task Structure_OfTheDrawnPage_JoinsTheDocumentsTree()
        {
            var document = await BuildAsync(tagged: true, TaggedFile());
            var elements = Walk(document.Inspect().StructureRoot).ToList();
            var types = elements.Select(e => e.StructureType).ToList();

            Assert.Contains("/H1", types);
            Assert.Contains("/Figure", types);
            Assert.Equal("A green square", elements.Single(e => e.StructureType == "/Figure").AlternateText);

            // A list item nests as L > LI > LBody > P, with a single LBody: the writer makes one for the item and the file's own is not repeated.
            var list = elements.Single(e => e.StructureType == "/L");
            var item = Assert.Single(list.Kids);
            Assert.Equal("/LI", item.StructureType);
            var body = Assert.Single(item.Kids);
            Assert.Equal("/LBody", body.StructureType);
            Assert.Equal("/P", Assert.Single(body.Kids).StructureType);

            // The file's own role (Foo) is mapped to the standard type its role map names; nothing non-standard is written.
            Assert.DoesNotContain("/Foo", types);
            Assert.Equal(1, types.Count(t => t == "/LBody"));
        }

        [Fact]
        public async Task ContentWithoutStructure_IsMarkedAsAnArtifact()
        {
            var document = await BuildAsync(tagged: true, TaggedFile());
            var content = Content(document);

            // The file's artifact sequence and its untagged square are artifacts in the new page; its structured sequences carry marked-content ids.
            Assert.Matches(new Regex(@"/Artifact\s+BMC"), content);
            Assert.True(Regex.Matches(content, @"/MCID\s+\d+\s*>>\s*BDC").Count >= 5, "the page's own text and four of the file's sequences");

            // No drawing operator sits outside a marked-content sequence: strip every sequence and nothing is painted.
            var outside = Regex.Replace(content, @"(?s)/\w+\s*(<<[^>]*>>\s*BDC|BMC).*?\bEMC", "");
            Assert.DoesNotMatch(new Regex(@"\b(f|f\*|S|B)\s*$", RegexOptions.Multiline), outside.Replace("0.9 g", ""));
        }

        [Fact]
        public async Task WithoutTagging_NothingIsMarked()
        {
            var document = await BuildAsync(tagged: false, TaggedFile());
            var content = Content(document);

            Assert.DoesNotContain("BDC", content);
            Assert.Null(document.Inspect().StructureTreeRoot);
        }

        [Fact]
        public async Task PageDrawnTwice_GetsStructureElementsOfItsOwnEachTime()
        {
            var document = await BuildAsync(tagged: true, TaggedFile(), stamps: 2);
            var types = Walk(document.Inspect().StructureRoot).Select(e => e.StructureType).ToList();

            Assert.Equal(2, types.Count(t => t == "/H1"));
            Assert.Equal(2, types.Count(t => t == "/Figure"));
        }

        [Fact]
        public async Task FileWithoutStructure_IsAllArtifacts()
        {
            var file = TaggedFile("1 0 0 rg 10 10 50 50 re f\n").AsSpan().ToArray();
            var document = await BuildAsync(tagged: true, file);
            var types = Walk(document.Inspect().StructureRoot).Select(e => e.StructureType).ToList();

            Assert.DoesNotContain("/H1", types);                       // the page's content carries no sequences, so none of the file's elements is used
            Assert.Matches(new Regex(@"/Artifact\s+BMC"), Content(document));
        }
    }
}
