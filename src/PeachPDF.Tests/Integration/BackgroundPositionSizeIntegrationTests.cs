using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class BackgroundPositionSizeIntegrationTests
    {
        // A real, loadable 1x1 pixel PNG - lets background-size/background-position resolve
        // against an image with a genuine (if trivial) intrinsic size, without any filesystem
        // dependency. Quoted inside url(...) since the unquoted form trips over the ';' in
        // "data:image/png;base64,..." being misread as a declaration terminator.
        private const string PngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        private static string ImageBoxHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }}</style></head><body>" +
            $"<div style=\"width: 200px; height: 100px; background-image: url('data:image/png;base64,{PngBase64}'); {css}\"></div>" +
            "</body></html>";

        private static string BoxHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }} div {{ width: 200px; height: 100px; {css} }}</style></head><body><div></div></body></html>";

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
        public async Task BackgroundSizeAndPosition_ScalesAndPositionsAtBottomRight()
        {
            var pdfText = await GetPdfText(ImageBoxHtml(
                "background-size: 50px 50px; background-position: right bottom; background-repeat: no-repeat;"));

            // The div spans x:[20,220] y:[722,822] in page points (20pt margin, 200x100 box).
            // A 50x50 tile flush to the right/bottom edge lands at (170, 722).
            Assert.Matches(new Regex(@"q 50 0 0 50 170(\.\d+)? 722(\.\d+)? cm /\w+ Do"), pdfText);
        }

        [Fact]
        public async Task BackgroundPosition_PercentPercent_IsNotMiscategorizedAsCentered()
        {
            // Regression test: a historical bug used "does the string contain a literal '0'
            // character" as a heuristic for centering, which miscategorized values like "25% 75%".
            // With a 50x50 tile in the 200x100 box, 25%/75% must resolve against the 150x50
            // available space: offsetX = 0.25*150 = 37.5, offsetY = 0.75*50 = 37.5, giving a
            // translate of (20+37.5, 822-50-37.5) = (57.5, 734.5) - not the centered (95, 747).
            var pdfText = await GetPdfText(ImageBoxHtml(
                "background-size: 50px 50px; background-position: 25% 75%; background-repeat: no-repeat;"));

            Assert.Matches(new Regex(@"q 50 0 0 50 57\.5 734\.5 cm /\w+ Do"), pdfText);
            Assert.DoesNotMatch(new Regex(@"q 50 0 0 50 95(\.\d+)? 747(\.\d+)? cm"), pdfText);
        }

        [Fact]
        public async Task BackgroundRepeat_TilesAtResolvedSize_NotNaturalImageSize()
        {
            var pdfText = await GetPdfText(ImageBoxHtml("background-size: 40px 40px; background-repeat: repeat;"));

            // 200/40 = 5 columns exactly; each tile step is 40pt, not the image's natural 1x1 size.
            Assert.Matches(new Regex(@"q 40 0 0 40 20(\.\d+)? \d+(\.\d+)? cm /\w+ Do"), pdfText);
            Assert.Matches(new Regex(@"q 40 0 0 40 60(\.\d+)? \d+(\.\d+)? cm /\w+ Do"), pdfText);
            Assert.Matches(new Regex(@"q 40 0 0 40 100(\.\d+)? \d+(\.\d+)? cm /\w+ Do"), pdfText);
        }

        [Fact]
        public async Task MultipleLayers_BackgroundPosition_CyclesPerLayer()
        {
            var pdfText = await GetPdfText(BoxHtml(
                $"background-image: url('data:image/png;base64,{PngBase64}'), url('data:image/png;base64,{PngBase64}'); " +
                "background-position: 10px 10px, center; background-repeat: no-repeat;"));

            // Each layer must use its own comma-list entry, not the same string for both -
            // the "10px 10px" layer is flush near the top-left (translate x=30 = 20 + 10px offset).
            Assert.Matches(new Regex(@"cm /\w+ Do"), pdfText);
            Assert.Contains("1 0 0 1 30 ", pdfText);
            // The "center" layer must land at a different X than the "10px 10px" layer.
            Assert.Contains("1 0 0 1 119.5 ", pdfText);
        }

        [Fact]
        public async Task GradientBackgroundSize_SmallerThanBox_TilesInsteadOfFillingWholeBox()
        {
            var pdfText = await GetPdfText(BoxHtml(
                "background-image: linear-gradient(to right, red, blue); background-size: 50%; background-repeat: repeat;"));

            // A 100x100 tile (50% of 200x100 -> width 100, height defaults to the full container
            // since a gradient has no intrinsic ratio) repeating across a 200-wide box draws twice,
            // at natural (1,1) scale relative to its own Form XObject BBox, translated 100pt apart.
            Assert.Contains("/BBox [0 0 100 100]", pdfText);
            Assert.Contains("1 0 0 1 20 722 cm /Fm0 Do", pdfText);
            Assert.Contains("1 0 0 1 120 722 cm /Fm0 Do", pdfText);
        }

        [Theory]
        [InlineData("cover")]
        [InlineData("contain")]
        [InlineData("auto")]
        public async Task GradientBackgroundSize_NoIntrinsicRatio_CollapsesToFullBox(string sizeKeyword)
        {
            // Gradients have no intrinsic size/ratio, so cover/contain/auto must all resolve to
            // exactly the container size - the same untiled single-shading-fill path used when no
            // background-size is specified at all (no Form XObject tile involved).
            var pdfText = await GetPdfText(BoxHtml(
                $"background-image: linear-gradient(to right, red, blue); background-size: {sizeKeyword};"));

            Assert.Contains("/ShadingType", pdfText);
            Assert.DoesNotContain("/BBox", pdfText);
        }

        [Fact]
        public async Task BorderRadius_WithBackgroundImage_ClipsToRoundedCorners()
        {
            // Regression test: url()/gradient-tile background images previously ignored
            // border-radius entirely (only solid-color/gradient-fill paints got the rounded clip).
            var pdfText = await GetPdfText(ImageBoxHtml("background-repeat: no-repeat; border-radius: 20px;"));

            // A rounded-rect clip path draws one bezier curve ("c" operator) per corner - a plain
            // rectangular clip (the pre-fix behavior for background images) has none.
            var curveOperatorCount = Regex.Matches(pdfText, @"(?<=\n| )c\n").Count;
            Assert.True(curveOperatorCount >= 4, $"Expected at least 4 'c' (curve) operators for a rounded-rect clip, found {curveOperatorCount}");
            Assert.Contains("Do", pdfText);
        }
    }
}
