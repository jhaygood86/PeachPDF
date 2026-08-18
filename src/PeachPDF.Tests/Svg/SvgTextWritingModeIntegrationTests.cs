using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Real vertical <c>writing-mode</c> (<c>vertical-rl</c>/<c>vertical-lr</c>) and per-character
    /// <c>text-orientation</c> for SVG <c>&lt;text&gt;</c> - the SVG-pipeline counterpart of
    /// <c>TextOrientationIntegrationTests</c> (HTML). Renders through <see cref="SvgRenderer.RenderInto"/>
    /// into a <see cref="TestRecordingGraphics"/> and asserts the actual painted <c>DrawString</c>
    /// sequence and rotation transforms - not just that the properties parse. Uses the real
    /// <c>PdfSharpAdapter</c> (matching <c>SvgTextBidiTests</c>'s own established pattern), so - unlike
    /// the HTML pipeline's <c>TextOrientationIntegrationTests</c>, which can use a deterministic
    /// <c>TestFont</c> - a resolved glyph's exact width/height/ascent are whatever font this environment
    /// actually resolves and are deliberately not asserted on; a rotated glyph's own <c>Px</c> (the pen's
    /// cross-axis coordinate, derived purely from <c>x</c>/<c>dx</c> attributes, never from font metrics)
    /// is the one geometry value cheap enough to assert exactly.
    /// </summary>
    public class SvgTextWritingModeIntegrationTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        // U+30C6 "テ" (katakana TE, Vertical_Orientation=U) + "AB" (both R) - the same choice
        // TextOrientationIntegrationTests (HTML) uses and for the same reason: a CJK Unified Ideograph
        // would be pre-split by an unrelated HTML-only mechanism that doesn't exist in this SVG
        // pipeline, but using the identical text keeps the two test suites easy to compare.
        private const string Upright = "テ";
        private const string Latin = "AB";

        // RenderInto itself pushes/pops one transform for the viewBox-to-viewport mapping, independent
        // of any glyph rotation - every PushTransformCount/PopTransformCount assertion below is against
        // g.GlyphTransformPushes/GlyphTransformPops (that baseline already subtracted), not the raw
        // TestRecordingGraphics counters.
        private static TestRecordingGraphics Render(string body)
        {
            var markup = $$"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                  {{body}}
                </svg>
                """;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new RRect(0, 0, 200, 200));
            return g;
        }

        private static int GlyphTransformPushes(TestRecordingGraphics g) => g.PushTransformCount - 1;
        private static int GlyphTransformPops(TestRecordingGraphics g) => g.PopTransformCount - 1;

        [Fact]
        public void Mixed_PaintsUprightGlyphUnrotated_RotatedGlyphsWithPushPop()
        {
            var g = Render($"""<text x="10" y="50" font-size="20" writing-mode="vertical-rl" text-orientation="mixed">{Upright}{Latin}</text>""");

            // Never batched under a vertical writing mode - one DrawString call per glyph.
            Assert.Equal(3, g.DrawStringCalls.Count);
            Assert.Equal(Upright, g.DrawStringCalls[0].Text);
            Assert.Equal("A", g.DrawStringCalls[1].Text);
            Assert.Equal("B", g.DrawStringCalls[2].Text);

            // Only the two rotated (Latin) glyphs push/pop a rotation transform - the upright glyph paints
            // with none.
            Assert.Equal(2, GlyphTransformPushes(g));
            Assert.Equal(2, GlyphTransformPops(g));

            // A and B (both rotated) share the same cross-axis position: no dx separates them, and a
            // rotated glyph paints at exactly Px (PaintRotatedGlyph), unlike an upright glyph's
            // font-metric-dependent centering - so this holds regardless of which font resolves.
            Assert.Equal(10, g.DrawStringCalls[1].Point.X);
            Assert.Equal(10, g.DrawStringCalls[2].Point.X);

            // Per this repo's own painting-test convention, order matters, not just counts: each
            // rotated glyph's own push must immediately precede its draw and its own pop must
            // immediately follow it - never all pushes up front then all draws then all pops, which
            // would rotate every glyph by the LAST pushed matrix instead of its own.
            var drawA = g.Log.IndexOf(g.DrawStringCalls[1]);
            var drawB = g.Log.IndexOf(g.DrawStringCalls[2]);
            Assert.IsType<TestRecordingGraphics.PushTransformCall>(g.Log[drawA - 1]);
            Assert.IsType<TestRecordingGraphics.PopTransformCall>(g.Log[drawA + 1]);
            Assert.IsType<TestRecordingGraphics.PushTransformCall>(g.Log[drawB - 1]);
            Assert.IsType<TestRecordingGraphics.PopTransformCall>(g.Log[drawB + 1]);
        }

        [Fact]
        public void Mixed_MultipleBoundaries_AllGlyphsClassifiedIndependently()
        {
            // R | U | R - three orientation classifications in one run, not just a single boundary.
            var g = Render($"""<text x="10" y="50" font-size="20" writing-mode="vertical-rl">{Latin[..1]}{Upright}{Latin[1..]}</text>""");

            Assert.Equal(3, g.DrawStringCalls.Count);
            Assert.Equal(2, GlyphTransformPushes(g)); // the two rotated Latin glyphs
        }

        [Fact]
        public void Upright_ForcesEveryGlyphUpright_NoRotationAtAll()
        {
            var g = Render($"""<text x="10" y="50" font-size="20" writing-mode="vertical-rl" text-orientation="upright">{Latin}</text>""");

            Assert.Equal(2, g.DrawStringCalls.Count);
            Assert.Equal(0, GlyphTransformPushes(g));
        }

        [Fact]
        public void Sideways_ForcesEveryGlyphRotated_EvenUprightClassifiedOnes()
        {
            var g = Render($"""<text x="10" y="50" font-size="20" writing-mode="vertical-rl" text-orientation="sideways">{Upright}</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(Upright, draw.Text);
            Assert.Equal(1, GlyphTransformPushes(g));
            Assert.Equal(1, GlyphTransformPops(g));
        }

        [Fact]
        public void ExplicitRotateAttribute_OverridesAutomaticOrientation()
        {
            // An upright-classified glyph with an explicit rotate="45" is rotated by the explicit angle,
            // not painted upright - explicit rotate="" always wins over the orientation-driven default.
            var g = Render($"""<text x="10" y="50" font-size="20" writing-mode="vertical-rl" rotate="45">{Upright}</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(Upright, draw.Text);
            Assert.Equal(1, GlyphTransformPushes(g));
        }

        [Fact]
        public void HorizontalTb_NeverAppliesOrientationOrPushesTransform_Regression()
        {
            var g = Render($"""<text x="10" y="50" font-size="20" writing-mode="horizontal-tb">{Upright}{Latin}</text>""");

            // Default writing-mode: horizontal-tb - batches into one call exactly as before this feature.
            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(Upright + Latin, draw.Text);
            Assert.Equal(0, GlyphTransformPushes(g));
        }

        [Fact]
        public void VerticalLr_SameOrientationBehaviorAsVerticalRl()
        {
            // text-orientation classification and the pen's inline axis are independent of vertical-rl
            // vs vertical-lr (that distinction is about which physical edge the block axis starts from -
            // out of scope for SVG text, which has no block-axis line-wrapping to place columns along;
            // see the accepted-gap note).
            var g = Render($"""<text x="10" y="50" font-size="20" writing-mode="vertical-lr">{Upright}{Latin}</text>""");

            Assert.Equal(3, g.DrawStringCalls.Count);
            Assert.Equal(2, GlyphTransformPushes(g));
        }

        [Fact]
        public void TSpan_TextOrientationOverride_AppliesOnlyToItsOwnGlyphs()
        {
            // A nested <tspan> can override text-orientation independently of its ancestor <text> - unlike
            // writing-mode, which is resolved once from the <text> root (see LayoutGlyphs's own remarks).
            var g = Render($"""
                <text x="10" y="50" font-size="20" writing-mode="vertical-rl">{Upright}<tspan text-orientation="upright">{Latin}</tspan></text>
                """);

            Assert.Equal(3, g.DrawStringCalls.Count);
            // テ is upright by its own mixed-mode classification; A/B are upright only because the
            // <tspan> forces it - neither pushes a rotation transform.
            Assert.Equal(0, GlyphTransformPushes(g));
        }

        [Fact]
        public void TSpan_OwnYAttribute_StartsANewTextChunkAlongTheColumn()
        {
            // A nested <tspan> with its own y (the inline/chunk-advance axis under a vertical writing
            // mode - see LayoutGlyphs's own remarks) starts a fresh text chunk, the vertical counterpart
            // of an own-x <tspan> starting a new line under horizontal-tb.
            var g = Render($"""
                <text x="10" y="50" font-size="20" writing-mode="vertical-rl" text-orientation="upright">{Upright}<tspan y="150">{Upright}</tspan></text>
                """);

            Assert.Equal(2, g.DrawStringCalls.Count);
            // Both chunks share the same start (text-anchor:start, the default) so the second chunk's
            // own explicit y=150 - not an accumulated advance from the first - determines its position.
            Assert.NotEqual(g.DrawStringCalls[0].Point.Y, g.DrawStringCalls[1].Point.Y);
        }

        [Fact]
        public void TextAnchorEnd_ShiftsTheColumnBackAlongTheInlineAxis()
        {
            // text-anchor under a vertical writing mode shifts the chunk back along Y (the inline axis),
            // the same role it plays along X under horizontal-tb.
            var startAnchored = Render($"""<text x="10" y="50" font-size="20" writing-mode="vertical-rl" text-orientation="upright">{Upright}{Upright}</text>""");
            var endAnchored = Render($"""<text x="10" y="50" font-size="20" writing-mode="vertical-rl" text-orientation="upright" text-anchor="end">{Upright}{Upright}</text>""");

            // text-anchor:end shifts the whole chunk back by its own extent, so an end-anchored chunk's
            // first glyph paints at a different position than a start-anchored chunk's first glyph - the
            // exact sign isn't asserted here since it depends on this environment's resolved font metrics
            // (see this file's own remarks on why exact positions otherwise aren't asserted), only that
            // the shift actually applies.
            Assert.NotEqual(startAnchored.DrawStringCalls[0].Point.Y, endAnchored.DrawStringCalls[0].Point.Y);
        }
    }
}
