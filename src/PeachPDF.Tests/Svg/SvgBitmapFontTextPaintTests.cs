using PeachPDF.Tests.TestSupport;
using System.Text;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// SVG <c>&lt;text&gt;</c> in a bitmap-only font (no outlines to trace) with a gradient fill and a stroke: painted through the raster
    /// backend, with the text still present as invisible, selectable text.
    /// </summary>
    public class SvgBitmapFontTextPaintTests
    {
        private static async Task<string> RenderAsync(string textElement)
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.RealCbdt))
                await generator.AddFontFromStream(stream);

            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.RealCbdt);
            var html = $$"""
                <!DOCTYPE html><html><body>
                <svg width="200" height="120" viewBox="0 0 200 120">
                  <defs><linearGradient id="g"><stop offset="0" stop-color="red"/><stop offset="1" stop-color="blue"/></linearGradient></defs>
                  {{textElement.Replace("FAMILY", family)}}
                </svg></body></html>
                """;
            var doc = await generator.GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task GradientFill_OnABitmapGlyph_IsDrawnThroughTheRasterBackend()
        {
            var pdf = await RenderAsync("""<text x="10" y="100" font-family="FAMILY" font-size="80" fill="url(#g)">A</text>""");

            Assert.Contains("/Subtype /Image", pdf);
            // The glyph is also supplied as invisible text (render mode 3), so it can be selected and searched.
            Assert.Contains("3 Tr", pdf);
        }

        [Fact]
        public async Task Stroke_OnABitmapGlyph_IsPaintedAsABandAroundTheGlyph()
        {
            var filled = await RenderAsync("""<text x="10" y="100" font-family="FAMILY" font-size="80" fill="url(#g)">A</text>""");
            var stroked = await RenderAsync("""<text x="10" y="100" font-family="FAMILY" font-size="80" fill="url(#g)" stroke="rgb(255,0,0)" stroke-width="4">A</text>""");

            // A second raster image (the stroke band) is drawn.
            Assert.True(Count(stroked, "/Subtype /Image") > Count(filled, "/Subtype /Image"));
        }

        private static int Count(string haystack, string needle)
        {
            var count = 0;
            for (var i = haystack.IndexOf(needle, System.StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + 1, System.StringComparison.Ordinal))
                count++;
            return count;
        }
    }
}
