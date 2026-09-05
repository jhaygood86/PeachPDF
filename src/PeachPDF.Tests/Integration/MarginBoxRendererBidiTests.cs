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

            var result = MarginBoxRenderer.ResolveBidiText(hebrew, style, null, out var logicalText);

            var expected = new string(hebrew.Reverse().ToArray());
            Assert.Equal(expected, result);
            // The whole string is a single RTL run (surrounding neutrals resolve to R), so it's a pure
            // whole-string reversal - the caller can recover the true logical-order source for ToUnicode
            // text extraction. logicalText is positionally aligned with the visual result (not with the
            // original hebrew string) - here that happens to equal the same reversed string, since no
            // Hebrew letter is a mirrorable character (mirroring only changes a character's value, and
            // none of these do).
            Assert.Equal(expected, logicalText);
        }

        [Fact]
        public void DirectionRtl_PlainLatinContent_StaysLogical()
        {
            var style = ParseDeclarations("direction: rtl;");

            var result = MarginBoxRenderer.ResolveBidiText("Page 1", style, null, out var logicalText);

            Assert.Equal("Page 1", result);
            // Nothing was reordered - the visual string already equals the logical one, so there is
            // nothing to recover.
            Assert.Null(logicalText);
        }

        [Fact]
        public void DirectionUnset_FallsBackToPageContextDirection()
        {
            const string hebrew = "שלום";
            var marginStyle = ParseDeclarations("content: \"x\";");
            var pageStyle = ParseDeclarations("direction: rtl;");

            var result = MarginBoxRenderer.ResolveBidiText(hebrew, marginStyle, pageStyle, out var logicalText);

            var expected = new string(hebrew.Reverse().ToArray());
            Assert.Equal(expected, result);
            Assert.Equal(expected, logicalText);
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

            var result = MarginBoxRenderer.ResolveBidiText("Page 1", style, null, out var logicalText);

            Assert.Equal("Page 1", result);
            Assert.Null(logicalText);
        }

        [Fact]
        public void DirectionRtl_MirroredPunctuation_LogicalTextIsPositionallyAlignedNotJustReversed()
        {
            // Parens are mirrorable (unlike any Hebrew letter) - '(' becomes ')' and vice versa, not just
            // repositioned. logicalText must be positionally aligned with the *visual* result (each
            // position holding the true character whose mirrored image is painted there), not simply the
            // original string reversed-and-left-at-that - the two coincide for the plain-Hebrew cases
            // above only because none of those characters actually change value under mirroring.
            const string source = "(אב)";
            var style = ParseDeclarations("direction: rtl;");

            var result = MarginBoxRenderer.ResolveBidiText(source, style, null, out var logicalText);

            Assert.Equal("(בא)", result);
            Assert.Equal(")בא(", logicalText);
        }

        [Fact]
        public void MixedDirectionRuns_LogicalTextNotRecovered()
        {
            // A Latin word embedded in a longer Hebrew (RTL) paragraph produces more than one bidi run -
            // the visual string is a per-run reorder-and-concatenate, not a single end-to-end reversal, so
            // ResolveBidiText must not claim a recoverable logical source for it (the caller's ToUnicode
            // remap formula assumes a pure whole-string reversal and would silently corrupt this case).
            const string mixed = "שלום Latin עולם";
            var style = ParseDeclarations("direction: rtl;");

            MarginBoxRenderer.ResolveBidiText(mixed, style, null, out var logicalText);

            Assert.Null(logicalText);
        }

        private static StyleDeclaration ParseDeclarations(string css) =>
            new StylesheetParser().Parse($"@page {{ @top-left {{ {css} }} }}")
                .Rules.OfType<PageRule>().Single().Margins.Single().Style;
    }
}
