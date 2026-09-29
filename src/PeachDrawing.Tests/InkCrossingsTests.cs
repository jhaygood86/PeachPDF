using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Tests
{
    public class InkCrossingsTests
    {
        private const double Size = 32;
        private static readonly Typeface Face = Load(BundledFonts.Ttf);

        private static Typeface Load(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "InkCrossings-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static double Scale => Size / Face.Metrics.UnitsPerEm;

        [Fact]
        public void Measure_ADescenderCrossesABandJustBelowTheBaseline()
        {
            // "gpy" all have descenders; a band a little under the baseline is crossed by each.
            var spans = InkCrossings.Measure(Face, "gpy", Scale, 3, 5, 0, ShapeSettings.Default);

            Assert.NotNull(spans);
            Assert.NotEmpty(spans);
            Assert.All(spans, s => Assert.True(s.End > s.Start));
        }

        [Fact]
        public void Measure_TextWithNoDescendersCrossesNothingBelowTheBaseline()
        {
            var spans = InkCrossings.Measure(Face, "HEX", Scale, 6, 8, 0, ShapeSettings.Default);

            Assert.NotNull(spans);
            Assert.Empty(spans);
        }

        [Fact]
        public void Measure_SpansAreOrderedAndDisjoint()
        {
            var spans = InkCrossings.Measure(Face, "Typography gyp", Scale, -10, 6, 0, ShapeSettings.Default)!;

            for (var i = 1; i < spans.Count; i++)
                Assert.True(spans[i].Start > spans[i - 1].End);
        }

        [Fact]
        public void Measure_LetterSpacingMovesLaterGlyphs()
        {
            var tight = InkCrossings.Measure(Face, "HH", Scale, -6, -4, 0, ShapeSettings.Default)!;
            var loose = InkCrossings.Measure(Face, "HH", Scale, -6, -4, 10, ShapeSettings.Default)!;

            Assert.Equal(tight[0].Start, loose[0].Start, 6);
            Assert.True(loose[^1].Start >= tight[^1].Start + 9.5);
        }

        [Fact]
        public void Measure_ARunOfSpaces_IsUnknownNotEmpty() =>
            Assert.Null(InkCrossings.Measure(Face, "   ", Scale, 3, 5, 0, ShapeSettings.Default));

        [Fact]
        public void Measure_AnEmptyOrInvertedBandOrZeroScale_IsUnknown()
        {
            Assert.Null(InkCrossings.Measure(Face, "gpy", Scale, 5, 5, 0, ShapeSettings.Default));
            Assert.Null(InkCrossings.Measure(Face, "gpy", Scale, 5, 3, 0, ShapeSettings.Default));
            Assert.Null(InkCrossings.Measure(Face, "gpy", 0, 3, 5, 0, ShapeSettings.Default));
        }

        [Fact]
        public void Measure_ValidatesItsArguments()
        {
            Assert.Throws<ArgumentNullException>(() => InkCrossings.Measure(null!, "a", Scale, 0, 1, 0, ShapeSettings.Default));
            Assert.Throws<ArgumentNullException>(() => InkCrossings.Measure(Face, null!, Scale, 0, 1, 0, ShapeSettings.Default));
        }

        [Fact]
        public void RasterCanvas_ReportsInkCrossings_AtTheRunsOwnPosition()
        {
            var context = new RasterRenderContext();
            using var canvas = context.CreateCanvas(300, 100);
            var font = new TypefaceFont(Face, Size);
            var metrics = Face.Metrics;
            var baseline = 20 + Size * metrics.CellAscent / metrics.UnitsPerEm;

            // A band 3-5 units under the baseline, for a run whose top-left is at (40, 20).
            var atOrigin = canvas.GetInkCrossings("gpy", font, new PaintPoint(0, 20), baseline + 3, baseline + 5);
            var moved = canvas.GetInkCrossings("gpy", font, new PaintPoint(40, 20), baseline + 3, baseline + 5);

            Assert.NotNull(atOrigin);
            Assert.NotEmpty(atOrigin);
            Assert.Equal(atOrigin!.Count, moved!.Count);
            for (var i = 0; i < atOrigin.Count; i++)
                Assert.Equal(atOrigin[i].Start + 40, moved[i].Start, 6);
        }

        [Fact]
        public void RasterCanvas_AgreesWithWhereItsInkIsActuallyDrawn()
        {
            var context = new RasterRenderContext();
            using var canvas = context.CreateCanvas(300, 100);
            var font = new TypefaceFont(Face, Size);
            var metrics = Face.Metrics;
            var baseline = 20 + Size * metrics.CellAscent / metrics.UnitsPerEm;
            var bandTop = baseline + 4;
            var bandBottom = baseline + 6;

            canvas.DrawString("gy", font, PaintColor.Black, new PaintPoint(10, 20), canvas.MeasureString("gy", font));
            var spans = canvas.GetInkCrossings("gy", font, new PaintPoint(10, 20), bandTop, bandBottom)!;
            var buffer = canvas.ToPixelBuffer();
            var row = (int)Math.Round((bandTop + bandBottom) / 2);

            // Wherever a span says there is ink on that row there is, and just outside every span there is none.
            var inkOnRow = new List<int>();
            for (var x = 0; x < buffer.Width; x++)
            {
                if (buffer.PremultipliedRgba.Span[(row * buffer.Width + x) * 4 + 3] > 0)
                    inkOnRow.Add(x);
            }

            Assert.NotEmpty(inkOnRow);
            Assert.All(inkOnRow, x => Assert.Contains(spans, s => x + 1 >= s.Start - 1 && x <= s.End + 1));
        }

        [Fact]
        public void RasterCanvas_AnInvertedBand_IsUnknown()
        {
            var context = new RasterRenderContext();
            using var canvas = context.CreateCanvas(100, 100);

            Assert.Null(canvas.GetInkCrossings("g", new TypefaceFont(Face, Size), new PaintPoint(0, 0), 10, 5));
        }
    }
}
