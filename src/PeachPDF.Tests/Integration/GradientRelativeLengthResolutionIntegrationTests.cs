using PeachPDF;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issues #823/#824, both follow-ups to #821 left as tracked gaps (see
    /// <c>.claude/recent-fixes/2026-08-24-gradient-pixelsperpoint.md</c>):
    /// <list type="bullet">
    /// <item>
    /// <b>#824</b>: <c>CssImagePainter</c>'s gradient stop-position <c>em</c> resolution used a hard-coded
    /// <c>emPx = 16.0</c> default instead of the painting box's own real, cascaded font-size.
    /// </item>
    /// <item>
    /// <b>#823</b>: an explicit <c>radial-gradient()</c> radius in <c>em</c>/<c>rem</c>/viewport units threw
    /// <c>InvalidOperationException</c> from <c>Length.ToPixel()</c>, which only understands absolute units.
    /// </item>
    /// </list>
    /// Both are fixed by resolving every relative unit through the same
    /// <c>CssImagePainter.ResolveGradientLength</c> helper - the box's real <see cref="Html.Core.Dom.CssBox.GetEmHeight"/>/
    /// <see cref="Html.Core.Dom.CssBox.GetRemHeight"/> for <c>em</c>/<c>rem</c>/<c>ex</c>/<c>ch</c>, and
    /// <c>CssValueParser.ParseLength</c> (already correct for these) for viewport/container-relative units.
    /// </summary>
    public class GradientRelativeLengthResolutionIntegrationTests
    {
        private static string GradientHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }} div {{ width: 200px; height: 100px; {css} }}</style></head><body><div></div></body></html>";

        private static async Task<string> GetPdfText(string html, double pixelsPerInch = 72)
        {
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 300,
                PixelsPerInch = pixelsPerInch,
            };
            config.SetMargins(0);

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private static double ExtractFirstBound(string pdfText)
        {
            var m = Regex.Match(pdfText, @"/Bounds\s*\[\s*([\d.eE+-]+)");
            Assert.True(m.Success, "expected a stitching function /Bounds array");
            return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static double ExtractCircleRadius(string pdfText)
        {
            var m = Regex.Match(pdfText,
                @"/Coords\s*\[\s*([\d.eE+-]+)\s+([\d.eE+-]+)\s+0\s+([\d.eE+-]+)\s+([\d.eE+-]+)\s+([\d.eE+-]+)\s*\]");
            Assert.True(m.Success, "expected a circular radial shading /Coords array");
            return double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
        }

        // ─── #824: em stop position resolves against the box's real font-size ─────────────────────────

        [Fact]
        public async Task EmStopPosition_ResolvesAgainstTheBoxsOwnFontSize_NotAHardCodedDefault()
        {
            // Issue #824's own repro: a 24px font-size means "2em" is 48px (36pt), not the pre-fix
            // hard-coded-16px-default's 32px (24pt). The gradient line spans the box's full 200px (150pt)
            // width, so the stop's normalized position is 36/150 = 0.24 - the pre-fix code landed on
            // 24/150 = 0.16 instead.
            var html = GradientHtml("font-size:24px; background-image: linear-gradient(to right, red 0, blue 2em, green);");

            var pdfText = await GetPdfText(html);
            var bound = ExtractFirstBound(pdfText);

            Assert.Equal(0.24, bound, 3);
        }

        [Fact]
        public async Task EmStopPosition_AtDefaultFontSize_MatchesTheOldHardCodedDefault()
        {
            // At the UA-default "medium" font-size (11pt), the old hard-coded 16px (12pt) default and the
            // box's own real font-size disagree too - this just confirms the fix resolves against whatever
            // the box's own font-size actually is, not that 16px happens to be a magic constant.
            var html = GradientHtml("background-image: radial-gradient(circle, red 0, blue 1em, green);");

            var pdfText = await GetPdfText(html);
            var bound = ExtractFirstBound(pdfText);

            // Circle (no explicit size, centered) defaults to farthest-corner: sqrt(75^2 + 37.5^2) =
            // 83.8525pt. 1em at the UA-default 11pt font-size is 11pt. 11 / 83.8525 = 0.13118.
            Assert.Equal(0.13118, bound, 3);
        }

        // ─── #823: explicit radius no longer throws for em/rem/viewport units ─────────────────────────

        [Fact]
        public async Task ExplicitRadiusEmUnit_DoesNotThrow_AndResolvesToTheRealFontSize()
        {
            // Issue #823's own repro.
            var html = GradientHtml("font-size:20pt; background-image: radial-gradient(2em at center, red, blue);");

            var pdfText = await GetPdfText(html);

            // 2em at font-size:20pt is a 40pt radius.
            Assert.Equal(40.0, ExtractCircleRadius(pdfText), 3);
        }

        [Fact]
        public async Task ExplicitRadiusRemUnit_DoesNotThrow_AndResolvesAgainstTheRootFontSize()
        {
            // GetRemHeight() (DerivedStyle.GetRemHeight) walks to the box tree's synthetic root wrapper and
            // reads its own font-size, which - like every fixture in PixelsPerPointEmResolutionIntegrationTests
            // that doesn't declare an @page font-size - stays the UA-default "medium" (11pt), independent of
            // the declaring element's own font-size (unlike em).
            var html = GradientHtml("font-size:30pt; background-image: radial-gradient(1.5rem at center, red, blue);");

            var pdfText = await GetPdfText(html);

            // 1.5rem at the UA-default root font-size of 11pt is a 16.5pt radius - not 45pt, which 1.5rem
            // would be if it (incorrectly) resolved against the declaring div's own 30pt font-size instead.
            Assert.Equal(16.5, ExtractCircleRadius(pdfText), 3);
        }

        [Fact]
        public async Task ExplicitRadiusViewportUnit_DoesNotThrow_AndResolvesAgainstThePage()
        {
            // Page is 400pt wide (ManualPageWidth) - 10vw is a 40pt radius.
            var html = GradientHtml("background-image: radial-gradient(10vw at center, red, blue);");

            var pdfText = await GetPdfText(html);

            Assert.Equal(40.0, ExtractCircleRadius(pdfText), 3);
        }

        // ─── DPI invariance for the newly-supported relative units ────────────────────────────────────

        [Fact]
        public async Task ExplicitRadiusEmUnit_IsInvariantUnderDifferentPixelsPerInch()
        {
            var html = GradientHtml("font-size:20pt; background-image: radial-gradient(2em at center, red, blue);");

            var r72 = ExtractCircleRadius(await GetPdfText(html, 72));
            var r96 = ExtractCircleRadius(await GetPdfText(html, 96));

            Assert.Equal(r72, r96, 2);
        }

        [Fact]
        public async Task ExplicitRadiusRemUnit_IsInvariantUnderDifferentPixelsPerInch()
        {
            var html = GradientHtml("background-image: radial-gradient(1.5rem at center, red, blue);");

            var r72 = ExtractCircleRadius(await GetPdfText(html, 72));
            var r96 = ExtractCircleRadius(await GetPdfText(html, 96));

            Assert.Equal(r72, r96, 2);
        }

        [Fact]
        public async Task RemStopPosition_IsInvariantUnderDifferentPixelsPerInch()
        {
            var html = GradientHtml("background-image: radial-gradient(circle, red 0, blue 1.5rem, green);");

            var bound72 = ExtractFirstBound(await GetPdfText(html, 72));
            var bound96 = ExtractFirstBound(await GetPdfText(html, 96));

            Assert.Equal(bound72, bound96, 3);
        }
    }
}
