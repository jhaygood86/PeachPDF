using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Confirms <c>position: fixed</c> content repeats identically across real, multi-page
    /// <see cref="PdfGenerator"/> output - i.e. a genuine end-to-end pipeline test of the "running
    /// header/footer" mechanic <c>docs/html-css-support.md</c> documents ("repeats identically
    /// (page-anchored) on every page"), which was previously only exercised via single-
    /// <c>PerformPaint</c>-call tests (see <c>Acid2FeatureVerificationTests.
    /// PositionedZIndex_PaintsOverFixedPositionedContent</c>). This is the legitimate feature Round 9's
    /// content-empty-page-skipping change (<see cref="Html.Core.HtmlContainerInt.GetPaginationSlots"/>)
    /// must not disturb - a fixed box must still repeat on every page that IS materialized, exactly as
    /// before.
    /// </summary>
    public class FixedPositionPaginationIntegrationTests
    {
        // A small, distinctively-colored fixed rect at a fixed page offset - unambiguous per this
        // repo's structural content-stream convention (color set immediately followed by its own
        // "re f" rect, not a bare substring - see CanvasBackgroundIntegrationTests).
        private const string FixedRectPattern = @"0\.0\d* 0\.13\d* 0\.2\d* rg[\s\S]{0,40}30(\.\d+)? 30(\.\d+)? re\s*\nf";

        [Fact]
        public async Task FixedPositionBox_RepeatsIdenticallyOnEveryRealPage()
        {
            // Explicit page breaks with real text content guarantee exactly 3 materialized pages,
            // independent of Round 9's content-empty-page skip (none of these pages are content-empty).
            var html = "<!DOCTYPE html><html><head><style>"
                + "body { margin: 0; }"
                + ".fixedBox { position: fixed; top: 10pt; left: 10pt; width: 30pt; height: 30pt; background: rgb(12,34,56); }"
                + "</style></head><body>"
                + "<div class='fixedBox'></div>"
                + "<p>page 1</p>"
                + "<p style='page-break-before: always'>page 2</p>"
                + "<p style='page-break-before: always'>page 3</p>"
                + "</body></html>";

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);

            Assert.Equal(3, doc.PageCount);

            var ms = new MemoryStream();
            doc.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());

            var matches = new Regex(FixedRectPattern).Matches(pdfText);

            Assert.True(matches.Count >= 3,
                $"expected the fixed-position box to repeat on every one of the 3 real pages, found {matches.Count}");
        }
    }
}
