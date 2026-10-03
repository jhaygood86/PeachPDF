using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary><c>textLength</c> and <c>lengthAdjust</c> on SVG text: straight, <c>&lt;textPath&gt;</c> and vertical.</summary>
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

        /// <summary>Every transform pushed for a glyph (the first push of a render is the viewBox mapping).</summary>
        private static TestRecordingGraphics.PushTransformCall[] GlyphPushes(TestRecordingGraphics g) =>
            g.Log.OfType<TestRecordingGraphics.PushTransformCall>().Skip(1).ToArray();

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
        public void TextLength_SpacingAndGlyphs_StretchesGlyphsToExactlyThatSpan()
        {
            var plain = Render("", "abcd");
            var g = Render("""textLength="200" lengthAdjust="spacingAndGlyphs" """, "abcd");

            var scale = 200 / Span(plain);
            Assert.Equal(4, g.DrawStringCalls.Count);
            var pushes = GlyphPushes(g);
            Assert.Equal(4, pushes.Length);
            Assert.All(pushes, p => Assert.Equal(scale, p.Matrix.M11, 3));

            // Each glyph paints about its own pen position, at the stretched position.
            Assert.Equal(plain.DrawStringCalls[0].PaintPoint.X, g.DrawStringCalls[0].PaintPoint.X, 3);
            for (var i = 0; i < 3; i++)
                Assert.Equal(g.DrawStringCalls[i].PaintPoint.X + g.DrawStringCalls[i].Size.Width * scale, g.DrawStringCalls[i + 1].PaintPoint.X, 3);

            var last = g.DrawStringCalls[^1];
            Assert.Equal(200, last.PaintPoint.X + last.Size.Width * scale - g.DrawStringCalls[0].PaintPoint.X, 3);
        }

        [Fact]
        public void TextLength_SpacingAndGlyphs_SmallerThanNatural_CompressesGlyphs()
        {
            var plain = Render("", "abcd");
            var g = Render("""textLength="30" lengthAdjust="spacingAndGlyphs" """, "abcd");

            var scale = 30 / Span(plain);
            Assert.True(scale < 1);
            Assert.All(GlyphPushes(g), p => Assert.Equal(scale, p.Matrix.M11, 3));
        }

        [Fact]
        public void TextLength_SpacingAndGlyphs_SingleCharacterIsStretched()
        {
            var plain = Render("", "a");
            var g = Render("""textLength="100" lengthAdjust="spacingAndGlyphs" """, "a");

            var push = Assert.Single(GlyphPushes(g));
            Assert.Equal(100 / plain.DrawStringCalls[0].Size.Width, push.Matrix.M11, 3);
        }

        [Fact]
        public void TextLength_SpacingAndGlyphs_EqualToNatural_PaintsUntransformed()
        {
            var plain = Render("", "abcd");
            var same = Render($"textLength=\"{Span(plain).ToString(System.Globalization.CultureInfo.InvariantCulture)}\" lengthAdjust=\"spacingAndGlyphs\" ", "abcd");

            Assert.Empty(GlyphPushes(same));
        }

        [Fact]
        public void TextLength_SpacingAndGlyphs_OnTspan_StretchesOnlyThatSpanAndShiftsTheRest()
        {
            var plain = Render("", "a<tspan>bcd</tspan>e");
            var g = Render("", "a<tspan textLength=\"100\" lengthAdjust=\"spacingAndGlyphs\">bcd</tspan>e");

            var natural = plain.DrawStringCalls[1].Size.Width;
            Assert.Equal(["a", "b", "c", "d", "e"], g.DrawStringCalls.Select(c => c.Text).ToArray());
            var pushes = GlyphPushes(g);
            Assert.Equal(3, pushes.Length);
            Assert.All(pushes, p => Assert.Equal(100 / natural, p.Matrix.M11, 3));
            Assert.Equal(plain.DrawStringCalls[0].PaintPoint.X, g.DrawStringCalls[0].PaintPoint.X, 3);
            Assert.Equal(plain.DrawStringCalls[2].PaintPoint.X + (100 - natural), g.DrawStringCalls[4].PaintPoint.X, 3);
            var d = g.DrawStringCalls[3];
            Assert.Equal(100, d.PaintPoint.X + d.Size.Width * pushes[2].Matrix.M11 - g.DrawStringCalls[1].PaintPoint.X, 3);
        }

        [Fact]
        public void TextLength_SpacingAndGlyphs_NestedInnerThenOuter_ComposesTheScales()
        {
            var g = Render("""textLength="200" lengthAdjust="spacingAndGlyphs" """, "ab<tspan textLength=\"50\" lengthAdjust=\"spacingAndGlyphs\">cd</tspan>");

            var pushes = GlyphPushes(g);
            Assert.Equal(4, pushes.Length);
            // The outer element spans exactly its textLength, whatever the inner one did first.
            var last = g.DrawStringCalls[^1];
            Assert.Equal(200, last.PaintPoint.X + last.Size.Width * pushes[3].Matrix.M11 - g.DrawStringCalls[0].PaintPoint.X, 3);
            // The inner glyphs carry the product of both factors, the outer ones only their own.
            Assert.NotEqual(pushes[0].Matrix.M11, pushes[2].Matrix.M11, 3);
        }

        [Fact]
        public void TextLength_SpacingAndGlyphs_AnchorUsesTheStretchedExtent()
        {
            var g = Render("""textLength="200" lengthAdjust="spacingAndGlyphs" text-anchor="middle" """, "abcd");

            var scale = GlyphPushes(g)[0].Matrix.M11;
            var last = g.DrawStringCalls[^1];
            var left = g.DrawStringCalls[0].PaintPoint.X;
            Assert.Equal(10, left + (last.PaintPoint.X + last.Size.Width * scale - left) / 2, 3);
        }

        [Fact]
        public void TextLength_SpacingAndGlyphs_DecorationSpansTheStretchedText()
        {
            var g = Render("""textLength="200" lengthAdjust="spacingAndGlyphs" text-decoration="underline" text-decoration-skip-ink="none" """, "abcd");

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.Equal(g.DrawStringCalls[0].PaintPoint.X, line.X1, 3);
            Assert.Equal(g.DrawStringCalls[0].PaintPoint.X + 200, line.X2, 3);
        }

        [Fact]
        public void TextLength_SpacingAndGlyphs_ShadowIsStretchedToo()
        {
            var g = Render("""textLength="200" lengthAdjust="spacingAndGlyphs" style="text-shadow: 3px 4px red" """, "abcd");

            // Per glyph: the shadow's push (stretch, then the offset in user space) and then the glyph's own.
            var pushes = GlyphPushes(g);
            Assert.Equal(8, pushes.Length);
            for (var i = 0; i < 8; i += 2)
            {
                Assert.Equal(pushes[i + 1].Matrix.M31 + 3, pushes[i].Matrix.M31, 3);
                Assert.Equal(pushes[i + 1].Matrix.M32 + 4, pushes[i].Matrix.M32, 3);
                Assert.Equal(pushes[i + 1].Matrix.M11, pushes[i].Matrix.M11, 3);
            }
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
            var plain = Render("", "بيت");
            var g = Render("""textLength="200" """, "بيت");

            Assert.Equal(plain.DrawStringCalls.Count, g.DrawStringCalls.Count);
            Assert.Equal(plain.DrawStringCalls.Select(c => c.Text).ToArray(), g.DrawStringCalls.Select(c => c.Text).ToArray());
        }

        [Fact]
        public void TextLength_SpacingAndGlyphs_KeepsAComplexScriptRunTogether()
        {
            var plain = Render("", "بيت");
            var g = Render("""textLength="200" lengthAdjust="spacingAndGlyphs" """, "بيت");

            // One shaped run, stretched as a whole.
            Assert.Equal(plain.DrawStringCalls.Select(c => c.Text).ToArray(), g.DrawStringCalls.Select(c => c.Text).ToArray());
            var push = Assert.Single(GlyphPushes(g));
            Assert.Equal(200 / Span(plain), push.Matrix.M11, 3);
        }

        private static TestRecordingGraphics RenderPath(string textPathAttrs, string content, string pathData = "M10 60 L310 60")
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 100"><defs><path id="p" d="{pathData}"/></defs><text font-size="20"><textPath href="#p" {textPathAttrs}>{content}</textPath></text></svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 400, 100));
            return g;
        }

        /// <summary>Left edge of the first glyph to the right edge of the last along a straight textPath, from each glyph's frame (its centre) and its drawn half-advance.</summary>
        private static double PathSpan(TestRecordingGraphics g, double scale = 1)
        {
            var pushes = GlyphPushes(g);
            var first = pushes[0].Matrix.M31 + g.DrawStringCalls[0].PaintPoint.X * scale;
            var last = pushes[^1].Matrix.M31 - g.DrawStringCalls[^1].PaintPoint.X * scale;
            return last - first;
        }

        [Fact]
        public void TextLength_OnTextPath_Spacing_SpreadsGlyphsAlongThePath()
        {
            var plain = RenderPath("", "abcd");
            var g = RenderPath("""textLength="200" """, "abcd");

            Assert.True(PathSpan(plain) < 200);
            Assert.Equal(200, PathSpan(g), 3);
            Assert.All(GlyphPushes(g), p => Assert.Equal(1, p.Matrix.M11, 3));
            // Glyphs keep their natural size, and the text still starts where it did.
            Assert.Equal(plain.DrawStringCalls.Select(c => c.PaintPoint.X), g.DrawStringCalls.Select(c => c.PaintPoint.X));
            Assert.Equal(GlyphPushes(plain)[0].Matrix.M31, GlyphPushes(g)[0].Matrix.M31, 3);
        }

        [Fact]
        public void TextLength_OnTextPath_Spacing_SqueezesWhenShorter()
        {
            var g = RenderPath("""textLength="30" """, "abcd");

            Assert.Equal(30, PathSpan(g), 3);
        }

        [Fact]
        public void TextLength_OnTextPath_SpacingAndGlyphs_ScalesGlyphsAlongThePath()
        {
            var plain = RenderPath("", "abcd");
            var g = RenderPath("""textLength="200" lengthAdjust="spacingAndGlyphs" """, "abcd");

            var scale = 200 / PathSpan(plain);
            var pushes = GlyphPushes(g);
            Assert.Equal(4, pushes.Length);
            Assert.All(pushes, p => Assert.Equal(scale, p.Matrix.M11, 3));
            Assert.Equal(200, PathSpan(g, scale), 3);
            // The first glyph's left edge stays on the start of the path.
            Assert.Equal(10, pushes[0].Matrix.M31 + g.DrawStringCalls[0].PaintPoint.X * scale, 3);
        }

        [Fact]
        public void TextLength_OnTextPath_Anchor_UsesTheAdjustedLength()
        {
            var g = RenderPath("""textLength="200" startOffset="150" text-anchor="middle" """, "abcd");

            var pushes = GlyphPushes(g);
            var left = pushes[0].Matrix.M31 + g.DrawStringCalls[0].PaintPoint.X;
            var right = pushes[^1].Matrix.M31 - g.DrawStringCalls[^1].PaintPoint.X;
            Assert.Equal(200, right - left, 3);
            Assert.Equal(10 + 150, (left + right) / 2, 3);
        }

        private static TestRecordingGraphics RenderVertical(string textAttrs, string content)
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 300"><text x="100" y="20" font-size="20" writing-mode="vertical-rl" {textAttrs}>{content}</text></svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 200, 300));
            return g;
        }

        // A sideways (rotated) glyph's pen is its Py (the baseline origin): the box top is Py - ascent.
        private static double Py(TestRecordingGraphics.DrawStringCall c) => c.PaintPoint.Y + c.Font.Ascent;

        [Fact]
        public void TextLength_VerticalWritingMode_Spacing_SpreadsDownTheColumn()
        {
            var plain = RenderVertical("text-orientation=\"sideways\" ", "ABCD");
            var g = RenderVertical("""text-orientation="sideways" textLength="120" """, "ABCD");

            var natural = Py(plain.DrawStringCalls[3]) + plain.DrawStringCalls[3].Size.Width - Py(plain.DrawStringCalls[0]);
            Assert.True(natural < 120);
            Assert.Equal(120, Py(g.DrawStringCalls[3]) + g.DrawStringCalls[3].Size.Width - Py(g.DrawStringCalls[0]), 3);
            Assert.Equal(Py(plain.DrawStringCalls[0]), Py(g.DrawStringCalls[0]), 3);
            Assert.Equal(plain.DrawStringCalls[0].PaintPoint.X, g.DrawStringCalls[0].PaintPoint.X, 3);
        }

        [Fact]
        public void TextLength_VerticalWritingMode_SpacingAndGlyphs_ScalesRotatedGlyphsAlongTheColumn()
        {
            var plain = RenderVertical("text-orientation=\"sideways\" ", "ABCD");
            var g = RenderVertical("""text-orientation="sideways" textLength="120" lengthAdjust="spacingAndGlyphs" """, "ABCD");

            var natural = Py(plain.DrawStringCalls[3]) + plain.DrawStringCalls[3].Size.Width - Py(plain.DrawStringCalls[0]);
            var scale = 120 / natural;
            var pushes = GlyphPushes(g);
            Assert.Equal(4, pushes.Length);
            // A 90 degree rotation of a glyph stretched along its own x: the matrix's y column carries the scale.
            Assert.All(pushes, p => Assert.Equal(scale, p.Matrix.M12, 3));
            Assert.Equal(120, Py(g.DrawStringCalls[3]) + g.DrawStringCalls[3].Size.Width * scale - Py(g.DrawStringCalls[0]), 3);
        }

        [Fact]
        public void TextLength_VerticalWritingMode_SpacingAndGlyphs_ScalesUprightGlyphsDownTheColumn()
        {
            const string Kana = "テテテテ";
            var plain = RenderVertical("text-orientation=\"upright\" ", Kana);
            var g = RenderVertical("""text-orientation="upright" textLength="200" lengthAdjust="spacingAndGlyphs" """, Kana);

            var advance = (plain.DrawStringCalls[3].PaintPoint.Y - plain.DrawStringCalls[0].PaintPoint.Y) / 3;
            var scale = 200 / (4 * advance);
            var pushes = GlyphPushes(g);
            Assert.Equal(4, pushes.Length);
            Assert.All(pushes, p => Assert.Equal(scale, p.Matrix.M22, 3));
            Assert.All(pushes, p => Assert.Equal(1, p.Matrix.M11, 3));
            var y0 = plain.DrawStringCalls[0].PaintPoint.Y;
            for (var i = 0; i < 4; i++)
                Assert.Equal(y0 + (plain.DrawStringCalls[i].PaintPoint.Y - y0) * scale, g.DrawStringCalls[i].PaintPoint.Y, 3);
        }
    }
}
