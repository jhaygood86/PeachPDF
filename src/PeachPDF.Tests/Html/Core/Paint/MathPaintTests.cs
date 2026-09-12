using PeachPDF.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Paint
{
    /// <summary>
    /// Paint-order coverage for MathML (per CLAUDE.md's painting-changes testing convention: assert
    /// the actual sequence of <c>RGraphics</c> calls, not just that painting completes or that some
    /// token shows up in the final PDF).
    /// </summary>
    public class MathPaintTests
    {
        static string Wrap(string mathHtml) =>
            $"<html><head><style>{BundledFonts.FontFaceRule(BundledFonts.Math, "TestMath", "font/truetype")} " +
            $"math {{ font-family: TestMath; }}</style></head><body style='margin:0'>{mathHtml}</body></html>";

        [Fact]
        public async Task Fraction_DrawsRuleBetweenNumeratorAndDenominatorTokenDraws()
        {
            var html = Wrap("<math><mfrac><mn>1</mn><mn>2</mn></mfrac></math>");

            var (root, container) = await LayoutHarness.LayoutAsync(html);
            Assert.NotNull(LayoutHarness.Descendants(root).FirstOrDefault(b => b is CssBoxMath));

            var adapter = new PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, recording);

            var kinds = recording.Log.Where(op => op.Kind is PaintOpKind.FillRect or PaintOpKind.DrawString).ToList();

            // Exactly one fraction bar (a filled rectangle) and two token draws (numerator "1",
            // denominator "2") - the fixture has no other content that would paint either kind.
            // MathRenderer paints a box's own paint data (here, the fraction's rule) before recursing
            // into its children (numerator, then denominator) - so the rule draws first.
            var ruleIndex = kinds.FindIndex(op => op.Kind == PaintOpKind.FillRect);
            var stringIndices = kinds.Select((op, i) => (op, i)).Where(t => t.op.Kind == PaintOpKind.DrawString).Select(t => t.i).ToList();

            Assert.True(ruleIndex >= 0, "fraction bar was never drawn");
            Assert.Equal(2, stringIndices.Count);
            Assert.True(ruleIndex < stringIndices[0] && stringIndices[0] < stringIndices[1],
                $"expected rule, then numerator draw, then denominator draw - got rule at {ruleIndex}, strings at [{string.Join(",", stringIndices)}]");
        }

        [Fact]
        public async Task StretchyFence_DrawsGlyphsNotPlainText()
        {
            var html = Wrap("<math><mrow><mo stretchy=\"true\">(</mo><mfrac><mi>x</mi><mi>y</mi></mfrac><mo stretchy=\"true\">)</mo></mrow></math>");

            var (root, container) = await LayoutHarness.LayoutAsync(html);
            Assert.NotNull(LayoutHarness.Descendants(root).FirstOrDefault(b => b is CssBoxMath));

            var adapter = new PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, recording);

            // A stretched fence paints via the raw-glyph-index primitive (RGraphics.DrawGlyphs), not
            // DrawString - see MathLayoutEngine.StretchToken/MathRenderer.
            Assert.Contains(recording.Log, op => op.Kind == PaintOpKind.DrawGlyphs);
        }

        [Fact]
        public async Task Row_TokensDrawLeftToRight_InDocumentOrder()
        {
            var html = Wrap("<math><mi>a</mi><mo>+</mo><mi>b</mi></math>");

            var (root, container) = await LayoutHarness.LayoutAsync(html);
            Assert.NotNull(LayoutHarness.Descendants(root).FirstOrDefault(b => b is CssBoxMath));

            var adapter = new PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, recording);

            var strings = recording.Log.Where(op => op.Kind == PaintOpKind.DrawString).Select(op => op.Text).ToList();
            // "a"/"b" are single-character mi tokens, so MathML Core's math-auto default italicizes
            // them (see MathItalicMappingsTests); "+" is an mo token, never affected by it.
            Assert.Equal(new[] { char.ConvertFromUtf32(0x1D44E), "+", char.ConvertFromUtf32(0x1D44F) }, strings);
        }
    }
}
