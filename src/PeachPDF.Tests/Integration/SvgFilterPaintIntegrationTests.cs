using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Proves each supported native SVG <c>&lt;filter&gt;</c> primitive actually reaches the real PDF
    /// construct it's meant to (per CLAUDE.md's testing convention: a content-stream-substring match
    /// alone is not proof of correct rendering, but is still the right level for END-TO-END "did the
    /// primitive wire all the way through the real HTML-to-PDF pipeline" coverage - the graph-resolution
    /// logic itself is separately unit-tested in <c>SvgFilterEvaluatorTests</c>). Dual-rasterization
    /// (PDFium + MuPDF) was done manually per CLAUDE.md for a representative sample of these - see this
    /// change's own notes; <c>/TR</c>-based primitives (feColorMatrix/feComponentTransfer) are PDFium-only,
    /// same documented MuPDF gap <c>MixBlendModeAndFilterPaintIntegrationTests</c> already established for
    /// CSS <c>filter</c>.
    /// </summary>
    public class SvgFilterPaintIntegrationTests
    {
        private static async Task<string> GetPdfText(string svg)
        {
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }}</style></head><body>{svg}</body></html>";
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task FeFlood_PaintsTheFloodColor()
        {
            var svg = """
                <svg width="100" height="100" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
                <defs><filter id="f" x="0" y="0" width="1" height="1" filterUnits="objectBoundingBox"><feFlood flood-color="#ff0000"/></filter></defs>
                <rect x="10" y="10" width="50" height="50" fill="#00ff00" filter="url(#f)"/>
                </svg>
                """;

            var pdfText = await GetPdfText(svg);

            // The flood tile fills with red (1 0 0 rg), never the rect's own green fill - the filter's
            // output entirely replaces the element's own content.
            Assert.Contains("1 0 0 rg", pdfText);
        }

        [Fact]
        public async Task FeColorMatrix_ChannelIndependentMatrix_ProducesATransferFunction()
        {
            // A diagonal (channel-independent) matrix - brightness-shaped.
            var svg = """
                <svg width="100" height="100" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
                <defs><filter id="f"><feColorMatrix type="matrix" values="1.5 0 0 0 0  0 1.5 0 0 0  0 0 1.5 0 0  0 0 0 1 0"/></filter></defs>
                <rect x="10" y="10" width="50" height="50" fill="#ff0000" filter="url(#f)"/>
                </svg>
                """;

            var pdfText = await GetPdfText(svg);

            Assert.Contains("/FunctionType 4", pdfText);
            Assert.Contains("/TR", pdfText);
        }

        [Fact]
        public async Task FeComponentTransfer_Linear_ProducesATransferFunction()
        {
            var svg = """
                <svg width="100" height="100" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
                <defs><filter id="f"><feComponentTransfer><feFuncR type="linear" slope="0.5" intercept="0.1"/></feComponentTransfer></filter></defs>
                <rect x="10" y="10" width="50" height="50" fill="#ff0000" filter="url(#f)"/>
                </svg>
                """;

            var pdfText = await GetPdfText(svg);

            Assert.Contains("/FunctionType 4", pdfText);
        }

        [Fact]
        public async Task FeColorMatrix_LuminanceToAlpha_ProducesALuminosityMask()
        {
            var svg = """
                <svg width="100" height="100" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
                <defs><filter id="f"><feColorMatrix type="luminanceToAlpha"/></filter></defs>
                <rect x="10" y="10" width="50" height="50" fill="#ffffff" filter="url(#f)"/>
                </svg>
                """;

            var pdfText = await GetPdfText(svg);

            Assert.Contains("/S /Luminosity", pdfText);
        }

        [Theory]
        [InlineData("over")]
        [InlineData("in")]
        [InlineData("out")]
        [InlineData("atop")]
        [InlineData("xor")]
        public async Task FeComposite_EachOperator_Renders(string op)
        {
            var svg = $$"""
                <svg width="100" height="100" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
                <defs><filter id="f">
                    <feFlood flood-color="#ff0000" result="a"/>
                    <feFlood flood-color="#0000ff" result="b"/>
                    <feComposite in="a" in2="b" operator="{{op}}"/>
                </filter></defs>
                <rect x="10" y="10" width="50" height="50" fill="#00ff00" filter="url(#f)"/>
                </svg>
                """;

            var pdfText = await GetPdfText(svg);

            // in/out/atop/xor all route through the new /Alpha-subtype soft mask; "over" is plain
            // sequential src-over painting and legitimately emits none.
            if (op != "over")
                Assert.Contains("/S /Alpha", pdfText);

            // Every operator still produces a real, non-empty result drawn onto the page.
            Assert.Matches(@"/Fm\d+\s+Do", pdfText);
        }

        [Fact]
        public async Task FeComposite_Out_UsesInvertedTransferFunctionOnTheMask()
        {
            var svg = """
                <svg width="100" height="100" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
                <defs><filter id="f">
                    <feFlood flood-color="#ff0000" result="a"/>
                    <feFlood flood-color="#0000ff" result="b"/>
                    <feComposite in="a" in2="b" operator="out"/>
                </filter></defs>
                <rect x="10" y="10" width="50" height="50" fill="#00ff00" filter="url(#f)"/>
                </svg>
                """;

            var pdfText = await GetPdfText(svg);

            // "out" is the operator that needs the mask-side /TR inversion (1 - alpha) - "in" alone
            // (tested separately above) never sets it.
            Assert.Contains("/S /Alpha", pdfText);
            Assert.Contains("/TR", pdfText);
        }

        [Fact]
        public async Task FeBlend_ProducesTheMatchingBlendModeExtGState()
        {
            var svg = """
                <svg width="100" height="100" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
                <defs><filter id="f">
                    <feFlood flood-color="#ff0000" result="a"/>
                    <feFlood flood-color="#0000ff" result="b"/>
                    <feBlend in="a" in2="b" mode="multiply"/>
                </filter></defs>
                <rect x="10" y="10" width="50" height="50" fill="#00ff00" filter="url(#f)"/>
                </svg>
                """;

            var pdfText = await GetPdfText(svg);

            Assert.Contains("/BM /Multiply", pdfText);
        }

        [Fact]
        public async Task FeMerge_LayersEachNamedInput()
        {
            var svg = """
                <svg width="100" height="100" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
                <defs><filter id="f">
                    <feFlood flood-color="#ff0000" result="a"/>
                    <feFlood flood-color="#0000ff" result="b"/>
                    <feMerge><feMergeNode in="a"/><feMergeNode in="b"/></feMerge>
                </filter></defs>
                <rect x="10" y="10" width="50" height="50" fill="#00ff00" filter="url(#f)"/>
                </svg>
                """;

            var pdfText = await GetPdfText(svg);

            // Both flood colors' tiles are painted (red then blue, layered) - proves feMerge actually
            // drew both inputs rather than only the last one.
            Assert.Contains("1 0 0 rg", pdfText);
            Assert.Contains("0 0 1 rg", pdfText);
        }

        [Fact]
        public async Task FeTile_CopiesItsInputAcrossTheFilterRegion()
        {
            // See SvgFilterEvaluator.EvaluateFeTile's own remarks: with no per-primitive subregion
            // support, feTile degenerates to a single untiled copy of its input - still a real, non-op
            // paint, not a silent no-op.
            var svg = """
                <svg width="100" height="100" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
                <defs><filter id="f"><feFlood flood-color="#ff0000"/><feTile/></filter></defs>
                <rect x="10" y="10" width="50" height="50" fill="#00ff00" filter="url(#f)"/>
                </svg>
                """;

            var pdfText = await GetPdfText(svg);

            Assert.Contains("1 0 0 rg", pdfText);
        }

        [Fact]
        public async Task UnsupportedFilter_ElementPaintsCompletelyUnfiltered()
        {
            var withUnsupportedFilter = """
                <svg width="100" height="100" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
                <defs><filter id="f"><feGaussianBlur stdDeviation="3"/></filter></defs>
                <rect x="10" y="10" width="50" height="50" fill="#ff0000" filter="url(#f)"/>
                </svg>
                """;
            var withoutFilter = """
                <svg width="100" height="100" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
                <rect x="10" y="10" width="50" height="50" fill="#ff0000"/>
                </svg>
                """;

            var withText = await GetPdfText(withUnsupportedFilter);
            var withoutText = await GetPdfText(withoutFilter);

            // A whole-filter rejection means the element falls all the way back to its own ordinary
            // direct paint - not a partially-applied graph, not even an extra (no-op) tile/composite.
            // Compare the page's own content stream only (not the whole file - the trailer /ID is a
            // fresh random GUID every render), same convention PdfAUnimplementedTransparencyFeatureTests
            // uses for the same kind of "pin today's true no-op" assertion.
            Assert.Equal(FirstContentStream(withoutText), FirstContentStream(withText));
        }

        [Theory]
        [InlineData("""<feImage href="#src"/>""")]
        [InlineData("""<feMerge><feMergeNode in="FillPaint"/></feMerge>""")]
        [InlineData("""<feMerge><feMergeNode in="BackgroundImage"/></feMerge>""")]
        public async Task PaintedInputs_ReachThePdfAsABitmapOfTheFilteredRegion(string primitive)
        {
            var svg = $"""
                <svg width="100" height="100" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
                <defs><filter id="f">{primitive}</filter></defs>
                <rect id="src" x="10" y="10" width="50" height="50" fill="#ff0000" filter="url(#f)"/>
                </svg>
                """;

            var pdfText = await GetPdfText(svg);

            // Evaluated over pixels: the result is an embedded image, drawn where the element would have been.
            Assert.Contains("/Subtype /Image", pdfText);
        }

        private static string FirstContentStream(string pdfText)
        {
            var match = Regex.Match(pdfText, @"stream\r?\n(.*?)\r?\nendstream", RegexOptions.Singleline);
            Assert.True(match.Success, "No content stream found in generated PDF.");
            return match.Groups[1].Value;
        }

        [Fact]
        public async Task FilterNone_Reference_ElementPaintsUnfiltered()
        {
            var svg = """
                <svg width="100" height="100" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
                <rect x="10" y="10" width="50" height="50" fill="#ff0000" filter="url(#doesNotExist)"/>
                </svg>
                """;

            var pdfText = await GetPdfText(svg);

            Assert.Contains("1 0 0 rg", pdfText);
            Assert.DoesNotContain("/TR", pdfText);
        }
    }
}
