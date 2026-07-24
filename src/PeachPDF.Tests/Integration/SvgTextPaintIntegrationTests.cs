using PeachPDF;
using PeachPDF.Fonts;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage that gradient/pattern <c>fill</c> and <c>stroke</c> on SVG
    /// <c>&lt;text&gt;</c> (issue #187) render through real glyph outlines in the content stream
    /// (path fills/strokes, not a single-color <c>Tj</c> text show), for a regular <c>glyf</c> text
    /// font - while plain solid text keeps the selectable <c>Tj</c> fast path. Uses the bundled
    /// Source Sans 3 (TrueType) and Source Code Pro (CFF) fonts.
    /// </summary>
    public class SvgTextPaintIntegrationTests
    {
        private static async Task<string> RenderSvgText(string fontPath, string textAttrs, string defs = "", string text = "Hi")
        {
            var family = TtfFontDescription.LoadDescription(fontPath).FontFamilyInvariantCulture;
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(fontPath))
                await generator.AddFontFromStream(stream);

            var svg = $$"""
                <svg xmlns="http://www.w3.org/2000/svg" width="300" height="120" viewBox="0 0 300 120">
                  <defs>{{defs}}</defs>
                  <text x="10" y="80" font-family="{{family}}" font-size="60" {{textAttrs}}>{{text}}</text>
                </svg>
                """;

            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }}</style></head><body>{svg}</body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private static int Count(string haystack, string needle)
        {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        [Fact]
        public async Task GradientFillText_PaintsShadingOutline_NoTextShow()
        {
            string pdf = await RenderSvgText(BundledFonts.Ttf,
                """fill="url(#g)" """,
                """<linearGradient id="g"><stop offset="0" stop-color="red"/><stop offset="1" stop-color="blue"/></linearGradient>""");

            // Outlined text - a gradient shading fills the glyph path, and there is no CID text show.
            Assert.Contains("/ShadingType", pdf);
            Assert.Equal(0, Count(pdf, " Tj"));
        }

        [Fact]
        public async Task StrokedText_PaintsStrokeOperator_NoTextShow()
        {
            string pdf = await RenderSvgText(BundledFonts.Ttf,
                """fill="rgb(0,128,0)" stroke="rgb(0,0,255)" stroke-width="2" """);

            // Fill then stroke of the glyph outline; no selectable text show.
            Assert.Contains("\nf\n", pdf);
            Assert.Contains("\nS\n", pdf);
            Assert.Equal(0, Count(pdf, " Tj"));
        }

        [Fact]
        public async Task SolidText_KeepsSelectableTextShow()
        {
            string pdf = await RenderSvgText(BundledFonts.Ttf, """fill="rgb(20,40,60)" """);

            // Plain solid, non-stroked text stays a real CID text run (selectable/tagged-friendly).
            Assert.True(Count(pdf, " Tj") > 0, "expected a Tj text show for plain solid text");
            Assert.DoesNotContain("/ShadingType", pdf);
        }

        [Fact]
        public async Task PatternFillText_TilesFormXObject_NoTextShow()
        {
            string pdf = await RenderSvgText(BundledFonts.Ttf,
                """fill="url(#p)" """,
                """<pattern id="p" width="10" height="10" patternUnits="userSpaceOnUse"><rect width="5" height="10" fill="orange"/></pattern>""");

            // The pattern tile is drawn as a repeated Form XObject clipped to the glyph outline.
            Assert.Matches(@"/Fm\d+ Do", pdf);
            Assert.Equal(0, Count(pdf, " Tj"));
        }

        [Fact]
        public async Task RadialGradientFillText_PaintsShadingOutline_NoTextShow()
        {
            string pdf = await RenderSvgText(BundledFonts.Ttf,
                """fill="url(#rg)" """,
                """<radialGradient id="rg"><stop offset="0" stop-color="#ffd23f"/><stop offset="1" stop-color="#ee4266"/></radialGradient>""");

            Assert.Contains("/ShadingType", pdf);
            Assert.Equal(0, Count(pdf, " Tj"));
        }

        [Fact]
        public async Task CffFont_StrokedText_FallsBackToSolidTextShow()
        {
            // Source Code Pro is CFF (no glyf), so no outline can be built: a stroke can't be honored
            // and the run falls back to a solid CID text show of its fill color.
            string pdf = await RenderSvgText(BundledFonts.Otf,
                """fill="rgb(0,128,0)" stroke="rgb(0,0,255)" stroke-width="2" """);

            Assert.True(Count(pdf, " Tj") > 0, "expected a fallback Tj text show for a CFF font");
        }

        [Fact]
        public async Task CffFont_TextPath_StrokedGlyphs_FallBackToSolidTextShow()
        {
            // A stroked <textPath> on a CFF font: each glyph has no outline, so it falls back to a solid
            // text show of its fill color (the stroke can't be honored).
            var family = TtfFontDescription.LoadDescription(BundledFonts.Otf).FontFamilyInvariantCulture;
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.Otf))
                await generator.AddFontFromStream(stream);

            var svg = $$"""
                <svg xmlns="http://www.w3.org/2000/svg" width="300" height="120" viewBox="0 0 300 120">
                  <defs><path id="p" d="M10,80 L280,80"/></defs>
                  <text font-family="{{family}}" font-size="40"><textPath href="#p" fill="rgb(0,128,0)" stroke="rgb(0,0,255)" stroke-width="2">Hi</textPath></text>
                </svg>
                """;
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }}</style></head><body>{svg}</body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            var pdf = Encoding.Latin1.GetString(ms.ToArray());

            Assert.True(Count(pdf, " Tj") > 0, "expected a fallback Tj text show per glyph for a CFF textPath");
        }
    }
}
