using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A page-margin box's own <c>background</c> (issue #1082) - painted via the shared
    /// <see cref="PeachPDF.Html.Core.Dom.LayeredBackgroundPainter"/> the <c>@page</c> box's own
    /// background also uses (<see cref="PageBackgroundIntegrationTests"/>). Verified the same
    /// content-stream-regex way as <see cref="CanvasBackgroundIntegrationTests"/>: a solid-color fill's
    /// exact operator sequence is unambiguous, so this isn't the "content-stream substring" pitfall
    /// CLAUDE.md warns about for masks/gradients.
    /// </summary>
    public class MarginBoxRendererBackgroundTests
    {
        private static async Task<string> GetPdfText(string html, int margin = 40)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.Undefined,
                ManualPageWidth = 600,
                ManualPageHeight = 800,
                CompressContentStreams = false,
            };
            config.SetMargins(margin);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task MarginBoxBackgroundColor_PaintsAcrossItsOwnRect()
        {
            // bottom-left/-right pin bottom-center's own slot to a known, round 200x40.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>@page { @bottom-left { content: \"L\"; width: 100pt; } " +
                "@bottom-center { content: \"x\"; width: 200pt; background-color: rgb(255,0,0); } " +
                "@bottom-right { content: \"R\"; width: 100pt; } }</style></head>" +
                "<body><p>short</p></body></html>");

            Assert.Matches(new Regex(@"1 0 0 rg[\s\S]{0,60}\d+(\.\d+)? \d+(\.\d+)? 200(\.\d+)? 40(\.\d+)? re\s*\nf"), pdfText);
        }

        [Fact]
        public async Task MarginBoxBackgroundColor_DoesNotPaint_WhenASqueezedAutoBoxCollapsesToZero()
        {
            // Mirrors MarginBoxRendererBoxModelTests' OverPaddedBox_CollapsesToZeroRatherThanANegativeExtent:
            // top-left's own huge fixed width leaves nothing for the row's two auto boxes, squeezing
            // top-center's outer slot (and so its border-box) to zero - PaintBackgroundAndBorder must skip
            // painting entirely rather than filling a degenerate rect.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>@page { @top-left { content: \"L\"; width: 10000pt; } " +
                "@top-center { background-color: rgb(255,0,255); } }</style></head>" +
                "<body><p>short</p></body></html>");

            Assert.DoesNotContain("1 0 1 rg", pdfText);
        }

        [Fact]
        public async Task MarginBoxBackgroundColor_PaintsEvenWithNoContentDeclared()
        {
            // A margin box can be a purely decorative band - background must not be gated on content.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>@page { @top-left { width: 150pt; background-color: rgb(0,255,0); } }" +
                "</style></head><body><p>short</p></body></html>");

            Assert.Matches(new Regex(@"0 1 0 rg[\s\S]{0,60}\d+(\.\d+)? \d+(\.\d+)? 150(\.\d+)? 40(\.\d+)? re\s*\nf"), pdfText);
        }

        [Fact]
        public async Task MarginBoxBackgroundImage_PaintsAGradientLayer_ViaTheSameSharedPainterAsPageBackground()
        {
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>@page { @bottom-center { content: \"x\"; width: 200pt; " +
                "background-image: linear-gradient(to right, red, blue); } } </style></head>" +
                "<body><p>short</p></body></html>");

            Assert.Contains("/ShadingType", pdfText);
        }

        [Theory]
        // A bottom-row box's own height is always the page's bottom margin thickness (150pt here) minus
        // whatever box model the clip in question excludes - border (10+10) and/or padding (20+20).
        // background-clip unset -> the initial value, padding-box: border excluded, padding is not.
        [InlineData("", 240, 130)]
        [InlineData("background-clip: border-box;", 260, 150)]
        [InlineData("background-clip: padding-box;", 240, 130)]
        [InlineData("background-clip: content-box;", 200, 90)]
        public async Task BackgroundClip_ChoosesTheGenuinelyDifferentBorderPaddingContentBoxRects(
            string clipDeclaration, int expectedWidth, int expectedHeight)
        {
            // width:200 is the CONTENT-box dimension (css-page-3 §5.3.2 - padding/border are additive on
            // top of it), so border-box/padding-box/content-box are three genuinely different rects here
            // - proof that background-origin/-clip fidelity is real, not a no-op.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>@page { @bottom-left { content: \"L\"; width: 20pt; } " +
                $"@bottom-center {{ content: \"x\"; width: 200pt; padding: 20pt; border: 10pt solid black; " +
                $"background-color: rgb(0,0,255); {clipDeclaration} }} " +
                "@bottom-right { content: \"R\"; width: 20pt; } }</style></head>" +
                "<body><p>short</p></body></html>", margin: 150);

            Assert.Matches(new Regex($@"0 0 1 rg[\s\S]{{0,60}}\d+(\.\d+)? \d+(\.\d+)? {expectedWidth}(\.\d+)? {expectedHeight}(\.\d+)? re\s*\nf"), pdfText);
        }
    }
}
