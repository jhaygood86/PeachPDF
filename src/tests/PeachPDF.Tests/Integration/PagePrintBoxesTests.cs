using PeachPDF;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-page-3 §12-13: <c>@page { bleed; marks }</c> grows the sheet around the page box. The page box is the <c>/TrimBox</c>, the
    /// <c>/BleedBox</c> adds the bleed, and the <c>/MediaBox</c> adds the strip that holds the marks. A prepress tool reads those boxes.
    /// </summary>
    public class PagePrintBoxesTests
    {
        private static async Task<PdfFile> RenderAsync(string pageRule, string body = "<p>content</p>")
        {
            var html = $"<!DOCTYPE html><html><head><style>@page {{ {pageRule} }} body {{ margin: 0 }}</style></head><body>{body}</body></html>";
            var doc = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4 });
            return doc.Inspect();
        }

        private static double[] Box(PdfFile file, string key) =>
            file.Pages[0].Array(key)?.Resolved.Select(v => ((PdfNumber)v).Value).ToArray() ?? [];

        private static void AssertBox(double[] expected, double[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i], 0.01);
        }

        [Fact]
        public async Task BleedAndCropMarks_GrowTheSheetAndSetTheBoxes()
        {
            var file = await RenderAsync("size: 200pt 300pt; margin: 20pt; bleed: 8pt; marks: crop cross");

            // bleed 8 + the 15pt strip the marks are drawn in = 23 on every side.
            AssertBox([-23, -23, 223, 323], Box(file, "/MediaBox"));
            AssertBox([0, 0, 200, 300], Box(file, "/TrimBox"));
            AssertBox([-8, -8, 208, 308], Box(file, "/BleedBox"));
        }

        [Fact]
        public async Task CropMarksWithAutoBleed_UseASixPointBleed()
        {
            var file = await RenderAsync("size: 200pt 300pt; marks: crop");

            AssertBox([0, 0, 200, 300], Box(file, "/TrimBox"));
            AssertBox([-6, -6, 206, 306], Box(file, "/BleedBox"));
            AssertBox([-21, -21, 221, 321], Box(file, "/MediaBox"));
        }

        [Fact]
        public async Task BleedWithoutMarks_AddsNoMarkStrip()
        {
            var file = await RenderAsync("size: 200pt 300pt; bleed: 3pt");

            AssertBox([-3, -3, 203, 303], Box(file, "/MediaBox"));
            AssertBox([-3, -3, 203, 303], Box(file, "/BleedBox"));
            AssertBox([0, 0, 200, 300], Box(file, "/TrimBox"));
        }

        [Fact]
        public async Task NoBleedOrMarks_LeavesThePageAlone()
        {
            var file = await RenderAsync("size: 200pt 300pt");

            AssertBox([0, 0, 200, 300], Box(file, "/MediaBox"));
            Assert.Empty(Box(file, "/TrimBox"));
            Assert.Empty(Box(file, "/BleedBox"));
        }

        [Fact]
        public async Task InvalidMarksAndBleed_AreIgnored()
        {
            var file = await RenderAsync("size: 200pt 300pt; marks: banana; bleed: -4pt");

            AssertBox([0, 0, 200, 300], Box(file, "/MediaBox"));
        }

        [Fact]
        public async Task OnlyThePagesTheRuleAppliesTo_GetTheBoxes()
        {
            var html = "<!DOCTYPE html><html><head><style>@page { size: 200pt 300pt } @page :first { bleed: 5pt; marks: crop } body { margin: 0 } p { margin: 0; height: 280pt }</style></head><body><p>one</p><p>two</p></body></html>";
            var file = (await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4 })).Inspect();

            Assert.True(file.PageCount >= 2);
            Assert.NotEmpty(file.Pages[0].Array("/TrimBox")!.Resolved);
            Assert.Null(file.Pages[1].Array("/TrimBox"));
        }

        [Fact]
        public async Task Marks_AreDrawnOutsideThePageBox()
        {
            var file = await RenderAsync("size: 200pt 300pt; bleed: 6pt; marks: crop cross");

            var content = string.Concat(file.ContentStreams(file.Pages[0]).Select(s => s.DecodedText));

            // Strokes at a negative x or y (to the left of / above the page box) and at hairline width.
            Assert.Contains("0.5 w", content);
            Assert.Matches(@"-\d+(\.\d+)? [-\d.]+ m", content.Replace("\n", " "));
        }
    }
}
