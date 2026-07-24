using System.IO;
using System.Text;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Parser coverage for the COLR (v0 + v1) and CPAL tables, using the hand-authored
    /// ColorTestV0/ColorTestV1 fixture fonts.
    /// </summary>
    public class ColrCpalTableTests
    {
        private static OpenTypeFontface Face(string path)
            => XFontSource.GetOrCreateFrom(File.ReadAllBytes(path)).Fontface;

        private static int Gid(OpenTypeFontface face, char ch)
        {
            var descriptor = new OpenTypeDescriptor("colr-test", "colr-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
            return descriptor.CharCodeToGlyphIndex(new Rune(ch));
        }

        [Fact]
        public void Cpal_ResolvesPaletteEntries_WithCorrectRgbaByteOrder()
        {
            CpalTable cpal = Face(BundledFonts.ColorV0).cpal;
            Assert.NotNull(cpal);
            Assert.Equal(4, cpal.EntriesPerPalette);
            Assert.Equal(1, cpal.PaletteCount);

            // Palette was authored red, green(0,128,0), blue, yellow - stored on disk as BGRA and
            // swizzled back to RGBA, so this proves the byte order survives.
            AssertColor(cpal, 0, 255, 0, 0, 255);
            AssertColor(cpal, 1, 0, 128, 0, 255);
            AssertColor(cpal, 2, 0, 0, 255, 255);
            AssertColor(cpal, 3, 255, 255, 0, 255);

            // Out-of-range entry declines; out-of-range palette clamps to palette 0.
            Assert.False(cpal.TryGetColor(0, 4, out _));
            Assert.True(cpal.TryGetColor(5, 0, out var clamped));
            Assert.Equal((byte)255, clamped.R);
        }

        [Fact]
        public void ColrV0_ExposesOrderedLayersPerBaseGlyph()
        {
            OpenTypeFontface face = Face(BundledFonts.ColorV0);
            ColrTable colr = face.colr;
            Assert.NotNull(colr);
            Assert.Equal(0, colr.Version);

            int box = Gid(face, 'X');
            int tri = Gid(face, 'Y');
            int circ = Gid(face, 'Z');

            Assert.True(colr.TryGetV0Layers(Gid(face, 'A'), out var aLayers));
            Assert.Equal(new[] { (box, 0), (tri, 1) }, aLayers.ToArray());

            Assert.True(colr.TryGetV0Layers(Gid(face, 'B'), out var bLayers));
            Assert.Equal(new[] { (circ, 2) }, bLayers.ToArray());

            // A layer/outline glyph is not itself a color base glyph.
            Assert.True(colr.HasColorGlyph(Gid(face, 'A')));
            Assert.False(colr.HasColorGlyph(box));
            Assert.False(colr.TryGetV0Layers(box, out _));
        }

        [Fact]
        public void Cpal_V1_ReadsMultiplePalettesAndLightDarkFlags()
        {
            // The Nabla subset carries 7 CPAL palettes (v1), with palette 1 flagged dark-background and
            // palette 2 light-background (see BundledFonts.Nabla / generate_nabla_subset.py).
            CpalTable cpal = Face(BundledFonts.Nabla).cpal;
            Assert.NotNull(cpal);
            Assert.Equal(7, cpal.PaletteCount);
            Assert.Equal(8, cpal.EntriesPerPalette);

            Assert.Equal(2, cpal.FirstLightPalette());
            Assert.Equal(1, cpal.FirstDarkPalette());

            // Palette selection resolves distinct entry-0 colors per palette (palette 0 warm yellow,
            // palette 2 blue) - proving the paletteIndex argument is honored, not ignored.
            Assert.True(cpal.TryGetColor(0, 0, out var p0));
            Assert.Equal((byte)255, p0.R);
            Assert.True(cpal.TryGetColor(2, 0, out var p2));
            Assert.Equal((0, 160, 225), (p2.R, p2.G, p2.B));
            Assert.NotEqual((p0.R, p0.G, p0.B), (p2.R, p2.G, p2.B));
        }

        [Fact]
        public void ColrV1_ParsesPaintColrLayersOfGlyphClippedSolids()
        {
            OpenTypeFontface face = Face(BundledFonts.ColorV1);
            ColrTable colr = face.colr;
            Assert.NotNull(colr);
            Assert.Equal(1, colr.Version);

            var root = Assert.IsType<ColrPaintColrLayers>(colr.GetV1BaseGlyphPaint(Gid(face, 'A')));
            Assert.Equal(2, root.NumLayers);

            // Bottom layer: box glyph filled solid with palette 0.
            var layer0 = Assert.IsType<ColrPaintGlyph>(colr.GetLayerPaint(root.FirstLayerIndex));
            Assert.Equal(Gid(face, 'X'), layer0.GlyphId);
            var solid0 = Assert.IsType<ColrPaintSolid>(layer0.Paint);
            Assert.Equal(0, solid0.PaletteIndex);
            Assert.Equal(1.0, solid0.Alpha, 3);

            // Top layer: triangle glyph filled solid with palette 1.
            var layer1 = Assert.IsType<ColrPaintGlyph>(colr.GetLayerPaint(root.FirstLayerIndex + 1));
            Assert.Equal(Gid(face, 'Y'), layer1.GlyphId);
            Assert.Equal(1, Assert.IsType<ColrPaintSolid>(layer1.Paint).PaletteIndex);
        }

        [Fact]
        public void ColrV1_ParsesLinearGradientWithColorLineStops()
        {
            OpenTypeFontface face = Face(BundledFonts.ColorV1);
            ColrTable colr = face.colr;

            var glyphPaint = Assert.IsType<ColrPaintGlyph>(colr.GetV1BaseGlyphPaint(Gid(face, 'G')));
            Assert.Equal(Gid(face, 'X'), glyphPaint.GlyphId);

            var gradient = Assert.IsType<ColrPaintLinearGradient>(glyphPaint.Paint);
            Assert.Equal(100, gradient.X0, 3);
            Assert.Equal(0, gradient.Y0, 3);
            Assert.Equal(900, gradient.X1, 3);
            Assert.Equal(2, gradient.Line.Stops.Count);
            Assert.Equal(ColrExtend.Pad, gradient.Line.Extend);
            Assert.Equal(0.0, gradient.Line.Stops[0].Offset, 3);
            Assert.Equal(0, gradient.Line.Stops[0].PaletteIndex);
            Assert.Equal(1.0, gradient.Line.Stops[1].Offset, 3);
            Assert.Equal(2, gradient.Line.Stops[1].PaletteIndex);
        }

        [Fact]
        public void ColrV1_ParsesTranslateTransformOverGlyph()
        {
            OpenTypeFontface face = Face(BundledFonts.ColorV1);
            ColrTable colr = face.colr;

            var transform = Assert.IsType<ColrPaintTransform>(colr.GetV1BaseGlyphPaint(Gid(face, 'T')));
            // Translate(100, 50): identity linear part, offset in DX/DY.
            Assert.Equal(1, transform.Affine.XX, 3);
            Assert.Equal(1, transform.Affine.YY, 3);
            Assert.Equal(100, transform.Affine.DX, 3);
            Assert.Equal(50, transform.Affine.DY, 3);

            var glyphPaint = Assert.IsType<ColrPaintGlyph>(transform.Paint);
            Assert.Equal(Gid(face, 'Y'), glyphPaint.GlyphId);
            Assert.Equal(3, Assert.IsType<ColrPaintSolid>(glyphPaint.Paint).PaletteIndex);
        }

        [Fact]
        public void ColrV1_ParsesRadialAndSweepGradients()
        {
            OpenTypeFontface face = Face(BundledFonts.ColorV1);
            ColrTable colr = face.colr;

            var radial = Assert.IsType<ColrPaintRadialGradient>(
                Assert.IsType<ColrPaintGlyph>(colr.GetV1BaseGlyphPaint(Gid(face, 'R'))).Paint);
            Assert.Equal(500, radial.X1, 3);
            Assert.Equal(400, radial.R1, 3);
            Assert.Equal(2, radial.Line.Stops.Count);

            var sweep = Assert.IsType<ColrPaintSweepGradient>(
                Assert.IsType<ColrPaintGlyph>(colr.GetV1BaseGlyphPaint(Gid(face, 'S'))).Paint);
            Assert.Equal(500, sweep.CenterX, 3);
            Assert.Equal(3, sweep.Line.Stops.Count);
        }

        [Fact]
        public void ColrV1_ParsesTransformFamilyAndColrGlyphReference()
        {
            OpenTypeFontface face = Face(BundledFonts.ColorV1);
            ColrTable colr = face.colr;

            // Every transform-family paint normalizes to a ColrPaintTransform wrapping its child:
            // C=scale-around-center, O=rotate-around-center, K=skew, W=affine, D=scale, E=scale-uniform,
            // H=scale-uniform-around-center, I=rotate, J=skew-around-center.
            foreach (char ch in new[] { 'C', 'O', 'K', 'W', 'D', 'E', 'H', 'I', 'J' })
            {
                var transform = Assert.IsType<ColrPaintTransform>(colr.GetV1BaseGlyphPaint(Gid(face, ch)));
                Assert.NotNull(transform.Paint);
            }

            // General affine transform ('W') carries the authored translation.
            var affine = Assert.IsType<ColrPaintTransform>(colr.GetV1BaseGlyphPaint(Gid(face, 'W'))).Affine;
            Assert.Equal(50, affine.DX, 3);
            Assert.Equal(50, affine.DY, 3);

            // Skew ('K', xSkewAngle=15, ySkewAngle=0): x' = x - tan(15°)·y, y' = y. So in
            // (XX, YX, XY, YY): XY = -tan(15°), YX = tan(0) = 0 (guards against the axis/sign slots).
            var skew = Assert.IsType<ColrPaintTransform>(colr.GetV1BaseGlyphPaint(Gid(face, 'K'))).Affine;
            Assert.Equal(1, skew.XX, 3);
            Assert.Equal(1, skew.YY, 3);
            Assert.Equal(-System.Math.Tan(15 * System.Math.PI / 180), skew.XY, 3);
            Assert.Equal(0, skew.YX, 3);

            // PaintColrGlyph references another base glyph.
            var colrGlyph = Assert.IsType<ColrPaintColrGlyph>(colr.GetV1BaseGlyphPaint(Gid(face, 'L')));
            Assert.Equal(Gid(face, 'A'), colrGlyph.GlyphId);
        }

        [Fact]
        public void Colr_UnknownGlyph_ReturnsNoPaint()
        {
            OpenTypeFontface face = Face(BundledFonts.ColorV1);
            Assert.Null(face.colr.GetV1BaseGlyphPaint(Gid(face, 'X'))); // 'X' is a plain outline glyph
            Assert.Null(face.colr.GetLayerPaint(9999));                 // out-of-range layer index
        }

        [Fact]
        public void OrdinaryFont_HasNoColorTables()
        {
            OpenTypeFontface face = Face(BundledFonts.Ttf);
            Assert.Null(face.colr);
            Assert.Null(face.cpal);
        }

        private static void AssertColor(CpalTable cpal, int entry, byte r, byte g, byte b, byte a)
        {
            Assert.True(cpal.TryGetColor(0, entry, out var color));
            Assert.Equal((r, g, b, a), color);
        }
    }
}
