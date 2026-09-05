using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Real UAX#9 bidi resolution for SVG <c>&lt;text&gt;</c> (SVG 2 §11.3.1: the same
    /// <c>direction</c>/<c>unicode-bidi</c> properties and algorithm CSS text uses). Renders through
    /// <see cref="SvgRenderer.RenderInto"/> into a <see cref="TestRecordingGraphics"/> and asserts on the
    /// actual painted <c>DrawString</c> text - not just that a bidi property parses.
    /// </summary>
    public class SvgTextBidiTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static TestRecordingGraphics Render(string body)
        {
            var markup = $$"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
                  {{body}}
                </svg>
                """;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new RRect(0, 0, 200, 100));
            return g;
        }

        [Fact]
        public void Text_DirectionRtl_RealHebrewContent_IsReorderedAndMirrored()
        {
            const string hebrew = "שלום עולם";
            var g = Render($"""<text x="10" y="50" font-size="20" direction="rtl">{hebrew}</text>""");

            var draw = Assert.Single(g.DrawStringCalls);

            // L2 reverses the whole (uniform-level, since it's all strong-R plus a neutral space) run;
            // L4 would additionally mirror any bracket-like characters, of which this text has none.
            var expected = new string(hebrew.Reverse().ToArray());
            Assert.Equal(expected, draw.Text);
        }

        [Fact]
        public void Text_DirectionRtl_PlainLatinContent_StaysLogical()
        {
            // UAX#9 I2 bumps strong-L characters to the next even level inside an RTL (odd) paragraph, so
            // plain Latin text is not reordered even under direction="rtl" - unlike real RTL-script text.
            var g = Render("""<text x="10" y="50" font-size="20" direction="rtl">AB CD</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("AB CD", draw.Text);
        }

        [Fact]
        public void Text_DirectionRtl_AstralCharacterAmongLatinText_StaysLogicalNoMisindexing()
        {
            // Issue #555: ApplyBidiReordering built its per-glyph level array by indexing a UTF-16
            // string's BidiResolver.Resolve output (one entry per code unit) directly by glyph ordinal
            // (one entry per Rune/FlattenRun glyph) - a surrogate pair (any codepoint above U+FFFF,
            // like the grinning-face emoji U+1F600 here) is 2 code units for 1 glyph, so everything
            // from the first astral character onward read the wrong level, corrupting reordering and
            // (before the fix) risking an out-of-range read entirely for text ending in/after one.
            // Plain Latin text plus a neutral emoji, both surrounded by strong-L characters, resolves
            // to one uniform logical (unreversed) run under UAX#9 N1/N2 even inside an RTL paragraph -
            // mirroring Text_DirectionRtl_PlainLatinContent_StaysLogical's own reasoning.
            const string text = "AB\U0001F600CD";
            var g = Render($"""<text x="10" y="50" font-size="20" direction="rtl">{text}</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(text, draw.Text);
        }

        [Fact]
        public void Text_DirectionRtl_MirrorsParentheses()
        {
            const string hebrew = "שלום (עולם)";
            var g = Render($"""<text x="10" y="50" font-size="20" direction="rtl">{hebrew}</text>""");

            var draw = Assert.Single(g.DrawStringCalls);

            // Reversing the whole run then mirroring each character's shape puts an ordinary '(' back at
            // the run's start and ')' back at its end, wrapping the reversed enclosed word - and since
            // reversing the whole line also reverses the two words' relative order, the parenthesized
            // group (originally second/last) ends up first (see the equivalent HTML characterization in
            // ShapingCharacterizationTests).
            var reversedOlam = new string("עולם".Reverse().ToArray());
            Assert.Contains($"({reversedOlam})", draw.Text);
        }

        [Fact]
        public void Text_DirectionRtl_MirroredPunctuation_LogicalTextIsPositionallyAligned()
        {
            // Regression: SvgRenderer.ApplyBidiReordering has the identical bidi-mirroring bug HTML's
            // ToUnicode fix addresses (see CMapInfoLogicalTextTests/RtlToUnicodeIntegrationTests) - a
            // parenthesized RTL word swaps parenthesis identity, not just position, and painting must
            // hand DrawString a positionally-aligned logical source so text extraction recovers it.
            // unicode-bidi="bidi-override" (like Text_BidiOverrideReversesGlyphsOfDifferingAdvance_NoGapOrOverlap
            // above) forces every character, parens and Latin letters alike, to one uniform RTL level -
            // without it, plain direction="rtl" alone would bump the Latin letters to their own even
            // level (UAX#9 I2, see Text_DirectionRtl_PlainLatinContent_StaysLogical) and split this into
            // more than one run/DrawString call, the same mechanism the HTML embedding-level-split test
            // (CssLayoutEngineBidiTests.RtlParagraph_DigitRunWithNoSurroundingWhitespace_SplitsIntoItsOwnWord)
            // covers.
            var g = Render("""<text x="10" y="50" font-size="20" direction="rtl" unicode-bidi="bidi-override">(AB)</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("(BA)", draw.Text);
            // '(' at the painted string's start stands in for the logical closing ')', and vice versa
            // for the trailing ')' - logicalText must be positionally aligned with draw.Text, not simply
            // the original "(AB)" left as-is.
            Assert.Equal(")BA(", draw.LogicalText);
        }

        [Fact]
        public void Text_ExplicitRotatePerCharacter_MirroredPunctuation_LogicalTextIsPerCharacterAligned()
        {
            // An explicit rotate="" forces PaintRotatedGlyph's own single-glyph-per-DrawString path
            // (distinct from PaintGlyphs' batched-run path the test above covers) - each call's
            // logicalText must be that one glyph's own true logical source (GlyphInfo.LogicalGlyph),
            // not a whole-batch string.
            var g = Render(
                """<text x="10" y="50" font-size="20" direction="rtl" unicode-bidi="bidi-override" rotate="10 10 10 10">(AB)</text>""");

            // One DrawString call per character, in painted (visual, post-reorder) order: '(' 'B' 'A' ')'.
            // 'B'/'A' were never mirrored, so GlyphInfo.LogicalGlyph (unlike HTML's whole-word logicalText)
            // stays null for them - there is nothing distinct to recover per character, and CMapInfo
            // treats null the same as "identical to what's painted".
            Assert.Equal(4, g.DrawStringCalls.Count);
            Assert.Equal([("(", ")"), ("B", null), ("A", null), (")", "(")],
                g.DrawStringCalls.Select(c => (c.Text, c.LogicalText)));
        }

        [Fact]
        public void Text_DirectionLtr_PlainContent_HasNoLogicalTextOverride()
        {
            // The overwhelming common case: nothing was mirrored, so there is nothing to recover.
            var g = Render("""<text x="10" y="50" font-size="20">AB CD</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Null(draw.LogicalText);
        }

        [Fact]
        public void TSpan_UnicodeBidiIsolate_DoesNotLeakIntoSurroundingLtrText()
        {
            // An isolated RTL tspan's own reordering must not affect how the surrounding LTR text's own
            // words are ordered - mirrors CssLayoutEngineBidiTests' <bdi> isolation test for HTML.
            var g = Render(
                """<text x="10" y="50" font-size="20">before <tspan unicode-bidi="isolate" direction="rtl">שלום עולם</tspan> after</text>""");

            var texts = g.DrawStringCalls.Select(c => c.Text).ToList();

            // "before"/"after" are painted in their own authored (unreversed) form...
            Assert.Contains(texts, t => t.Contains("before"));
            Assert.Contains(texts, t => t.Contains("after"));

            // ...while the isolated tspan's own two words reorder relative to each other: the second
            // logical word (עולם) ends up visually before the first (שלום).
            var hebrewCall = Assert.Single(texts, t => t.Contains('ם') || t.Contains('ש'));
            var reversedOlam = new string("עולם".Reverse().ToArray());
            var reversedShalom = new string("שלום".Reverse().ToArray());
            Assert.True(hebrewCall.IndexOf(reversedOlam, System.StringComparison.Ordinal) < hebrewCall.IndexOf(reversedShalom, System.StringComparison.Ordinal),
                $"expected the isolated tspan's own words to reorder; text='{hebrewCall}'");
        }

        [Fact]
        public void Text_BidiOverrideReversesGlyphsOfDifferingAdvance_NoGapOrOverlap()
        {
            // Reflecting a run about its own content span (each glyph's own Advance and its own offset
            // from the run's start), not reusing the logical-order Px of whichever glyph used to occupy
            // a list position, is what keeps this correct once glyph advances differ within a run - here
            // by giving the two tspans very different font sizes. A regression shows up as a gap or
            // overlap between the two runs, not merely as the wrong left-to-right order.
            var g = Render(
                """<text x="10" y="60" direction="rtl" unicode-bidi="bidi-override">""" +
                """<tspan font-size="40">AA</tspan><tspan font-size="10">BB</tspan></text>""");

            Assert.Equal(2, g.DrawStringCalls.Count);
            var bb = Assert.Single(g.DrawStringCalls, c => c.Text == "BB");
            var aa = Assert.Single(g.DrawStringCalls, c => c.Text == "AA");

            // "BB" (logically last, small font) is visually first; "AA" (logically first, large font)
            // immediately follows it with no gap and no overlap.
            var bbAdvance = g.MeasureString("BB", bb.Font).Width;
            Assert.Equal(bb.Point.X + bbAdvance, aa.Point.X, 1);
        }
    }
}
