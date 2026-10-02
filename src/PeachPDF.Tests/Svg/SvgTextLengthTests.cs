using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary><c>textLength</c> with <c>lengthAdjust="spacing"</c> on SVG text.</summary>
    public class SvgTextLengthTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static TestRecordingGraphics Render(string textAttrs, string content, string svgAttrs = "")
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 100" {svgAttrs}><text x="10" y="50" font-size="20" {textAttrs}>{content}</text></svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 400, 100));
            return g;
        }

        private static double[] Xs(TestRecordingGraphics g) => g.DrawStringCalls.Select(c => c.PaintPoint.X).ToArray();

        /// <summary>Left edge of the first glyph to the right edge of the last.</summary>
        private static double Span(TestRecordingGraphics g)
        {
            var last = g.DrawStringCalls[^1];
            return last.PaintPoint.X + last.Size.Width - g.DrawStringCalls[0].PaintPoint.X;
        }

        [Fact]
        public void TextLength_StretchesTheRunToExactlyThatSpan()
        {
            var g = Render("""textLength="200" """, "abcd");

            Assert.Equal(4, g.DrawStringCalls.Count);
            Assert.Equal(200, Span(g), 3);
        }

        [Fact]
        public void TextLength_SmallerThanNatural_SqueezesTheSpacing()
        {
            var natural = Render("", "abcd");
            var squeezed = Render("""textLength="30" """, "abcd");

            Assert.True(Span(natural) > 30);
            Assert.Equal(30, Span(squeezed), 3);
        }

        [Fact]
        public void TextLength_GapsAreEven()
        {
            var g = Render("""textLength="200" """, "abcd");

            var xs = Xs(g);
            var gaps = new[] { xs[1] - xs[0], xs[2] - xs[1], xs[3] - xs[2] };
            var widths = g.DrawStringCalls.Take(3).Select(c => c.Size.Width).ToArray();
            var extra = gaps.Select((gap, i) => gap - widths[i]).ToArray();
            Assert.Equal(extra[0], extra[1], 3);
            Assert.Equal(extra[1], extra[2], 3);
        }

        [Fact]
        public void TextLength_EqualToNatural_ChangesNothing()
        {
            var plain = Render("", "abcd");
            var span = Span(plain);
            var same = Render($"textLength=\"{span.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" ", "abcd");

            Assert.Equal(span, Span(same), 3);
        }

        [Fact]
        public void TextLength_SingleCharacter_IsInert()
        {
            Assert.Single(Render("""textLength="100" """, "a").DrawStringCalls);
        }

        [Fact]
        public void TextLength_OnTspan_ShiftsTheFollowingText()
        {
            var plain = Render("", "a<tspan>bcd</tspan>e");
            var g = Render("", "a<tspan textLength=\"100\">bcd</tspan>e");

            Assert.Equal(["a", "bcd", "e"], plain.DrawStringCalls.Select(c => c.Text).ToArray());
            var natural = plain.DrawStringCalls[1].Size.Width;

            Assert.Equal(["a", "b", "c", "d", "e"], g.DrawStringCalls.Select(c => c.Text).ToArray());
            var tspanSpan = g.DrawStringCalls[3].PaintPoint.X + g.DrawStringCalls[3].Size.Width - g.DrawStringCalls[1].PaintPoint.X;
            Assert.Equal(100, tspanSpan, 3);
            Assert.Equal(plain.DrawStringCalls[2].PaintPoint.X + (100 - natural), g.DrawStringCalls[4].PaintPoint.X, 3);
            Assert.Equal(plain.DrawStringCalls[0].PaintPoint.X, g.DrawStringCalls[0].PaintPoint.X, 3);
        }

        [Fact]
        public void TextLength_SpacingAndGlyphs_IsNotApplied()
        {
            var plain = Render("", "abcd");
            var g = Render("""textLength="200" lengthAdjust="spacingAndGlyphs" """, "abcd");

            Assert.Equal(Span(plain), Span(g), 3);
        }

        [Fact]
        public void TextLength_MovesTheAnchoredExtent()
        {
            var g = Render("""textLength="200" text-anchor="middle" """, "abcd");

            // Anchored on the final extent: centred on x=10.
            Assert.Equal(10, g.DrawStringCalls[0].PaintPoint.X + Span(g) / 2, 3);
        }

        [Fact]
        public void TextLength_KeepsAComplexScriptRunTogether()
        {
            // An Arabic word is one shaping unit: spreading the run must not paint its first letter alone.
            var plain = Render("", "\u0628\u064A\u062A");
            var g = Render("""textLength="200" """, "\u0628\u064A\u062A");

            Assert.Equal(plain.DrawStringCalls.Count, g.DrawStringCalls.Count);
            Assert.Equal(plain.DrawStringCalls.Select(c => c.Text).ToArray(), g.DrawStringCalls.Select(c => c.Text).ToArray());
        }
    }
}
