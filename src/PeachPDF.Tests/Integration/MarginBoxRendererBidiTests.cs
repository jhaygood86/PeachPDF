using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct unit tests for <see cref="MarginBoxRenderer.ResolveBidiText"/> - real UAX#9 resolution
    /// (reorder + mirror) for a margin box's resolved <c>content</c> text, following this file's
    /// established convention (see <c>MarginBoxRendererFontTests</c>) of exercising the renderer's
    /// resolution steps directly rather than through full HTML→PDF generation.
    /// </summary>
    public class MarginBoxRendererBidiTests
    {
        [Fact]
        public void DirectionRtl_RealHebrewContent_IsReorderedAndMirrored()
        {
            const string hebrew = "שלום עולם";
            var style = ParseDeclarations("direction: rtl;");

            var result = MarginBoxRenderer.ResolveBidiText(hebrew, style, null);

            var expected = new string(hebrew.Reverse().ToArray());
            Assert.Equal(expected, result);
        }

        [Fact]
        public void DirectionRtl_PlainLatinContent_StaysLogical()
        {
            var style = ParseDeclarations("direction: rtl;");

            var result = MarginBoxRenderer.ResolveBidiText("Page 1", style, null);

            Assert.Equal("Page 1", result);
        }

        [Fact]
        public void DirectionUnset_FallsBackToPageContextDirection()
        {
            const string hebrew = "שלום";
            var marginStyle = ParseDeclarations("content: \"x\";");
            var pageStyle = ParseDeclarations("direction: rtl;");

            var result = MarginBoxRenderer.ResolveBidiText(hebrew, marginStyle, pageStyle);

            var expected = new string(hebrew.Reverse().ToArray());
            Assert.Equal(expected, result);
        }

        [Fact]
        public void DirectionLtr_PlainLatinContent_IsUnaffected()
        {
            // A paragraph's own direction mainly governs its base embedding level/alignment, not whether
            // strong-script content within it reorders - real RTL-script text (see the Hebrew case above)
            // still reorders under direction: ltr too (UAX#9 I1 bumps R characters up to an odd level
            // regardless of the paragraph's own even base level). Plain Latin content, having nothing to
            // reorder either way, is the genuinely unaffected case.
            var style = ParseDeclarations("direction: ltr;");

            var result = MarginBoxRenderer.ResolveBidiText("Page 1", style, null);

            Assert.Equal("Page 1", result);
        }

        private static StyleDeclaration ParseDeclarations(string css) =>
            new StylesheetParser().Parse($"@page {{ @top-left {{ {css} }} }}")
                .Rules.OfType<PageRule>().Single().Margins.Single().Style;
    }
}
