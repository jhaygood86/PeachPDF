using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end: <c>font-palette</c>/<c>@font-palette-values</c> reaching SVG text in the generated PDF, for an inline
    /// <c>&lt;svg&gt;</c> (page registry) and a standalone SVG image (its own <c>&lt;style&gt;</c> registry). The
    /// palette-2 blue of Nabla's <c>A</c> must be painted - and must not be when no palette is selected.
    /// </summary>
    public class SvgTextFontPaletteIntegrationTests
    {
        private static async Task<string> Render(string body, string css = "")
        {
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.Nabla);
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.Nabla))
                await generator.AddFontFromStream(stream);

            var html = $"<!DOCTYPE html><html><head><style>{css.Replace("FAMILY", family)}</style></head><body>{body.Replace("FAMILY", family)}</body></html>";
            var doc = await generator.GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        // Palette 2's blue is (0, 0.627, 0.882).
        private static bool HasBlue(string pdf) =>
            Regex.IsMatch(pdf, @"(?<![\d.])0(?:\.0+)? 0\.62\d* 0\.88\d* rg|/C[01] \[0(?:\.0+)? 0\.62\d* 0\.88\d*\]");

        private const string Text = "<text x='10' y='80' font-family='FAMILY' font-size='80'{0}>A</text>";

        [Fact]
        public async Task InlineSvg_FontPalette_SelectsPalette()
        {
            var pdf = await Render($"<svg width='200' height='100'>{string.Format(Text, " font-palette='--blue'")}</svg>",
                "@font-palette-values --blue { font-family: 'FAMILY'; base-palette: 2; }");

            Assert.True(HasBlue(pdf), "inline SVG text should paint CPAL palette 2");
        }

        [Fact]
        public async Task InlineSvg_NoFontPalette_DoesNotPaintPalette2()
        {
            var pdf = await Render($"<svg width='200' height='100'>{string.Format(Text, "")}</svg>",
                "@font-palette-values --blue { font-family: 'FAMILY'; base-palette: 2; }");

            Assert.False(HasBlue(pdf));
        }

        [Fact]
        public async Task StandaloneSvg_OwnPaletteRegistry_SelectsPalette()
        {
            var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='100'>" +
                      "<style>@font-palette-values --blue { font-family: 'FAMILY'; base-palette: 2; }</style>" +
                      string.Format(Text, " font-palette='--blue'") + "</svg>";
            svg = svg.Replace("FAMILY", TypefaceFixtures.FamilyNameOf(BundledFonts.Nabla));
            var uri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));

            var pdf = await Render($"<img src=\"{uri}\" width='200' height='100'>");

            Assert.True(HasBlue(pdf), "standalone SVG text should paint CPAL palette 2");
        }
    }
}
