using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class BackgroundRepeatOriginClipLayersIntegrationTests
    {
        private const string PngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        // Box with visible border and padding so border/padding/content-box regions all differ,
        // matching the fixture in BackgroundOriginClipIntegrationTests.cs. Dimensions are authored in
        // pt (not px) so the expected content-stream coordinates below stay literal: with the
        // spec-correct 1px = 0.75pt convention a px-authored box would lay out at 0.75x its numbers,
        // shifting every asserted origin/clip coordinate. The geometry these tests exercise is
        // unit-agnostic, so pt keeps the layout units identical to the pre-change px behavior.
        private static string BoxHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }} div {{ width: 120pt; height: 80pt; border: 10pt solid black; padding: 10pt; {css} }}</style></head><body><div></div></body></html>";

        private static string ImageBoxHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }}</style></head><body>" +
            $"<div style=\"width: 120pt; height: 80pt; border: 10pt solid black; padding: 10pt; background-image: url('data:image/png;base64,{PngBase64}'), url('data:image/png;base64,{PngBase64}'); {css}\"></div></body></html>";

        private static async Task<string> GetPdfText(string html)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task MultipleLayers_BackgroundRepeat_CyclesPerLayer()
        {
            var pdfText = await GetPdfText(ImageBoxHtml(
                "background-size: 20pt 20pt; background-position: left top; background-repeat: no-repeat, repeat-x;"));

            // The "no-repeat" layer draws exactly once at (30, 792) (padding-box top-left); the
            // "repeat-x" layer tiles multiple 20pt-wide copies starting from the same X.
            var singleDrawCount = Regex.Matches(pdfText, @"q 20 0 0 20 30 792 cm").Count;
            Assert.True(singleDrawCount >= 2, $"Expected at least one draw per layer at the shared start position, found {singleDrawCount}");
            Assert.Matches(new Regex(@"q 20 0 0 20 50 792 cm"), pdfText); // second repeat-x tile, 20pt to the right
            Assert.Matches(new Regex(@"q 20 0 0 20 70 792 cm"), pdfText); // third repeat-x tile
        }

        [Fact]
        public async Task MultipleLayers_BackgroundOriginAndClip_DifferPerLayer()
        {
            var pdfText = await GetPdfText(BoxHtml(
                "background-image: linear-gradient(to right, red, blue), linear-gradient(to bottom, green, yellow); " +
                "background-origin: content-box, border-box; background-clip: content-box, border-box;"));

            // Layer 0 (content-box): x=40 (20 margin + 10 border + 10 padding), 120x80.
            Assert.Contains("40 722 120 80 re", pdfText);
            // Layer 1 (border-box): x=20 (margin only), 160x120 (120+2*10 border+2*10 padding).
            Assert.Contains("20 702 160 120 re", pdfText);
        }

        [Fact]
        public async Task BackgroundColor_MultiValueClip_UsesLastEntry_NoImages()
        {
            var pdfText = await GetPdfText(BoxHtml(
                "background-color: coral; background-clip: border-box, content-box;"));

            // With no background-image layers, the solid fill must still clip to the LAST entry
            // (content-box: 40,722,120,80), not the first (border-box: 20,702,160,120).
            Assert.Contains("40 722 120 80 re", pdfText);
            Assert.DoesNotContain("20 702 160 120 re", pdfText);
        }
    }
}
