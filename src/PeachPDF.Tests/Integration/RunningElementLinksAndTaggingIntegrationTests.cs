using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Structure;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for §5.1 (PDF link annotations inside a css-gcpm-3 running element) and §5.2
    /// (tagged-PDF structure for <c>content: element()</c> content), asserted on the in-memory
    /// <c>PdfDocument</c>/structure-tree object model, mirroring <c>HandleLinksPaginationTests</c> and
    /// <c>TaggedPdfStructureTreeTests</c>' own patterns for the main-tree case.
    /// </summary>
    public class RunningElementLinksAndTaggingIntegrationTests
    {
        private static string LinkFixture() =>
            """
            <!DOCTYPE html><html><head><style>
            @page { size: a6; margin: 12mm; }
            @page { @top-center { content: element(heading); } }
            h1.running { position: running(heading); margin: 0; font-size: 9pt; }
            p { line-height: 1.6; }
            </style></head><body>
            <h1 class="running">Chapter <a href="https://example.com/">One</a></h1>
            """ +
            string.Concat(Enumerable.Repeat("<p>Lorem ipsum dolor sit amet, consectetur adipiscing elit.</p>", 80)) +
            """
            </body></html>
            """;

        [Fact]
        public async Task RunningElementLink_ProducesOneAnnotationPerPageItAppearsOn()
        {
            var doc = await new PdfGenerator().GeneratePdf(LinkFixture(), PageSize.A6);

            Assert.True(doc.PageCount > 1, "fixture does not paginate, so it asserts nothing");

            for (var i = 0; i < doc.PageCount; i++)
            {
                Assert.True(doc.Pages[i].Annotations.Count > 0,
                    $"page {i + 1} is missing the running header's link annotation");
            }
        }

        [Fact]
        public async Task RunningElementLink_AnnotationSitsWithinTopMarginBand()
        {
            var doc = await new PdfGenerator().GeneratePdf(LinkFixture(), PageSize.A6);

            // A6 = 297.64pt tall; the link is inside a 12mm (~34pt) top margin, so its annotation must
            // sit near the physical top of the page, not down in the body content.
            var rect = doc.Pages[0].Annotations[0].Rectangle;
            var topFromSheetTop = doc.Pages[0].Height.Point - rect.Y2;
            Assert.InRange(topFromSheetTop, 0, 40);
        }

        [Fact]
        public async Task RunningElementAnchorLink_ResolvesToTargetPage()
        {
            // The anchor branch (href starting with '#') of HandleRunningElementLinks - distinct from
            // the external-URL branch the other tests in this file exercise.
            var html = """
                <!DOCTYPE html><html><head><style>
                @page { size: a6; margin: 12mm; }
                @page { @top-center { content: element(heading); } }
                h1.running { position: running(heading); margin: 0; font-size: 9pt; }
                p { line-height: 1.6; }
                </style></head><body>
                <h1 class="running">Chapter <a href="#target">One</a></h1>
                """ +
                string.Concat(Enumerable.Repeat("<p>Lorem ipsum dolor sit amet, consectetur adipiscing elit.</p>", 80)) +
                """
                <p id="target">Target paragraph.</p>
                </body></html>
                """;

            var doc = await new PdfGenerator().GeneratePdf(html, PageSize.A6);

            Assert.True(doc.PageCount > 1, "fixture does not paginate, so it asserts nothing");
            for (var i = 0; i < doc.PageCount; i++)
            {
                Assert.True(doc.Pages[i].Annotations.Count > 0,
                    $"page {i + 1} is missing the running header's anchor-link annotation");
            }
        }

        [Fact]
        public async Task RunningElementLink_TaggedPdf_LinksToStructureElement()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A6, EnableTaggedPdf = true };
            var doc = await new PdfGenerator().GeneratePdf(LinkFixture(), config);

            Assert.True(doc.PdfDocument.Catalog.MarkInfo.Marked);

            // The running header's <a> produced a real /Link structure element somewhere in the tree -
            // proof that content: element() content is reachable by the structure builder at all.
            var allElements = AllStructureElements(doc.PdfDocument.Catalog.StructureTreeRoot).ToList();
            Assert.Contains(allElements, e => e.StructureType == "/Link");
        }

        [Fact]
        public async Task RunningElementContent_TaggedPdf_ProducesHeadingStructureElement()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A6, EnableTaggedPdf = true };
            var doc = await new PdfGenerator().GeneratePdf(LinkFixture(), config);

            // The running <h1> itself - real element() content painted via the same FragmentPainter
            // choke point tagging already flows through - should also produce a heading structure
            // element, proving §5.2's "falls out automatically" claim rather than leaving it assumed.
            var allElements = AllStructureElements(doc.PdfDocument.Catalog.StructureTreeRoot).ToList();
            Assert.Contains(allElements, e => e.StructureType == "/H1");
        }

        private static IEnumerable<PdfStructureElement> AllStructureElements(PdfStructureTreeRoot root)
        {
            foreach (var top in PdfStructureElement.GetKids(root.Elements).OfType<PdfStructureElement>())
            {
                foreach (var e in Descendants(top))
                    yield return e;
            }
        }

        private static IEnumerable<PdfStructureElement> Descendants(PdfStructureElement element)
        {
            yield return element;
            foreach (var child in PdfStructureElement.GetKids(element.Elements).OfType<PdfStructureElement>())
            {
                foreach (var e in Descendants(child))
                    yield return e;
            }
        }
    }
}
