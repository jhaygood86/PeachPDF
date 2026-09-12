using System.IO;
using System.Text;
using System.Threading.Tasks;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end smoke coverage: representative MathML formulas actually render through the full
    /// HTML-&gt;PDF pipeline without throwing, using the bundled STIX Two Math font (a real MATH table)
    /// so the layout engine exercises its real-font code paths, not just the no-MATH-table fallback.
    /// </summary>
    public class MathSmokeTests
    {
        static string FontFace() =>
            BundledFonts.FontFaceRule(BundledFonts.Math, "TestMath", "font/truetype");

        static async Task<string> GetPdfText(string mathHtml)
        {
            var html = $"<html><head><style>{FontFace()} math {{ font-family: TestMath; }}</style></head>" +
                       $"<body>{mathHtml}</body></html>";
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task SimpleIdentifier_RendersWithoutThrowing()
        {
            var pdfText = await GetPdfText("<math><mi>x</mi></math>");
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task Fraction_RendersRuleAndOperands()
        {
            var pdfText = await GetPdfText("<math><mfrac><mn>1</mn><mn>2</mn></mfrac></math>");
            // A fraction bar is a filled rectangle - "re" (rectangle) + fill operator should appear.
            Assert.Contains("re\n", pdfText);
        }

        [Fact]
        public async Task SquareRoot_RendersWithoutThrowing()
        {
            var pdfText = await GetPdfText("<math><msqrt><mi>x</mi></msqrt></math>");
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task Superscript_RendersWithoutThrowing()
        {
            var pdfText = await GetPdfText("<math><msup><mi>x</mi><mn>2</mn></msup></math>");
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task Subscript_RendersWithoutThrowing()
        {
            var pdfText = await GetPdfText("<math><msub><mi>x</mi><mn>1</mn></msub></math>");
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task StretchyFences_RenderWithoutThrowing()
        {
            var pdfText = await GetPdfText(
                "<math><mrow><mo stretchy=\"true\">(</mo><mfrac><mi>x</mi><mi>y</mi></mfrac><mo stretchy=\"true\">)</mo></mrow></math>");
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task ComplexFormula_RendersWithoutThrowing()
        {
            // A quadratic-formula-shaped expression exercising fraction + radical + scripts + row
            // spacing together.
            var mathml = "<math display=\"block\">" +
                "<mi>x</mi><mo>=</mo>" +
                "<mfrac>" +
                "<mrow><mo>-</mo><mi>b</mi><mo>&#177;</mo><msqrt><mrow><msup><mi>b</mi><mn>2</mn></msup><mo>-</mo><mn>4</mn><mi>a</mi><mi>c</mi></mrow></msqrt></mrow>" +
                "<mrow><mn>2</mn><mi>a</mi></mrow>" +
                "</mfrac>" +
                "</math>";
            var pdfText = await GetPdfText(mathml);
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task Table_RendersWithoutThrowing()
        {
            var mathml = "<math><mtable>" +
                "<mtr><mtd><mn>1</mn></mtd><mtd><mn>0</mn></mtd></mtr>" +
                "<mtr><mtd><mn>0</mn></mtd><mtd><mn>1</mn></mtd></mtr>" +
                "</mtable></math>";
            var pdfText = await GetPdfText(mathml);
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task MfencedDesugars_RendersWithoutThrowing()
        {
            var pdfText = await GetPdfText("<math><mfenced><mi>x</mi><mi>y</mi></mfenced></math>");
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task Semantics_RendersFirstChildOnly()
        {
            var pdfText = await GetPdfText(
                "<math><semantics><mi>x</mi><annotation encoding=\"application/x-tex\">x</annotation></semantics></math>");
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task UnderOver_RendersWithoutThrowing()
        {
            var pdfText = await GetPdfText(
                "<math><munder><mi>lim</mi><mrow><mi>x</mi><mo>&#8594;</mo><mn>0</mn></mrow></munder>" +
                "<mover accent=\"true\"><mi>x</mi><mo>^</mo></mover>" +
                "<munderover><mo>&#8721;</mo><mrow><mi>i</mi><mo>=</mo><mn>0</mn></mrow><mi>n</mi></munderover></math>");
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task Multiscripts_WithPrescripts_RendersWithoutThrowing()
        {
            var pdfml = "<math><mmultiscripts><mi>X</mi>" +
                "<mn>1</mn><mn>2</mn>" +
                "<mprescripts/><mn>3</mn><none/>" +
                "</mmultiscripts></math>";
            var pdfText = await GetPdfText(pdfml);
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task Space_Phantom_Padded_Enclose_Error_RenderWithoutThrowing()
        {
            var pdfText = await GetPdfText(
                "<math><mspace width=\"1em\" height=\"2pt\" depth=\"1pt\"/>" +
                "<mphantom><mi>x</mi></mphantom>" +
                "<mpadded width=\"2em\" lspace=\"1em\"><mi>y</mi></mpadded>" +
                "<menclose notation=\"box\"><mi>z</mi></menclose>" +
                "<merror><mtext>bad</mtext></merror></math>");
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task Action_SelectionAndNoChildren_RenderWithoutThrowing()
        {
            var withSelection = await GetPdfText(
                "<math><maction actiontype=\"toggle\" selection=\"2\"><mi>a</mi><mi>b</mi></maction></math>");
            Assert.Contains("/Type /Page", withSelection);

            var noChildren = await GetPdfText("<math><maction/></math>");
            Assert.Contains("/Type /Page", noChildren);

            var outOfRangeSelection = await GetPdfText(
                "<math><maction selection=\"99\"><mi>a</mi><mi>b</mi></maction></math>");
            Assert.Contains("/Type /Page", outOfRangeSelection);
        }

        [Fact]
        public async Task MlabeledtrAndUnrecognizedElement_RenderWithoutThrowing()
        {
            var pdfText = await GetPdfText(
                "<math><mtable><mlabeledtr><mtd><mn>1</mn></mtd><mtd><mi>x</mi></mtd></mlabeledtr></mtable>" +
                "<mfoo><mi>x</mi></mfoo></math>");
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task MstyleOverridesDisplaystyleScriptlevelMathsize_RendersWithoutThrowing()
        {
            var pdfText = await GetPdfText(
                "<math><mstyle displaystyle=\"true\" scriptlevel=\"+1\" mathsize=\"150%\" mathcolor=\"red\">" +
                "<mfrac><mi>x</mi><mi>y</mi></mfrac></mstyle></math>");
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task EmptyMath_RendersWithoutThrowing()
        {
            var pdfText = await GetPdfText("<math></math>");
            Assert.Contains("/Type /Page", pdfText);
        }

        [Fact]
        public async Task NoMathTable_FallsBackGracefully()
        {
            // Deliberately no font-family override - the default resolved font almost certainly has no
            // MATH table, exercising MathMetrics' TeX-derived fallback path end to end.
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf("<html><body><math><mfrac><mi>x</mi><mi>y</mi></mfrac></math></body></html>", config);
            var ms = new MemoryStream();
            doc.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());
            Assert.Contains("/Type /Page", pdfText);
        }
    }
}
