using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A variable colour font (COLR version 1 with variable paints) drawn at a location of its design space: the PDF paints the numbers that
    /// apply there, without the PDF writer knowing anything about variations.
    /// </summary>
    public class VariableColorFontIntegrationTests
    {
        private static async Task<string> RenderAsync(string body, string css = "")
        {
            var b64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.VariableColorTest));
            var html = $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'VC'; src: url('data:font/truetype;base64,{b64}') format('truetype'); font-weight: 100 900; font-stretch: 75% 125%; }}
body {{ font-family: 'VC'; font-size: 60pt; }}
{css}
</style></head><body>{body}</body></html>";

            var doc = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            var stream = new MemoryStream();
            doc.Save(stream);
            return Encoding.Latin1.GetString(stream.ToArray());
        }

        /// <summary>The fill opacities the colour glyphs are painted with (the <c>/ca</c> entries of the graphics states).</summary>
        private static List<double> Opacities(string pdf) =>
            Regex.Matches(pdf, @"/ca\s+(-?\d*\.?\d+)").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().ToList();

        [Fact]
        public async Task ThePaintOfAColourGlyph_FollowsTheWeight()
        {
            // 'A' is a box filled by PaintVarSolid whose opacity is 0.4 at weight 100 and 0.87 at weight 900 (both are masters).
            var light = Opacities(await RenderAsync("A", "body { font-weight: 100 }"));
            var heavy = Opacities(await RenderAsync("A", "body { font-weight: 900 }"));

            Assert.Contains(light, a => Math.Abs(a - 0.4) < 0.01);
            Assert.Contains(heavy, a => Math.Abs(a - 0.87) < 0.01);
            Assert.DoesNotContain(light, a => Math.Abs(a - 0.87) < 0.01);
        }

        [Fact]
        public async Task TheTwoWeightsOfOneDocument_EachKeepTheirOwnPaint()
        {
            var both = Opacities(await RenderAsync("<div style=\"font-weight: 100\">A</div><div style=\"font-weight: 900\">A</div>"));

            Assert.Contains(both, a => Math.Abs(a - 0.4) < 0.01);
            Assert.Contains(both, a => Math.Abs(a - 0.87) < 0.01);
        }
    }
}
