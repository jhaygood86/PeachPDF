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
