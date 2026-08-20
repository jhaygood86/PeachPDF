using PeachPDF.Adapters;
using PeachPDF.Fonts.OpenType;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.IO;
using System.Threading.Tasks;
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
        public async Task Upright_PaintsAtRealVmtxAdvanceStep_WhenFontHasVerticalMetrics()
        {
            // Registers the same bundled CJK font (real vhea/vmtx data - see
            // VerticalMetricsTablesTests.RealBundledCjkFont_HasVerticalMetrics_AdvanceComesFromARealVmtxEntry)
            // on the real Adapter that SvgTreeBuilder.Build resolves <text font-family> against, so
            // gi.Font here (unlike this file's other tests, which deliberately don't assert on exact
            // font metrics) is a real FontAdapter with real vertical metrics to consult - proving
            // LayoutGlyphs/PaintUprightGlyph's real-vmtx path (issue #770) actually drives SVG's own
            // pen advance, not just HTML's.
            await using var stream = File.OpenRead(BundledFonts.Cjk);
            await Adapter.AddFont(stream, "CjkVerticalMetricsTest");

            var g = Render($"""<text x="10" y="50" font-size="20" font-family="CjkVerticalMetricsTest" writing-mode="vertical-rl" text-orientation="upright">{Upright}{Upright}</text>""");

            Assert.Equal(2, g.DrawStringCalls.Count);
            var font = g.DrawStringCalls[0].Font;
            Assert.True(font.HasVerticalMetrics);

            var rune = System.Text.Rune.GetRuneAt(Upright, 0);
            var expectedStep = font.GetVerticalAdvance(rune);
            var actualStep = g.DrawStringCalls[1].Point.Y - g.DrawStringCalls[0].Point.Y;

            Assert.Equal(expectedStep, actualStep, precision: 6);
        }

        [Fact]
        public async Task Upright_ClipsEachGlyphToItsOwnCell_WhenFontHasVerticalMetrics()
        {
            // A real vmtx advance is routinely narrower than the font's own line height (see
            // FragmentPainter.Text.cs's PaintUprightVerticalRun remarks) - RGraphics.DrawString still
            // paints across that full line-height span regardless, so without confining each glyph's
            // paint to its own reserved cell, consecutive glyphs bleed into each other. Verifies the
            // push/pop clip actually brackets the draw call (order, not just count, per this repo's own
            // painting-test convention) and that a font without real vertical metrics needs no clip at
            // all (its advance already equals its own painted span).
            await using var stream = File.OpenRead(BundledFonts.Cjk);
            await Adapter.AddFont(stream, "CjkClipTest");

            var real = Render($"""<text x="10" y="50" font-size="20" font-family="CjkClipTest" writing-mode="vertical-rl" text-orientation="upright">{Upright}</text>""");
            var draw = Assert.Single(real.DrawStringCalls);
            Assert.True(draw.Font.HasVerticalMetrics);

            var drawIndex = real.Log.IndexOf(draw);
            Assert.IsType<TestRecordingGraphics.PushClipCall>(real.Log[drawIndex - 1]);
            Assert.IsType<TestRecordingGraphics.PopClipCall>(real.Log[drawIndex + 1]);

            // RenderInto itself always pushes one clip for the viewBox-to-viewport mapping (see this
            // file's own remarks on GlyphTransformPushes/Pops for the analogous baseline transform case),
            // so a font without real vertical metrics is verified by the draw call itself not being
            // freshly bracketed - not by the total clip count being zero.
            var fallback = Render($"""<text x="10" y="50" font-size="20" writing-mode="vertical-rl" text-orientation="upright">{Upright}</text>""");
            var fallbackDraw = Assert.Single(fallback.DrawStringCalls);
            Assert.False(fallbackDraw.Font.HasVerticalMetrics);

            var fallbackDrawIndex = fallback.Log.IndexOf(fallbackDraw);
            Assert.IsNotType<TestRecordingGraphics.PushClipCall>(fallback.Log[fallbackDrawIndex - 1]);
            Assert.IsNotType<TestRecordingGraphics.PopClipCall>(fallback.Log[fallbackDrawIndex + 1]);
        }

        [Fact]
        public async Task Upright_AppliesRealVorgOrigin_WhenFontHasRealVorgTable()
        {
            // BundledFonts.Otf (Source Code Pro) is confirmed CFF-flavored (no glyf), so a real VORG
            // table spliced onto it is actually trusted (issue #775) - BundledFonts.Cjk is TrueType and
            // would have its VORG ignored (see the companion test below), so this needs the CFF font
            // instead, which means Latin text (Source Code Pro has no CJK coverage) and its own
            // synthetic vhea/vmtx too (unlike Cjk, it has neither natively). defaultVertOriginY=700 is
            // deliberately different from this font's own Ascender (984, confirmed directly via
            // OpenTypeDescriptor - see TextOrientationIntegrationTests's identical HTML-side fixture)
            // so the resulting shift is real and provably non-zero. Compared against a plain (no-VORG)
            // rendering of the same text/position rather than an internal layout property, since
            // GlyphInfo.Py isn't exposed to this test the way CssRect.Top is to the HTML-side
            // equivalent.
            var plainBytes = File.ReadAllBytes(BundledFonts.Otf);
            var numGlyphs = XFontSource.GetOrCreateFrom(plainBytes).Fontface.maxp.numGlyphs;
            var vorgBytes = SyntheticFontTables.InsertTableDirectoryEntry(plainBytes, TableTagNames.VHea, SyntheticFontTables.BuildVhea(ascent: 900, descent: -200, numOfLongVerMetrics: numGlyphs));
            vorgBytes = SyntheticFontTables.InsertTableDirectoryEntry(vorgBytes, TableTagNames.VMtx, SyntheticFontTables.BuildVmtxUniform(1000, numGlyphs));
            vorgBytes = SyntheticFontTables.InsertTableDirectoryEntry(vorgBytes, TableTagNames.VOrg, SyntheticFontTables.BuildVorg(700));

            await using (var vorgStream = new MemoryStream(vorgBytes))
                await Adapter.AddFont(vorgStream, "OtfVorgTest");
            var g = Render($"""<text x="10" y="50" font-size="20" font-family="OtfVorgTest" writing-mode="vertical-rl" text-orientation="upright">{Latin}</text>""");
            var draw = g.DrawStringCalls[0];
            Assert.True(draw.Font.HasVerticalOrigin);

            var rune = System.Text.Rune.GetRuneAt(Latin, 0);
            var expectedShift = draw.Font.GetVerticalOriginY(rune) - draw.Font.Ascent;
            Assert.NotEqual(0, expectedShift, precision: 3); // the shift is actually nonzero for this fixture

            await using (var plainStream = new MemoryStream(plainBytes))
                await Adapter.AddFont(plainStream, "OtfPlainForVorgCompare");
            var plainG = Render($"""<text x="10" y="50" font-size="20" font-family="OtfPlainForVorgCompare" writing-mode="vertical-rl" text-orientation="upright">{Latin}</text>""");
            var plainDraw = plainG.DrawStringCalls[0];
            Assert.False(plainDraw.Font.HasVerticalOrigin);

            Assert.Equal(plainDraw.Point.Y + expectedShift, draw.Point.Y, precision: 6);
        }

        [Fact]
        public async Task Upright_IgnoresVorg_OnTrueTypeFont_RendersIdenticallyToNoVorg()
        {
            // The same synthetic VORG bytes on BundledFonts.Cjk - a real TrueType/glyf font - must be
            // ignored per the OpenType spec's CFF-only restriction (issue #775), rendering
            // byte-for-byte identically to the plain no-VORG case (issue #770's already-shipped
            // behavior), not merely "no crash."
            var plainBytes = File.ReadAllBytes(BundledFonts.Cjk);
            var vorgBytes = SyntheticFontTables.InsertTableDirectoryEntry(plainBytes, TableTagNames.VOrg, SyntheticFontTables.BuildVorg(700));

            await using (var vorgStream = new MemoryStream(vorgBytes))
                await Adapter.AddFont(vorgStream, "CjkVorgIgnoredTest");
            var withVorg = Render($"""<text x="10" y="50" font-size="20" font-family="CjkVorgIgnoredTest" writing-mode="vertical-rl" text-orientation="upright">{Upright}</text>""");
            var withVorgDraw = Assert.Single(withVorg.DrawStringCalls);
            Assert.False(withVorgDraw.Font.HasVerticalOrigin);

            await using (var plainStream = new MemoryStream(plainBytes))
                await Adapter.AddFont(plainStream, "CjkPlainForIgnoreCompare");
            var plain = Render($"""<text x="10" y="50" font-size="20" font-family="CjkPlainForIgnoreCompare" writing-mode="vertical-rl" text-orientation="upright">{Upright}</text>""");
            var plainDraw = Assert.Single(plain.DrawStringCalls);

            Assert.Equal(plainDraw.Point.Y, withVorgDraw.Point.Y, precision: 6);
            Assert.Equal(plainDraw.Point.X, withVorgDraw.Point.X, precision: 6);
        }

        [Fact]
        public async Task Upright_AppliesVorgOrigin_AndClipsGlyph_OnFontWithVorgButNoVerticalMetrics()
        {
            // Regression test for a bug the post-change review pass caught: LayoutGlyphs originally
            // computed OriginYOffset *inside* the HasVerticalMetrics branch, so a font with a real VORG
            // table but no vhea/vmtx (unlike the fixture above, which synthesizes both) silently got no
            // origin shift at all - HasVerticalOrigin and HasVerticalMetrics are independent capability
            // flags (see RFont's own remarks) and must be checked independently. BundledFonts.Otf (CFF)
            // has neither vhea nor vmtx natively, so appending only a synthetic VORG table (no vhea/vmtx)
            // exercises exactly that combination. This also covers PaintUprightGlyph's clip gate, which
            // the same review pass found needed extending from HasVerticalMetrics alone to
            // "HasVerticalMetrics || HasVerticalOrigin" - without it, this glyph would paint unclipped.
            var plainBytes = File.ReadAllBytes(BundledFonts.Otf);
            var vorgOnlyBytes = SyntheticFontTables.InsertTableDirectoryEntry(plainBytes, TableTagNames.VOrg, SyntheticFontTables.BuildVorg(700));

            await using (var vorgStream = new MemoryStream(vorgOnlyBytes))
                await Adapter.AddFont(vorgStream, "OtfVorgOnlyTest");
            var g = Render($"""<text x="10" y="50" font-size="20" font-family="OtfVorgOnlyTest" writing-mode="vertical-rl" text-orientation="upright">{Latin}</text>""");
            var draw = g.DrawStringCalls[0];
            Assert.True(draw.Font.HasVerticalOrigin);
            Assert.False(draw.Font.HasVerticalMetrics);

            var drawIndex = g.Log.IndexOf(draw);
            Assert.IsType<TestRecordingGraphics.PushClipCall>(g.Log[drawIndex - 1]);
            Assert.IsType<TestRecordingGraphics.PopClipCall>(g.Log[drawIndex + 1]);

            var rune = System.Text.Rune.GetRuneAt(Latin, 0);
            var expectedShift = draw.Font.GetVerticalOriginY(rune) - draw.Font.Ascent;
            Assert.NotEqual(0, expectedShift, precision: 3);

            await using (var plainStream = new MemoryStream(plainBytes))
                await Adapter.AddFont(plainStream, "OtfPlainForVorgOnlyCompare");
            var plainG = Render($"""<text x="10" y="50" font-size="20" font-family="OtfPlainForVorgOnlyCompare" writing-mode="vertical-rl" text-orientation="upright">{Latin}</text>""");
            var plainDraw = plainG.DrawStringCalls[0];
            Assert.False(plainDraw.Font.HasVerticalOrigin);

            Assert.Equal(plainDraw.Point.Y + expectedShift, draw.Point.Y, precision: 6);
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
