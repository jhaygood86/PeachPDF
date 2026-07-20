using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end tests for 4-edge full-bleed pages through the real <see cref="PdfGenerator"/>
    /// pipeline: a `@page :first { margin: 0 }` cover sized to the full sheet must occupy exactly
    /// one page (per-page geometry gives slot 0 the whole sheet; the exact-boundary forced break
    /// adds no blank page) and paint its plate to all four sheet edges, with following content on a
    /// normally-margined page 2. Content-stream assertions are structural (a color set immediately
    /// followed by its own full-sheet `re` fill), per this repo's testing conventions.
    /// </summary>
    public class FullBleedPageIntegrationTests
    {
        private const string Html = """
            <!DOCTYPE html><html><head><style>
            @page { size: letter portrait; margin: 60pt 50pt; }
            @page :first { margin: 0; }
            body { margin: 0; }
            .cover { width: 612pt; height: 792pt; background: rgb(20,40,60); page-break-after: always; }
            p { margin: 0; }
            </style></head><body>
            <div class='cover'></div>
            <p>first ordinary page of content</p>
            </body></html>
            """;

        [Fact]
        public async Task FullBleedCover_ExactSheetSize_YieldsCoverPlusOneContentPage()
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.Letter, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(Html, config);

            Assert.Equal(2, doc.PageCount);
        }

        [Fact]
        public async Task FullBleedCover_PlateRect_CoversTheEntireSheet()
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.Letter, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(Html, config);

            var ms = new MemoryStream();
            doc.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());

            // Structural adjacency, not bare token presence: the page-1 margin-override translate
            // (delta = 0 - base margins = -50,-60) is followed, in the same clip nest, by the cover
            // fill - rgb(20,40,60) set and its own rect whose pre-translate operands
            // "50 60 612 792 re" land, after that cm, exactly on the physical (0,0)-(612,792)
            // sheet - i.e. genuine 4-edge coverage, not just a color token somewhere.
            var plate = new Regex(
                @"1 -0 -0 1 -50 -60 cm[\s\S]{0,220}?0\.078\d* 0\.157\d* 0\.235\d* rg[\s\S]{0,40}?50 60 612(\.\d+)? 792(\.\d+)? re\s*\n?f");
            Assert.Matches(plate, pdfText);
        }
    }
}
