using PeachDrawing.Core;
using PeachDrawing.Core.Geometry;
using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Tests
{
    public class PathTextTests
    {
        private const double Size = 20;
        private static readonly Typeface Face = Load(BundledFonts.Ttf);

        private static Typeface Load(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "PathText-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static (RasterCanvas Canvas, GraphicsPath Path) LinePath(double x0, double y0, double x1, double y1, int size = 200)
        {
            var canvas = new RasterRenderContext().CreateCanvas(size, size);
            var path = canvas.GetGraphicsPath();
            path.Start(x0, y0);
            path.LineTo(x1, y1);
            return (canvas, path);
        }

        [Fact]
        public void Layout_OnAHorizontalLine_PlacesGlyphsAtTheirPenPositions()
        {
            var (canvas, path) = LinePath(10, 100, 190, 100);
            using var _ = canvas;

            var glyphs = PathText.Layout(Face, Size, "Wave", new PathMeasure(path));

            Assert.Equal(4, glyphs.Count);
            double pen = 10;
            foreach (var glyph in glyphs)
            {
                // The transform's translation is where the glyph's origin lands: the start of its advance on the baseline.
                Assert.Equal(pen, glyph.Transform.M31, 3);
                Assert.Equal(100, glyph.Transform.M32, 3);
                Assert.Equal(1, glyph.Transform.M11, 4);
                Assert.Equal(0, glyph.Transform.M12, 4);
                pen += glyph.Advance;
            }
        }

        [Fact]
        public void Layout_UsesTheShapersAdvances_SoTheRunMatchesItsShapedWidth()
        {
            var (canvas, path) = LinePath(0, 50, 190, 50);
            using var _ = canvas;
            var run = Shaper.Shape(Face, "Typography", ShapeSettings.Default);
            var expected = run.Advance * Size / Face.Metrics.UnitsPerEm;

            var glyphs = PathText.Layout(Face, Size, "Typography", new PathMeasure(path));

            Assert.Equal(expected, glyphs.Sum(g => g.Advance), 2);
        }

        [Fact]
        public void Layout_OnAVerticalLine_TurnsTheGlyphsToFollowIt()
        {
            var (canvas, path) = LinePath(50, 10, 50, 190);
            using var _ = canvas;

            var glyph = PathText.Layout(Face, Size, "A", new PathMeasure(path)).Single();

            // Heading down the canvas (+y): the glyph's x-axis points along +y.
            Assert.Equal(0, glyph.Transform.M11, 4);
            Assert.Equal(1, glyph.Transform.M12, 4);
        }

        [Fact]
        public void Layout_DropsGlyphsThatFallOffTheEndOfThePath()
        {
            var (canvas, path) = LinePath(10, 100, 40, 100);
            using var _ = canvas;

            var glyphs = PathText.Layout(Face, Size, "Overflowing text", new PathMeasure(path));

            Assert.InRange(glyphs.Count, 1, 6);
            Assert.All(glyphs, g => Assert.True(g.Transform.M31 <= 40));
        }

        [Fact]
        public void Layout_StartOffset_ShiftsTheText()
        {
            var (canvas, path) = LinePath(0, 100, 190, 100);
            using var _ = canvas;
            var measure = new PathMeasure(path);

            var plain = PathText.Layout(Face, Size, "Hi", measure).First();
            var shifted = PathText.Layout(Face, Size, "Hi", measure, new PathTextOptions(StartOffset: 30)).First();

            Assert.Equal(plain.Transform.M31 + 30, shifted.Transform.M31, 3);
        }

        [Fact]
        public void Layout_MiddleAnchor_CentresTheTextOnTheOffset()
        {
            var (canvas, path) = LinePath(0, 100, 190, 100);
            using var _ = canvas;
            var measure = new PathMeasure(path);

            var glyphs = PathText.Layout(Face, Size, "Centre", measure, new PathTextOptions(StartOffset: 95, Anchor: PathTextAnchor.Middle));
            var width = glyphs.Sum(g => g.Advance);

            Assert.Equal(95 - width / 2, glyphs[0].Transform.M31, 3);
        }

        [Fact]
        public void Layout_EndAnchor_EndsTheTextAtTheOffset()
        {
            var (canvas, path) = LinePath(0, 100, 190, 100);
            using var _ = canvas;

            var glyphs = PathText.Layout(Face, Size, "End", new PathMeasure(path), new PathTextOptions(StartOffset: 150, Anchor: PathTextAnchor.End));

            Assert.Equal(150, glyphs[^1].Transform.M31 + glyphs[^1].Advance, 3);
        }

        [Fact]
        public void Layout_RightSide_ReadsThePathFromItsEndUpsideDown()
        {
            var (canvas, path) = LinePath(0, 100, 190, 100);
            using var _ = canvas;

            var glyph = PathText.Layout(Face, Size, "A", new PathMeasure(path), new PathTextOptions(Side: PathTextSide.Right)).Single();

            // Turned half a turn: the x-axis points back along the path, and the glyph starts at its far end.
            Assert.Equal(-1, glyph.Transform.M11, 4);
            Assert.True(glyph.Transform.M31 > 150);
        }

        [Fact]
        public void Layout_LetterSpacing_AddsToEveryAdvance()
        {
            var (canvas, path) = LinePath(0, 100, 190, 100);
            using var _ = canvas;
            var measure = new PathMeasure(path);

            var tight = PathText.Layout(Face, Size, "Spacing", measure);
            var loose = PathText.Layout(Face, Size, "Spacing", measure, new PathTextOptions(LetterSpacing: 3));

            Assert.Equal(tight.Sum(g => g.Advance) + 3 * tight.Count, loose.Sum(g => g.Advance), 3);
        }

        [Fact]
        public void Layout_EmptyTextOrPath_HasNoGlyphs()
        {
            var (canvas, path) = LinePath(0, 0, 100, 0);
            using var _ = canvas;
            using var empty = canvas.GetGraphicsPath();

            Assert.Empty(PathText.Layout(Face, Size, "", new PathMeasure(path)));
            Assert.Empty(PathText.Layout(Face, Size, "Text", new PathMeasure(empty)));
        }

        [Fact]
        public void GetGlyphFrame_NormalOffset_MovesThePointPerpendicularToThePath()
        {
            var (canvas, path) = LinePath(0, 100, 190, 100);
            using var _ = canvas;

            var frame = PathText.GetGlyphFrame(new PathMeasure(path), 50, normalOffset: 5)!.Value;

            Assert.Equal(50, frame.M31, 4);
            Assert.Equal(105, frame.M32, 4);
        }

        [Fact]
        public void GetGlyphFrame_ExtraRotation_TurnsTheGlyph()
        {
            var (canvas, path) = LinePath(0, 100, 190, 100);
            using var _ = canvas;

            var frame = PathText.GetGlyphFrame(new PathMeasure(path), 50, rotationDegrees: 90)!.Value;

            Assert.Equal(0, frame.M11, 4);
            Assert.Equal(1, frame.M12, 4);
        }

        [Fact]
        public void GetGlyphFrame_OffThePath_IsNull()
        {
            var (canvas, path) = LinePath(0, 100, 190, 100);
            using var _ = canvas;
            var measure = new PathMeasure(path);

            Assert.Null(PathText.GetGlyphFrame(measure, -1));
            Assert.Null(PathText.GetGlyphFrame(measure, 191));
            Assert.Null(PathText.GetGlyphFrame(measure, -1, PathTextSide.Right));
        }

        [Fact]
        public void DrawStringAlongPath_PutsInkAlongTheCurve()
        {
            using var canvas = new RasterRenderContext().CreateCanvas(220, 140);
            using var path = canvas.GetGraphicsPath();
            path.Start(10, 120);
            path.AddBezierTo(10, 20, 210, 20, 210, 120);

            canvas.DrawStringAlongPath("Follow the curve", Face, 18, PaintColor.Black, path);

            var buffer = canvas.ToPixelBuffer();
            var span = buffer.PremultipliedRgba.Span;
            int ink = 0, inkHigh = 0;
            for (var y = 0; y < buffer.Height; y++)
            {
                for (var x = 0; x < buffer.Width; x++)
                {
                    if (span[(y * buffer.Width + x) * 4 + 3] == 0)
                        continue;

                    ink++;
                    if (y < 70)
                        inkHigh++;
                }
            }

            // Text on the arch rises into the upper half of the canvas; set on a flat baseline at y=120 it never would.
            Assert.True(ink > 200);
            Assert.True(inkHigh > 50);
        }

        [Fact]
        public void PathText_ValidatesItsArguments()
        {
            var (canvas, path) = LinePath(0, 0, 10, 0);
            using var _ = canvas;
            var measure = new PathMeasure(path);

            Assert.Throws<ArgumentNullException>(() => PathText.GetGlyphFrame(null!, 0));
            Assert.Throws<ArgumentNullException>(() => PathText.Layout(null!, 10, "a", measure));
            Assert.Throws<ArgumentNullException>(() => PathText.Layout(Face, 10, null!, measure));
            Assert.Throws<ArgumentNullException>(() => PathText.Layout(Face, 10, "a", null!));
            Assert.Throws<ArgumentNullException>(() => PathText.DrawStringAlongPath(null!, "a", Face, 10, PaintColor.Black, path));
            Assert.Throws<ArgumentNullException>(() => canvas.DrawStringAlongPath("a", Face, 10, PaintColor.Black, null!));
        }
    }
}
