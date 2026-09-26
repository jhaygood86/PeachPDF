using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>Grid-fitting of fonts with CFF outlines through the public API, and what the engine keeps between glyphs and sizes.</summary>
    public class HintingCffTests
    {
        private static Typeface Code => TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "SourceCodePro-Regular.otf"));

        private static ushort GlyphOf(Typeface typeface, char c)
        {
            Assert.True(typeface.TryMapRune(new Rune(c), out var glyph));
            return glyph;
        }

        private static OutlineRequest Request(double ppem, GridFitting mode = GridFitting.Standard) => new() { PixelsPerEm = ppem, GridFitting = mode };

        private static List<(double X, double Y)> PointsOf(GlyphOutline outline)
        {
            var points = new List<(double, double)>();
            foreach (var contour in outline.Contours)
            {
                points.Add((contour.Start.X, contour.Start.Y));
                foreach (var segment in contour.Segments)
                {
                    if (segment.IsCubic)
                    {
                        points.Add((segment.Control1.X, segment.Control1.Y));
                        points.Add((segment.Control2.X, segment.Control2.Y));
                    }

                    points.Add((segment.End.X, segment.End.Y));
                }
            }

            return points;
        }

        [Fact]
        public void ACffFontIsGridFittedAndKeepsItsShapeInPixels()
        {
            var font = Code;
            var glyph = GlyphOf(font, 'e');

            Assert.True(font.TryGetOutline(glyph, out var design));
            Assert.True(font.TryGetOutline(glyph, Request(20), out var fitted));

            Assert.True(fitted.IsGridFitted);
            Assert.Equal(20, fitted.PixelsPerEm);
            Assert.False(design.IsGridFitted);
            Assert.Equal(design.Contours.Count, fitted.Contours.Count);

            // hinting moves points by fractions of a pixel: the fitted outline is the scaled design within a pixel
            double scale = 20.0 / font.Metrics.UnitsPerEm;
            var expected = PointsOf(design).Select(p => (X: p.X * scale, Y: p.Y * scale)).ToList();
            var actual = PointsOf(fitted);
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.InRange(actual[i].X, expected[i].X - 1, expected[i].X + 1);
                Assert.InRange(actual[i].Y, expected[i].Y - 1, expected[i].Y + 1);
            }

            // the advance is a whole number of pixels, and the two ways of asking for it agree
            Assert.NotNull(fitted.GridFittedAdvance);
            Assert.Equal(Math.Round(fitted.GridFittedAdvance!.Value), fitted.GridFittedAdvance);
            Assert.True(font.TryGetGridFittedAdvance(glyph, Request(20), out var advance));
            Assert.Equal(fitted.GridFittedAdvance, advance);
        }

        [Fact]
        public void TheFlatEdgesOfAGlyphAreOnPixelRowsWhenFitted()
        {
            // the baseline and x-height zones of the font capture the bottom and top of a flat glyph: at 12 ppem the letter x sits on whole rows
            var font = Code;
            var glyph = GlyphOf(font, 'x');
            Assert.True(font.TryGetOutline(glyph, Request(12), out var fitted));

            var ys = PointsOf(fitted).Select(p => p.Y).ToList();
            Assert.Equal(0, ys.Min());
            Assert.Equal(Math.Round(ys.Max()), ys.Max(), 6);
        }

        [Fact]
        public void BothModesGiveTheSameCffOutline()
        {
            // Adobe's engine has one behaviour: the modes only tell the TrueType interpreters apart
            var font = Code;
            var glyph = GlyphOf(font, 'g');

            Assert.True(font.TryGetOutline(glyph, Request(13, GridFitting.Standard), out var standard));
            Assert.True(font.TryGetOutline(glyph, Request(13, GridFitting.Monochrome), out var monochrome));

            Assert.Same(standard, monochrome);
        }

        [Fact]
        public void ACffFontIsFittedAtTheSizeAskedForWithoutRoundingItToWholePixels()
        {
            var font = Code;
            var glyph = GlyphOf(font, 'H');

            Assert.True(font.TryGetOutline(glyph, Request(11.4), out var fractional));
            Assert.True(font.TryGetOutline(glyph, Request(11), out var whole));

            Assert.Equal(11.4, fractional.PixelsPerEm, 1);
            Assert.NotEqual(PointsOf(whole), PointsOf(fractional));
        }

        [Fact]
        public void AGlyphWithNoInkHasAFittedAdvanceButNoOutline()
        {
            var font = Code;
            var space = GlyphOf(font, ' ');

            Assert.False(font.TryGetOutline(space, Request(12), out _));
            Assert.True(font.TryGetGridFittedAdvance(space, Request(12), out var advance));
            Assert.Equal(Math.Round(advance), advance);
            Assert.True(advance > 0);
        }

        [Fact]
        public void AGlyphIsFittedTheSameWhateverWasLoadedBeforeIt()
        {
            // The random operator of the charstring language draws numbers from a generator: FreeType lets a glyph's numbers depend on the
            // glyphs loaded before it, here every glyph starts from the font's own seed. The synthetic fixture has glyphs that use it.
            var font = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "HintingCff.otf"));

            var forward = new List<List<(double, double)>?>();
            for (ushort g = 1; g < 300; g++)
                forward.Add(font.TryGetOutline(g, Request(15), out var o) && o.IsGridFitted ? PointsOf(o) : null);

            var other = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "HintingCff.otf"));
            for (ushort g = 299; g >= 1; g--)
            {
                var again = other.TryGetOutline(g, Request(15), out var o) && o.IsGridFitted ? PointsOf(o) : null;
                Assert.Equal(forward[g - 1], again);
            }

            Assert.Contains(forward, f => f is not null);
            Assert.Contains(forward, f => f is null); // some glyphs of the fixture are refused, and fall back
        }

        [Fact]
        public async Task ManyThreadsGetTheSameCffOutlinesAsOne()
        {
            var font = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "SourceCodePro-Regular.otf"));
            var glyphs = "The quick brown fox jumps over the lazy dog 0123456789".Where(c => !char.IsWhiteSpace(c)).Select(c => GlyphOf(font, c)).Distinct().ToArray();
            var sizes = new[] { 9.0, 11.5, 14.0, 19.0 };

            var serial = new Dictionary<(ushort, double), List<(double, double)>>();
            foreach (var g in glyphs)
                foreach (var size in sizes)
                {
                    Assert.True(font.TryGetOutline(g, Request(size), out var o));
                    serial[(g, size)] = PointsOf(o);
                }

            // a second face object, with nothing cached, so this runs the engine concurrently from the start
            var fresh = TypefaceFixtures.FromBytes(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SourceCodePro-Regular.otf")));
            await Task.WhenAll(Enumerable.Range(0, 12).Select(t => Task.Run(() =>
            {
                var random = new Random(t);
                for (int i = 0; i < 250; i++)
                {
                    var g = glyphs[random.Next(glyphs.Length)];
                    var size = sizes[random.Next(sizes.Length)];
                    Assert.True(fresh.TryGetOutline(g, Request(size), out var o));
                    Assert.Equal(serial[(g, size)], PointsOf(o));
                }
            })));
        }

        [Fact]
        public void AnImpossibleSizeOfACffFontFallsBackToTheScaledDesign()
        {
            var font = Code;
            var glyph = GlyphOf(font, 'a');

            // more than the 2000 ppem Adobe's engine takes, and less than the half of a pixel that rounds to none
            foreach (double ppem in new[] { 2500.0, 0.004 })
            {
                Assert.True(font.TryGetOutline(glyph, Request(ppem), out var outline));
                Assert.False(outline.IsGridFitted);
                Assert.Equal(ppem, outline.PixelsPerEm);
            }
        }

        [Fact]
        public void TheCffFixturesOfEveryKindAreGridFitted()
        {
            foreach (var file in new[] { "HintingCff.otf", "HintingCffIdeo.otf", "HintingCffMatrix.otf", "HintingCffCid.otf" })
            {
                var font = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, file));
                int fitted = 0;
                for (ushort g = 1; g < 100; g++)
                {
                    if (font.TryGetOutline(g, Request(12), out var outline) && outline.IsGridFitted)
                        fitted++;
                }

                Assert.True(fitted > 40, $"{file}: only {fitted} glyphs were fitted");
            }
        }

        [Fact]
        public void TheEngineSaysWhetherAFontCanBeHinted()
        {
            Assert.True(Code.Face.Descriptor.Hinting.CanHint);
            Assert.True(TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf")).Face.Descriptor.Hinting.CanHint);
            Assert.False(TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "VariableTest.ttf")).Face.Descriptor.Hinting.CanHint);
        }
    }
}
