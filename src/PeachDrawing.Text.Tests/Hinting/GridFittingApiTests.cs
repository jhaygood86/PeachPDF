using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>What a caller sees of grid-fitting: <see cref="Typeface.TryGetOutline(ushort, in OutlineRequest, out GlyphOutline)"/> and its companions.</summary>
    public class GridFittingApiTests
    {
        private static readonly Lazy<Typeface> Sans = new(() => TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "LiberationSans-Regular.woff")));

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
        public void NoGridFittingIsExactlyTheDesignOutline()
        {
            var glyph = GlyphOf(Sans.Value, 'g');
            Assert.True(Sans.Value.TryGetOutline(glyph, out var design));

            foreach (var request in new[] { default, Request(12, GridFitting.None), Request(0, GridFitting.None) })
            {
                Assert.True(Sans.Value.TryGetOutline(glyph, request, out var outline));
                Assert.False(outline.IsGridFitted);
                Assert.Equal(0, outline.PixelsPerEm);
                Assert.Null(outline.GridFittedAdvance);
                Assert.Equal(PointsOf(design), PointsOf(outline));
            }
        }

        [Fact]
        public void StandardGridFittingGivesAPixelSpaceOutlineWithTheFontsHeightsOnWholePixels()
        {
            var glyph = GlyphOf(Sans.Value, 'H');
            Assert.True(Sans.Value.TryGetOutline(glyph, Request(13), out var outline));

            Assert.True(outline.IsGridFitted);
            Assert.Equal(13, outline.PixelsPerEm);

            // pixel space: an H is about ten pixels tall at 13 ppem and its top and bottom sit on pixel boundaries
            var ys = PointsOf(outline).Select(p => p.Y).ToList();
            Assert.InRange(ys.Max(), 8, 11);
            Assert.Equal(Math.Round(ys.Max()), ys.Max());
            Assert.Equal(Math.Round(ys.Min()), ys.Min());
        }

        [Fact]
        public void TheFittedOutlineStaysCloseToTheScaledDesign()
        {
            var glyph = GlyphOf(Sans.Value, 'e');
            Assert.True(Sans.Value.TryGetOutline(glyph, out var design));
            Assert.True(Sans.Value.TryGetOutline(glyph, Request(20), out var fitted));

            double scale = 20.0 / Sans.Value.Metrics.UnitsPerEm;
            var expected = PointsOf(design).Select(p => (X: p.X * scale, Y: p.Y * scale)).ToList();
            var actual = PointsOf(fitted);

            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.InRange(actual[i].X, expected[i].X - 1.2, expected[i].X + 1.2);
                Assert.InRange(actual[i].Y, expected[i].Y - 1.2, expected[i].Y + 1.2);
            }
        }

        [Fact]
        public void HintingChangesTheOutlineAtSmallSizes()
        {
            var glyph = GlyphOf(Sans.Value, 'o');
            Assert.True(Sans.Value.TryGetOutline(glyph, Request(11), out var fitted));
            Assert.True(Sans.Value.TryGetOutline(glyph, out var design));

            double scale = 11.0 / Sans.Value.Metrics.UnitsPerEm;
            var unhinted = PointsOf(design).Select(p => (X: p.X * scale, Y: p.Y * scale)).ToList();
            Assert.NotEqual(unhinted, PointsOf(fitted));
        }

        [Fact]
        public void TheAdvanceIsAWholeNumberOfPixels()
        {
            foreach (char c in "abgHW.")
            {
                Assert.True(Sans.Value.TryGetGridFittedAdvance(GlyphOf(Sans.Value, c), Request(13), out var advance));
                Assert.True(advance > 0);
                Assert.Equal(Math.Round(advance), advance);
            }
        }

        [Fact]
        public void AGlyphWithNoInkHasAnAdvanceButNoOutline()
        {
            var space = GlyphOf(Sans.Value, ' ');
            Assert.False(Sans.Value.TryGetOutline(space, Request(13), out var outline));
            Assert.True(outline.IsEmpty);

            Assert.True(Sans.Value.TryGetGridFittedAdvance(space, Request(13), out var advance));
            Assert.Equal(Math.Round(advance), advance);
            Assert.True(advance > 0);
        }

        [Fact]
        public void AnOutlineCarriesItsFittedAdvance()
        {
            var glyph = GlyphOf(Sans.Value, 'a');
            Assert.True(Sans.Value.TryGetOutline(glyph, Request(16), out var outline));
            Assert.True(Sans.Value.TryGetGridFittedAdvance(glyph, Request(16), out var advance));
            Assert.Equal(advance, outline.GridFittedAdvance);
        }

        [Fact]
        public void StandardLeavesTheHorizontalDesignAloneAndMonochromeFitsBothDirections()
        {
            var glyph = GlyphOf(Sans.Value, 'H');
            Assert.True(Sans.Value.TryGetOutline(glyph, out var design));
            Assert.True(Sans.Value.TryGetOutline(glyph, Request(12, GridFitting.Monochrome), out var mono));
            Assert.True(Sans.Value.TryGetOutline(glyph, Request(12, GridFitting.Standard), out var standard));

            Assert.True(mono.IsGridFitted);

            double scale = 12.0 / Sans.Value.Metrics.UnitsPerEm;
            var designX = PointsOf(design).Select(p => p.X * scale).ToList();
            var standardX = PointsOf(standard).Select(p => p.X).ToList();
            var monoX = PointsOf(mono).Select(p => p.X).ToList();

            // the interpreter that ignores the horizontal direction keeps the design's x, to the precision of 1/64 pixel
            for (int i = 0; i < designX.Count; i++)
                Assert.InRange(standardX[i], designX[i] - 1.0 / 64, designX[i] + 1.0 / 64);

            // the original interpreter moves points horizontally too
            Assert.Contains(Enumerable.Range(0, designX.Count), i => Math.Abs(monoX[i] - designX[i]) > 1.0 / 64);
        }

        [Fact]
        public void FractionalSizesAreHintedAtTheirOwnSize()
        {
            var glyph = GlyphOf(Sans.Value, 'H');
            Assert.True(Sans.Value.TryGetOutline(glyph, Request(12.5), out var a));
            Assert.True(Sans.Value.TryGetOutline(glyph, Request(12.6), out var b));
            Assert.Equal(12.5, a.PixelsPerEm);
            Assert.Equal(12.59375, b.PixelsPerEm); // in 1/64 pixel
        }

        [Fact]
        public void AskingAgainForTheSameGlyphAndSizeAnswersTheSameOutline()
        {
            var glyph = GlyphOf(Sans.Value, 'k');
            Assert.True(Sans.Value.TryGetOutline(glyph, Request(14), out var first));
            Assert.True(Sans.Value.TryGetOutline(glyph, Request(14), out var second));
            Assert.Same(first, second);
        }

        [Fact]
        public void ACffFontIsNotGridFittedYetAndGetsTheScaledDesign()
        {
            var cff = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "SourceCodePro-Regular.otf"));
            var glyph = GlyphOf(cff, 'e');

            Assert.True(cff.TryGetOutline(glyph, out var design));
            Assert.True(cff.TryGetOutline(glyph, Request(20), out var outline));
            Assert.False(outline.IsGridFitted);
            Assert.Equal(20, outline.PixelsPerEm);
            Assert.Null(outline.GridFittedAdvance);
            Assert.False(cff.TryGetGridFittedAdvance(glyph, Request(20), out _));

            double scale = 20.0 / cff.Metrics.UnitsPerEm;
            Assert.Equal(PointsOf(design).Select(p => (X: p.X * scale, Y: p.Y * scale)), PointsOf(outline));
        }

        [Fact]
        public void ATrueTypeFontWithoutInstructionsIsStillFittedAndOnlyScaled()
        {
            // The variable test font has no hinting programs: the outline is the scaled design rounded to 1/64 pixel.
            var font = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "VariableTest.ttf"));
            var glyph = GlyphOf(font, 'A');
            Assert.True(font.TryGetOutline(glyph, out var design));
            Assert.True(font.TryGetOutline(glyph, Request(30), out var outline));

            Assert.True(outline.IsGridFitted);
            double scale = 30.0 / font.Metrics.UnitsPerEm;
            var expected = PointsOf(design).Select(p => (X: p.X * scale, Y: p.Y * scale)).ToList();
            var actual = PointsOf(outline);
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.InRange(actual[i].X, expected[i].X - 1.0 / 64, expected[i].X + 1.0 / 64);
                Assert.InRange(actual[i].Y, expected[i].Y - 1.0 / 64, expected[i].Y + 1.0 / 64);
            }
        }

        [Fact]
        public void AVariableFontInstanceIsFittedAtItsLocation()
        {
            var font = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "VariableTest.ttf"));
            var bold = font.WithAxes([new AxisSetting(AxisTags.Weight, 800)]);
            var glyph = GlyphOf(bold, 'A');

            Assert.True(bold.TryGetOutline(glyph, out var design));
            Assert.True(bold.TryGetOutline(glyph, Request(30), out var outline));
            Assert.True(font.TryGetOutline(GlyphOf(font, 'A'), Request(30), out var regular));

            Assert.True(outline.IsGridFitted);
            double scale = 30.0 / bold.Metrics.UnitsPerEm;
            var expected = PointsOf(design).Select(p => (X: p.X * scale, Y: p.Y * scale)).ToList();
            var actual = PointsOf(outline);
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.InRange(actual[i].X, expected[i].X - 0.05, expected[i].X + 0.05);
                Assert.InRange(actual[i].Y, expected[i].Y - 0.05, expected[i].Y + 0.05);
            }

            // and it is not the default instance's outline
            Assert.NotEqual(PointsOf(regular), actual);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-3.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void AGridFittedRequestNeedsAPositiveFiniteSize(double ppem)
        {
            var glyph = GlyphOf(Sans.Value, 'a');
            Assert.Throws<ArgumentOutOfRangeException>(() => Sans.Value.TryGetOutline(glyph, Request(ppem), out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => Sans.Value.TryGetGridFittedAdvance(glyph, Request(ppem), out _));
        }

        [Fact]
        public void AnUndefinedGridFittingIsRejected()
        {
            var glyph = GlyphOf(Sans.Value, 'a');
            Assert.Throws<ArgumentOutOfRangeException>(() => Sans.Value.TryGetOutline(glyph, Request(12, (GridFitting)42), out _));
        }

        [Theory]
        [InlineData(0.004)]     // rounds to no pixel at all
        [InlineData(70000)]     // beyond what the size tables can say
        public void ASizeThatCannotBeHintedFallsBackToTheScaledDesign(double ppem)
        {
            var glyph = GlyphOf(Sans.Value, 'a');
            Assert.True(Sans.Value.TryGetOutline(glyph, out var design));
            Assert.True(Sans.Value.TryGetOutline(glyph, Request(ppem), out var outline));

            Assert.False(outline.IsGridFitted);
            double scale = ppem / Sans.Value.Metrics.UnitsPerEm;
            Assert.Equal(PointsOf(design).Select(p => (X: p.X * scale, Y: p.Y * scale)), PointsOf(outline));
        }

        [Fact]
        public void TheFittedNoGridFittingAdvanceIsNotAvailable()
        {
            Assert.False(Sans.Value.TryGetGridFittedAdvance(GlyphOf(Sans.Value, 'a'), Request(12, GridFitting.None), out var advance));
            Assert.Equal(0, advance);
        }

        [Fact]
        public async Task ManyThreadsGetTheSameOutlinesAsOne()
        {
            var typeface = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "LiberationSerif-Regular.woff"));
            var glyphs = "The quick brown fox jumps over the lazy dog 0123456789".Where(c => !char.IsWhiteSpace(c)).Select(c => GlyphOf(typeface, c)).Distinct().ToArray();
            var sizes = new[] { 9.0, 11.5, 14.0, 19.0 };

            var serial = new Dictionary<(ushort, double), List<(double, double)>>();
            foreach (var g in glyphs)
                foreach (var size in sizes)
                {
                    Assert.True(typeface.TryGetOutline(g, Request(size), out var o));
                    serial[(g, size)] = PointsOf(o);
                }

            // a second face object: nothing cached, so this runs hinting concurrently from the start
            var fresh = TypefaceFixtures.FromBytes(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "LiberationSerif-Regular.woff")));
            await Task.WhenAll(Enumerable.Range(0, 16).Select(t => Task.Run(() =>
            {
                var random = new Random(t);
                for (int i = 0; i < 300; i++)
                {
                    var g = glyphs[random.Next(glyphs.Length)];
                    var size = sizes[random.Next(sizes.Length)];
                    Assert.True(fresh.TryGetOutline(g, Request(size), out var o));
                    Assert.Equal(serial[(g, size)], PointsOf(o));
                }
            })));
        }
    }
}
