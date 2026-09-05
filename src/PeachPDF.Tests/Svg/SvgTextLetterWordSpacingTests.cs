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
    /// Coverage for SVG <c>&lt;text&gt;</c>'s <c>letter-spacing</c>/<c>word-spacing</c> support (issue
    /// #533) - both were previously read nowhere in <see cref="SvgTreeBuilder"/>, so every
    /// <see cref="RGraphics.DrawString"/>/<c>MeasureString</c> call always passed
    /// <c>letterSpacing: 0</c>. Renders through <see cref="SvgRenderer.RenderInto"/> into a
    /// <see cref="TestRecordingGraphics"/> and asserts the resolved value actually reaches
    /// <c>DrawString</c>.
    /// </summary>
    public class SvgTextLetterWordSpacingTests
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
        public void LetterSpacing_PxValue_ReachesDrawString()
        {
            var g = Render("""<text x="10" y="50" font-size="40" letter-spacing="2px">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(2.0, draw.LetterSpacing);
        }

        [Fact]
        public void LetterSpacing_EmValue_ResolvesAgainstOwnFontSize()
        {
            var g = Render("""<text x="10" y="50" font-size="20" letter-spacing="0.5em">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(10.0, draw.LetterSpacing); // 0.5 * 20
        }

        [Fact]
        public void LetterSpacing_Normal_IsZero()
        {
            var g = Render("""<text x="10" y="50" font-size="40" letter-spacing="normal">Hi</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(0.0, draw.LetterSpacing);
        }

        [Fact]
        public void LetterSpacing_InheritsFromAncestor_ToTspan()
        {
            var g = Render("""<text x="10" y="50" font-size="40" letter-spacing="3"><tspan>Hi</tspan></text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(3.0, draw.LetterSpacing);
        }

        [Fact]
        public void LetterSpacing_TspanOverridesAncestor()
        {
            var g = Render("""<text x="10" y="50" font-size="40" letter-spacing="3">A<tspan letter-spacing="7">B</tspan></text>""");

            Assert.Equal(2, g.DrawStringCalls.Count);
            Assert.Equal(3.0, g.DrawStringCalls[0].LetterSpacing);
            Assert.Equal(7.0, g.DrawStringCalls[1].LetterSpacing);
        }

        [Fact]
        public void WordSpacing_ForcesBatchBreakAfterSpaceGlyph_AndOffsetsTheFollowingBatch()
        {
            var g = Render("""<text x="10" y="50" font-size="40" word-spacing="15">A B</text>""");

            // "A B" collapses to no whitespace change (already single spaces) - word-spacing forces a
            // batch break right after the space glyph, so "A " and "B" paint as two DrawString calls
            // instead of one "A B" call, with the second one's Px reflecting the extra 15 units.
            Assert.Equal(2, g.DrawStringCalls.Count);
            var first = g.DrawStringCalls[0];
            var second = g.DrawStringCalls[1];
            Assert.Equal("A ", first.Text);
            Assert.Equal("B", second.Text);

            // second.Point.X = 10 (text x) + measured("A ") + wordSpacing(15).
            Assert.True(second.Point.X > first.Point.X + first.Size.Width + 10);
        }

        [Fact]
        public void WordSpacing_RunBoundaryLandsOnTheSpaceGlyph_GapStillRenders()
        {
            // A run boundary (here: entering the tspan) forces PaintGlyphs to start a fresh batch
            // exactly at the space glyph, so `start` itself - not a glyph appended after it - is the
            // word-spaced whitespace character. If the batch-break check only looked at glyphs appended
            // inside the loop (the bug this pins), 'C'/'D' would get appended into the same DrawString
            // call as the leading space with no break, silently losing the word-spacing gap even though
            // layout had already accounted for it in the pen advance.
            var g = Render("""<text x="10" y="50" font-size="40">AB<tspan word-spacing="10"> CD</tspan></text>""");

            Assert.Equal(3, g.DrawStringCalls.Count);
            var ab = g.DrawStringCalls[0];
            var space = g.DrawStringCalls[1];
            var cd = g.DrawStringCalls[2];
            Assert.Equal("AB", ab.Text);
            Assert.Equal(" ", space.Text);
            Assert.Equal("CD", cd.Text);

            // "CD"'s X must reflect the space's own width plus the extra word-spacing gap after it -
            // not just abut the space as it would if the gap were silently dropped.
            Assert.True(cd.Point.X > space.Point.X + space.Size.Width + 5);
        }

        [Fact]
        public void WordSpacing_Zero_KeepsSingleBatch_NoBehaviorChange()
        {
            var g = Render("""<text x="10" y="50" font-size="40">A B</text>""");

            // Default (no word-spacing set): batching is completely unaffected, matching pre-existing
            // behavior exactly.
            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("A B", draw.Text);
        }
    }
}
