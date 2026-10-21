using PeachPDF.Tests.TestSupport;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Structure;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    public class TaggedPdfStructureTreeTests
    {
        [Fact]
        public async Task EnableTaggedPdf_False_AllocatesNoStructureTreeObjects()
        {
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><h1>Title</h1><p>Body</p></body></html>", PageSize.A4);

            var catalog = result.Inspect().Catalog;
            Assert.False(catalog.ContainsKey("/MarkInfo"));
            Assert.False(catalog.ContainsKey("/StructTreeRoot"));
        }

        [Fact]
        public async Task EnableTaggedPdf_True_SetsMarkInfoMarked()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><p>Hello</p></body></html>", config);

            Assert.True(result.Inspect().Catalog.Dict("/MarkInfo")!.Bool("/Marked"));
        }

        [Fact]
        public async Task SimpleDocument_ProducesDocumentRootWithH1AndP()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><h1>Title</h1><p>Body text</p></body></html>", config);

            var root = result.Inspect();
            var documentElement = Assert.Single(RootKids(root));
            Assert.Equal("/Document", documentElement.StructureType);

            var topLevel = Kids(documentElement).ToList();
            Assert.Equal(new[] { "/H1", "/P" }, topLevel.Select(e => e.StructureType));
        }

        [Fact]
        public async Task Image_WithAlt_ProducesFigureWithMatchingAlt()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var html = "<html><body><img src='data:image/png;base64," +
                       TinyPngBase64 +
                       "' alt='A cat sleeping' /></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, config);

            var documentElement = RootKids(result.Inspect()).Single();
            var figure = Kids(documentElement).Single();

            Assert.Equal("/Figure", figure.StructureType);
            Assert.Equal("A cat sleeping", figure.AlternateText);
        }

        [Fact]
        public async Task NoneTagType_ProducesNoStructureElement_ChildrenAttachToParent()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var html = "<html><body><div style='-peachpdf-pdf-tag-type:none'><h1>Title</h1></div></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, config);

            var documentElement = RootKids(result.Inspect()).Single();
            var topLevel = Kids(documentElement).ToList();

            // The wrapper <div> (tagged none) is fully transparent - /H1 attaches directly to
            // /Document, with no intervening struct element for the div at all.
            Assert.Single(topLevel);
            Assert.Equal("/H1", topLevel[0].StructureType);
        }

        [Fact]
        public async Task AuthorOverride_PromotesDivToBlockQuote()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var html = "<html><body><div style='-peachpdf-pdf-tag-type:BlockQuote'><h1>Title</h1></div></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, config);

            var documentElement = RootKids(result.Inspect()).Single();
            var topLevel = Kids(documentElement).ToList();

            Assert.Single(topLevel);
            Assert.Equal("/BlockQuote", topLevel[0].StructureType);
        }

        [Fact]
        public async Task Page_StructParents_IndexesIntoParentTree()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><p>Hello</p></body></html>", config);

            var file = result.Inspect();
            var structParentsKey = (int)file.Pages[0].Number("/StructParents")!.Value;

            var entry = file.ParentTreeEntry(structParentsKey) as PdfArr;

            Assert.NotNull(entry);
            Assert.True(entry!.Count > 0);
        }

        [Fact]
        public async Task DeclarativeDocument_WithSeveralPageHandlers_SharesOneStructureTree()
        {
            // Each Page handler lays out and draws on its own, but the document has one structure tree: one Document element holding the
            // structure of every handler, and page keys that do not repeat.
            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Page(page => page.Size(200, 200).Content(container => container.Text("First")));
                doc.Page(page => page.Size(200, 200).Content(container => container.Text("Second")));
            }, new PdfGenerateConfig { EnableTaggedPdf = true });

            var file = document.Inspect();
            var documentElement = RootKids(file).Single();
            var leaves = Kids(documentElement).ToList();
            var keys = file.Pages.Select(page => (int)page.Number("/StructParents")!.Value).ToList();

            Assert.Equal(2, file.PageCount);
            Assert.Equal(keys.Count, keys.Distinct().Count());
            Assert.True(leaves.Count >= 2, "the structure of both handlers is in the tree");
            Assert.All(keys, key => Assert.True(file.ParentTreeEntry(key) is PdfArr { Count: > 0 }));
        }

        [Fact]
        public async Task Table_ProducesTableRowCellStructure()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var html = "<html><body><table><tbody><tr><td>A</td><td>B</td></tr></tbody></table></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, config);

            var documentElement = RootKids(result.Inspect()).Single();
            var table = Kids(documentElement).Single();
            Assert.Equal("/Table", table.StructureType);

            var tbody = Kids(table).Single();
            Assert.Equal("/TBody", tbody.StructureType);

            var tr = Kids(tbody).Single();
            Assert.Equal("/TR", tr.StructureType);

            var cells = Kids(tr).ToList();
            Assert.Equal(new[] { "/TD", "/TD" }, cells.Select(e => e.StructureType));
        }

        [Fact]
        public async Task Link_ProducesLinkStructureElement_OnlyWhenHrefPresent()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var html = "<html><body><a href='https://example.com'>go</a></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, config);

            var documentElement = RootKids(result.Inspect()).Single();
            var link = Kids(documentElement).Single();
            Assert.Equal("/Link", link.StructureType);
        }

        [Fact]
        public async Task List_ProducesL_WithLI_EachSplitIntoSiblingLblAndLBody()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var html = "<html><body><ul><li>One</li><li>Two</li></ul></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, config);

            var documentElement = RootKids(result.Inspect()).Single();
            var list = Kids(documentElement).Single();
            Assert.Equal("/L", list.StructureType);

            var listItems = Kids(list).ToList();
            Assert.Equal(new[] { "/LI", "/LI" }, listItems.Select(e => e.StructureType));

            foreach (var listItem in listItems)
            {
                var itemKids = Kids(listItem).ToList();
                Assert.Equal(new[] { "/Lbl", "/LBody" }, itemKids.Select(e => e.StructureType));
            }
        }

        [Fact]
        public async Task List_MarkerSuppressedByAuthor_ProducesLIWithOnlyLBody()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var html = "<html><head><style>li::marker { -peachpdf-pdf-tag-type: none }</style></head>" +
                       "<body><ul><li>One</li></ul></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, config);

            var documentElement = RootKids(result.Inspect()).Single();
            var list = Kids(documentElement).Single();
            var listItem = Kids(list).Single();

            var itemKids = Kids(listItem).ToList();
            Assert.Equal(new[] { "/LBody" }, itemKids.Select(e => e.StructureType));
        }

        [Fact]
        public async Task Link_ObjRLinksAnnotationBackToStructureElement_AndAnnotationGetsMatchingStructParent()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var html = "<html><body><a href='https://example.com'>go</a></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, config);

            var documentElement = RootKids(result.Inspect()).Single();
            var link = Kids(documentElement).Single();
            Assert.Equal("/Link", link.StructureType);

            // The struct element's "/K" holds both the anonymous text child's MCID content and,
            // per this linkage, an "/OBJR" pointing at the Link annotation.
            var file = result.Inspect();
            var objRef = Assert.Single(link.AllKids.OfType<PdfDict>(), k => k.Name("/Type") == "/OBJR");

            var page = file.Pages[0];
            var annotation = Assert.IsAssignableFrom<PdfDict>(objRef["/Obj"]);
            Assert.Equal(page.ObjectNumber, objRef.Dict("/Pg")!.ObjectNumber);

            // The annotation's own "/StructParent" resolves, via "/ParentTree", back to this same
            // "/Link" struct element - i.e. navigation works in both directions.
            var entry = file.ParentTreeEntry((int)annotation.Number("/StructParent")!.Value);
            var referencedElement = Assert.IsAssignableFrom<PdfDict>(entry is PdfArr array ? array[0] : entry);
            Assert.Equal(link.Dict.ObjectNumber, referencedElement.ObjectNumber);

            // PDF/UA requires tab order to follow structure order once a page has a
            // structure-linked annotation.
            Assert.Equal("/S", page.Name("/Tabs"));
        }

        [Fact]
        public async Task Hr_ProducesNoStructureElement()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var html = "<html><body><p>Above</p><hr/><p>Below</p></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, config);

            var documentElement = RootKids(result.Inspect()).Single();
            var topLevel = Kids(documentElement).ToList();

            Assert.Equal(new[] { "/P", "/P" }, topLevel.Select(e => e.StructureType));
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        // A 1x1 transparent PNG.
        const string TinyPngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

        static IEnumerable<StructElem> RootKids(PdfFile file) => file.StructureRoot;

        static IEnumerable<StructElem> Kids(StructElem element) => element.Kids;
    }
}
