using PeachDrawing.Text;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Outlines;
using PeachDrawing.Text.Tests.TestSupport;
using PeachPDF.Tests.TestSupport;
using System.Drawing;
using System.Text;

namespace PeachDrawing.Text.Tests.PublicApi
{
    /// <summary>
    /// Glyph outlines, colour glyphs (COLR versions 0 and 1 with their palettes) and bitmap glyphs as a consumer outside the
    /// assembly would read them. Each answer is also compared with the engine's own reader, which the public members wrap.
    /// </summary>
    public class OutlinesPublicApiTests
    {
        private static Typeface Face(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "Outlines-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static ushort? FirstGlyph(Func<ushort, bool> predicate)
        {
            for (ushort glyph = 0; glyph < 3000; glyph++)
            {
                if (predicate(glyph)) return glyph;
            }

            return null;
        }

        [Fact]
        public void TryGetOutline_ReadsTheContoursOfAGlyph()
        {
            var face = Face(BundledFonts.Ttf);
            face.TryMapRune(new Rune('o'), out var glyph);

            Assert.True(face.TryGetOutline(glyph, out var outline));

            Assert.False(outline.IsEmpty);
            // An "o" is a ring: an outer contour and an inner one that winds the other way.
            Assert.Equal(2, outline.Contours.Count);
            Assert.All(outline.Contours, contour => Assert.NotEmpty(contour.Segments));
            Assert.Contains(outline.Contours.SelectMany(c => c.Segments), segment => segment.IsCubic);
        }

        [Fact]
        public void TryGetOutline_MatchesTheEnginesDecoder()
        {
            var face = Face(BundledFonts.Ttf);
            face.TryMapRune(new Rune('g'), out var glyph);

            Assert.True(face.TryGetOutline(glyph, out var outline));
            Assert.True(face.Face.Descriptor.TryGetGlyphOutline(glyph, out var expected));

            Assert.Equal(expected.Contours.Count, outline.Contours.Count);
            for (var i = 0; i < expected.Contours.Count; i++)
            {
                Assert.Equal(expected.Contours[i].Start, outline.Contours[i].Start);
                Assert.Equal(expected.Contours[i].Segments, outline.Contours[i].Segments);
            }
        }

        [Fact]
        public void TryGetOutline_OfAGlyphWithNoInk_ReportsNothing()
        {
            var face = Face(BundledFonts.Ttf);
            face.TryMapRune(new Rune(' '), out var space);

            Assert.False(face.TryGetOutline(space, out var outline));
            Assert.True(outline.IsEmpty);
            Assert.False(face.TryGetOutline(ushort.MaxValue, out _));
        }

        [Fact]
        public void TryGetOutline_ReadsACffFont()
        {
            var face = Face(BundledFonts.Otf);
            face.TryMapRune(new Rune('A'), out var glyph);

            Assert.True(face.TryGetOutline(glyph, out var outline));
            Assert.NotEmpty(outline.Contours);
        }

        [Fact]
        public void Crossings_FindsWhereInkLiesAcrossABand()
        {
            var face = Face(BundledFonts.Ttf);
            face.TryMapRune(new Rune('o'), out var glyph);
            face.TryGetOutline(glyph, out var outline);
            var metrics = face.Metrics;
            var middle = metrics.XHeight / 2.0;

            // Across the middle of an "o" the ink is two strokes with the counter between them.
            var spans = outline.Crossings(middle - 5, middle + 5);

            Assert.Equal(2, spans.Count);
            Assert.True(spans[0].End < spans[1].Start);
            // Well below the baseline there is nothing.
            Assert.Empty(outline.Crossings(-metrics.UnitsPerEm, -metrics.UnitsPerEm / 2.0));
            // A band that is not above its lower edge has nothing in it.
            Assert.Empty(outline.Crossings(middle, middle));
        }

        [Fact]
        public void OutlineSegments_AreALineOrACubic()
        {
            var line = OutlineSegment.Line(new OutlinePoint(3, 4));
            var cubic = OutlineSegment.Cubic(new OutlinePoint(1, 1), new OutlinePoint(2, 2), new OutlinePoint(3, 3));

            Assert.False(line.IsCubic);
            Assert.Equal(new OutlinePoint(3, 4), line.End);
            Assert.True(cubic.IsCubic);
            Assert.Equal(new OutlinePoint(1, 1), cubic.Control1);
            Assert.Equal(new OutlinePoint(2, 2), cubic.Control2);
        }

        [Fact]
        public void ColorPalette_IsOnlyPresentOnAColourFace()
        {
            Assert.Null(Face(BundledFonts.Ttf).ColorPalette);

            var palette = Face(BundledFonts.ColorV0).ColorPalette;

            Assert.NotNull(palette);
            Assert.True(palette!.PaletteCount >= 1);
            Assert.True(palette.EntriesPerPalette >= 1);
        }

        [Fact]
        public void ColorPalette_LooksUpColoursAndMatchesTheEnginesTable()
        {
            var face = Face(BundledFonts.ColorV0);
            var palette = face.ColorPalette!;
            var table = face.Face.Descriptor.ColorPalette;

            Assert.True(palette.TryGetColor(0, 0, out var color));
            Assert.True(table.TryGetColor(0, 0, out var expected));
            Assert.Equal(Color.FromArgb(expected.A, expected.R, expected.G, expected.B), color);

            // An out-of-range palette falls back to the first; an entry that does not exist is not found.
            Assert.True(palette.TryGetColor(palette.PaletteCount + 5, 0, out var fallback));
            Assert.Equal(color, fallback);
            Assert.False(palette.TryGetColor(0, palette.EntriesPerPalette, out var missing));
            Assert.Equal(default, missing);
            Assert.False(palette.TryGetColor(0, -1, out _));

            Assert.Equal(table.PaletteCount, palette.PaletteCount);
            Assert.Equal(table.EntriesPerPalette, palette.EntriesPerPalette);
            Assert.Equal(table.FirstLightPalette(), palette.FirstLightPalette());
            Assert.Equal(table.FirstDarkPalette(), palette.FirstDarkPalette());
        }

        [Fact]
        public void TryGetColorLayers_ReadsTheLayersOfAVersion0ColourGlyph()
        {
            var face = Face(BundledFonts.ColorV0);
            var glyph = FirstGlyph(g => face.TryGetColorLayers(g, out _));

            Assert.NotNull(glyph);
            Assert.True(face.TryGetColorLayers(glyph!.Value, out var layers));
            Assert.NotEmpty(layers);
            Assert.True(face.Face.Descriptor.ColorTable!.TryGetV0Layers(glyph.Value, out var expected));
            Assert.Equal(expected, layers);

            // A version 0 table has no paint graph.
            Assert.Null(face.GetColorPaint(glyph.Value));
        }

        [Fact]
        public void TryGetColorLayers_OfAFaceWithNoColour_ReportsNothing()
        {
            var face = Face(BundledFonts.Ttf);

            Assert.False(face.TryGetColorLayers(5, out var layers));
            Assert.Empty(layers);
            Assert.Null(face.GetColorPaint(5));
            Assert.Null(face.GetColorLayerPaint(0));
        }

        [Fact]
        public void GetColorPaint_ReadsThePaintGraphOfAVersion1ColourGlyph()
        {
            var face = Face(BundledFonts.ColorV1);
            var glyph = FirstGlyph(g => face.GetColorPaint(g) is not null);

            Assert.NotNull(glyph);
            var paint = face.GetColorPaint(glyph!.Value);
            Assert.NotNull(paint);
            Assert.Same(face.Face.Descriptor.ColorTable!.GetV1BaseGlyphPaint(glyph.Value)?.GetType(), paint!.GetType());

            // Walk the whole graph and check every node is well formed.
            var seen = new HashSet<Type>();
            Visit(face, paint, seen, 0);
            Assert.NotEmpty(seen);
        }

        private static void Visit(Typeface face, ColorPaint? paint, HashSet<Type> seen, int depth)
        {
            Assert.InRange(depth, 0, 64);
            if (paint is null) return;

            seen.Add(paint.GetType());
            switch (paint)
            {
                case PaintColrLayers layers:
                    Assert.True(layers.NumLayers >= 0);
                    for (var i = 0; i < layers.NumLayers; i++)
                        Visit(face, face.GetColorLayerPaint(layers.FirstLayerIndex + i), seen, depth + 1);
                    break;
                case PaintSolid solid:
                    Assert.InRange(solid.Alpha, 0.0, 1.0);
                    break;
                case PaintLinearGradient linear:
                    Assert.NotEmpty(linear.Line.Stops);
                    // A caller cannot reach the shared, cached list by casting the view back.
                    Assert.False(linear.Line.Stops is List<ColorStop>);
                    break;
                case PaintRadialGradient radial:
                    Assert.NotEmpty(radial.Line.Stops);
                    break;
                case PaintSweepGradient sweep:
                    Assert.NotEmpty(sweep.Line.Stops);
                    break;
                case PaintGlyph glyph:
                    Visit(face, glyph.Paint, seen, depth + 1);
                    break;
                case PaintColrGlyph colrGlyph:
                    Visit(face, face.GetColorPaint((ushort)colrGlyph.GlyphId), seen, depth + 1);
                    break;
                case PaintTransform transform:
                    Visit(face, transform.Paint, seen, depth + 1);
                    break;
                case PaintComposite composite:
                    Visit(face, composite.Source, seen, depth + 1);
                    Visit(face, composite.Backdrop, seen, depth + 1);
                    break;
            }
        }

        [Fact]
        public void GetColorLayerPaint_ReportsNothingForAnIndexPastTheLayerList()
        {
            var face = Face(BundledFonts.ColorV1);

            Assert.Null(face.GetColorLayerPaint(-1));
            Assert.Null(face.GetColorLayerPaint(1_000_000));
        }

        [Fact]
        public void Affine2x3_ComposesAndHasAnIdentity()
        {
            var move = new Affine2x3(1, 0, 0, 1, 10, 20);
            var scale = new Affine2x3(2, 0, 0, 3, 0, 0);

            // Multiply applies its second argument first: scale, then move.
            var both = Affine2x3.Multiply(move, scale);

            Assert.Equal(new Affine2x3(2, 0, 0, 3, 10, 20), both);
            Assert.Equal(move, Affine2x3.Multiply(move, Affine2x3.Identity));
            Assert.Equal(move, Affine2x3.Multiply(Affine2x3.Identity, move));
        }

        [Fact]
        public void TryGetBitmap_ReadsThePictureOfAGlyphFromABitmapFont()
        {
            // A real font with a CBDT strike added: one 8 by 6 picture of "A" at 20 pixels per em.
            var font = File.ReadAllBytes(BundledFonts.Ttf);
            var a = BitmapGlyphFontFixture.GlyphId(font, 'A');
            var png = SolidPng.Make(8, 6, 255, 0, 0);
            var withBitmap = BitmapGlyphFontFixture.WithCbdt(font, new BitmapGlyphFontFixture.Picture(20, a, png, 8, 6, 1, 5));

            var set = new FontSet();
            var family = set.AddData(withBitmap, new AddOptions { FamilyName = "Bitmap-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            var face = match.Typeface;

            Assert.True(face.HasBitmapGlyphs);
            Assert.True(face.TryGetBitmap((ushort)a, 20, out var bitmap));
            Assert.Equal(20, bitmap.Ppem);
            Assert.Equal((8, 6), (bitmap.Width, bitmap.Height));
            Assert.Equal(1, bitmap.BearingX);
            Assert.Equal(5, bitmap.BearingTop);
            Assert.Equal(png, bitmap.Data);
            Assert.True(face.Face.Descriptor.TryGetBitmapGlyph(a, 20, out var expected));
            Assert.Equal(expected with { Data = bitmap.Data }, bitmap);
            Assert.Equal(expected.Data, bitmap.Data);

            // A glyph the strike has no picture for is not found.
            Assert.False(face.TryGetBitmap((ushort)BitmapGlyphFontFixture.GlyphId(font, 'B'), 20, out _));
        }

        [Fact]
        public void TryGetBitmap_OfAnOrdinaryFace_ReportsNothing()
        {
            var face = Face(BundledFonts.Ttf);

            Assert.False(face.HasBitmapGlyphs);
            Assert.False(face.TryGetBitmap(5, 16, out var bitmap));
            Assert.Equal(default, bitmap);
        }

        [Fact]
        public void AFaceMatchedTwice_IsTheSameTypefaceObject()
        {
            var set = new FontSet();
            var family = set.AddFile(BundledFonts.Ttf, new AddOptions { FamilyName = "Same-" + Guid.NewGuid().ToString("N") });

            Assert.True(family.TryMatch(new TypefaceQuery(), out var first));
            Assert.True(family.TryMatch(new TypefaceQuery(), out var second));

            // A caller can key a cache by the object itself.
            Assert.Same(first.Typeface, second.Typeface);
        }
    }
}
