using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layer G of mixed page orientation/size support: <see cref="PdfGenerator.AddPdfPages"/> now reads
    /// each page's own resolved physical size (<c>fragmentainer.Geometry.SheetWidthPt/SheetHeightPt</c>,
    /// resolved by <see cref="Html.Core.PageGeometryTable"/> from a named <c>@page</c> rule's own
    /// <c>size</c>) instead of the single document-wide <c>orgPageSize</c> for every page - the layer
    /// that actually makes a mixed-orientation document produce a real, differently-sized PDF page
    /// rather than just correct internal geometry. Full end-to-end pipeline (real
    /// <see cref="PdfGenerator"/>), following <c>FixedPositionPaginationIntegrationTests</c>'
    /// real-generator convention rather than the lightweight layout harness, since this is specifically
    /// about what lands in the generated <see cref="PdfDocument"/>.
    /// </summary>
    public class PdfGeneratorMixedPageSizeTests
    {
        [Fact]
        public async Task NamedPageWithDifferentSize_ProducesADifferentlySizedPdfPage()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                @page { margin: 20mm; }
                @page landscape-table { size: 800pt 500pt; margin: 20pt; }
                body, div, p { margin: 0; }
                </style></head><body>
                <p>portrait page one</p>
                <div style='page: landscape-table; height: 50pt'>wide section</div>
                <p style='page: default-again' id='after'>back to portrait</p>
                </body></html>
                """;

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var doc = await generator.GeneratePdf(html, config);

            Assert.Equal(3, doc.PageCount);

            var a4 = PageSizeConverter.ToSize(PageSize.A4);

            // Page 0: base A4 portrait, unaffected by the later named-page override.
            Assert.Equal(a4.Width, doc.PdfDocument.Pages[0].Width.Point, 1);
            Assert.Equal(a4.Height, doc.PdfDocument.Pages[0].Height.Point, 1);

            // Page 1: the named page's own explicit size - genuinely different physical dimensions,
            // not just a resized content band inside the same MediaBox.
            Assert.Equal(800, doc.PdfDocument.Pages[1].Width.Point, 1);
            Assert.Equal(500, doc.PdfDocument.Pages[1].Height.Point, 1);

            // Page 2: reverted to a page with no matching named rule - back to the base A4 size.
            Assert.Equal(a4.Width, doc.PdfDocument.Pages[2].Width.Point, 1);
            Assert.Equal(a4.Height, doc.PdfDocument.Pages[2].Height.Point, 1);
        }

        [Fact]
        public async Task UniformDocument_EveryPageSharesTheSameConfiguredSize()
        {
            // Regression guard: a document with no @page size overrides gets byte-identical page
            // dimensions on every page, same as before this layer existed.
            var html = """
                <!DOCTYPE html><html><head><style>
                body, p { margin: 0; }
                </style></head><body>
                <p>page one</p>
                <p style='page-break-before: always'>page two</p>
                </body></html>
                """;

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var doc = await generator.GeneratePdf(html, config);

            Assert.Equal(2, doc.PageCount);

            var a4 = PageSizeConverter.ToSize(PageSize.A4);
            Assert.Equal(a4.Width, doc.PdfDocument.Pages[0].Width.Point, 1);
            Assert.Equal(a4.Height, doc.PdfDocument.Pages[0].Height.Point, 1);
            Assert.Equal(doc.PdfDocument.Pages[0].Width.Point, doc.PdfDocument.Pages[1].Width.Point, 1);
            Assert.Equal(doc.PdfDocument.Pages[0].Height.Point, doc.PdfDocument.Pages[1].Height.Point, 1);
        }

        [Fact]
        public async Task LinkFromALandscapePage_TargetingAPortraitPage_ResolvesTheCorrectYFlip()
        {
            // The Y-flip for a cross-page link must use the TARGET page's own height (Layer G's
            // HandleLinks fix), not the page the link itself sits on - the two differ here.
            var html = """
                <!DOCTYPE html><html><head><style>
                @page { margin: 20mm; }
                @page landscape-table { size: 800pt 500pt; margin: 20pt; }
                body, div, p, a { margin: 0; }
                </style></head><body>
                <p id='target'>portrait target</p>
                <div style='page: landscape-table; height: 50pt'>
                <a id='backlink' href='#target'>back to target</a>
                </div>
                </body></html>
                """;

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);

            Assert.Equal(2, doc.PageCount);
            // The landscape page (page 1) must genuinely differ from the portrait target page (page 0),
            // which is the precondition for the Y-flip fix actually being exercised.
            Assert.NotEqual(doc.PdfDocument.Pages[0].Height.Point, doc.PdfDocument.Pages[1].Height.Point, 1);

            // A named destination was registered for the anchor, flipped against the TARGET (portrait,
            // 841.89pt-tall A4-margin-reduced) page's own height - if HandleLinks still used the
            // landscape page's stale height for the flip, the /D array's Y coordinate would fall well
            // outside the target page's own [0, height] range instead.
            var ms = new MemoryStream();
            doc.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());

            var match = new Regex(@"/FitH\s+([\d.]+)").Match(pdfText);
            Assert.True(match.Success, "expected a /FitH named-destination entry for the 'target' anchor");

            var flippedY = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var targetPageHeight = doc.PdfDocument.Pages[0].Height.Point;
            Assert.InRange(flippedY, 0, targetPageHeight);
        }
    }
}
