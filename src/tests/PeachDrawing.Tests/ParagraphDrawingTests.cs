using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Layout;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Tests
{
    /// <summary>Drawing a <c>PeachDrawing.Text</c> paragraph onto a canvas: the pixels must land where the layout says the text is.</summary>
    public class ParagraphDrawingTests
    {
        private const double Size = 20;
        private static readonly PaintColor Black = PaintColor.FromArgb(255, 0, 0, 0);
        private static readonly PaintColor Red = PaintColor.FromArgb(255, 255, 0, 0);
        private static readonly PaintColor Blue = PaintColor.FromArgb(255, 0, 0, 255);

        private static readonly Typeface Face = Load(BundledFonts.Ttf);

        private static Typeface Load(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "ParagraphDrawing-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static RasterCanvas NewCanvas(int width, int height) => new RasterRenderContext().CreateCanvas(width, height);

        private static ParagraphLayout Layout(string text, double width) =>
            new ParagraphBuilder(new RunStyle(Face, Size)).AddText(text).Build().Layout(width);

        /// <summary>The pixel rectangle (left, top, right, bottom exclusive) holding pixels for which <paramref name="test"/> is true; null when none.</summary>
        private static (int Left, int Top, int Right, int Bottom)? Ink(RasterCanvas canvas, Func<byte, byte, byte, byte, bool>? test = null)
        {
            var buffer = canvas.ToPixelBuffer();
            var span = buffer.PremultipliedRgba.Span;
            int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
            for (int y = 0; y < buffer.Height; y++)
            {
                for (int x = 0; x < buffer.Width; x++)
                {
                    var p = span.Slice((y * buffer.Width + x) * 4, 4);
                    if (test is null ? p[3] > 0 : test(p[0], p[1], p[2], p[3]))
                    {
                        left = Math.Min(left, x);
                        top = Math.Min(top, y);
                        right = Math.Max(right, x + 1);
                        bottom = Math.Max(bottom, y + 1);
                    }
                }
            }

            return right < 0 ? null : (left, top, right, bottom);
        }

        [Fact]
        public void DrawParagraph_PutsEveryLinesInkInsideThatLinesBounds()
        {
            var layout = Layout("The quick brown fox jumps over the lazy dog", 150);
            Assert.True(layout.Lines.Count >= 2);
            var origin = new PaintPoint(12, 7);

            foreach (var line in layout.Lines)
            {
                using var canvas = NewCanvas(200, 200);
                var single = new ParagraphPaint(Black);
                canvas.DrawParagraph(layout, origin, run => run.Baseline == line.Baseline ? single : new ParagraphPaint(PaintColor.FromArgb(0, 0, 0, 0)));

                var ink = Ink(canvas);
                Assert.NotNull(ink);
                var (l, t, r, b) = ink.Value;
                Assert.True(l >= origin.X + line.Bounds.Left - 1, $"left {l} vs {origin.X + line.Bounds.Left}");
                Assert.True(r <= origin.X + line.Bounds.Right + 1, $"right {r} vs {origin.X + line.Bounds.Right}");
                Assert.True(t >= origin.Y + line.Bounds.Top - 1, $"top {t} vs {origin.Y + line.Bounds.Top}");
                Assert.True(b <= origin.Y + line.Bounds.Bottom + 1, $"bottom {b} vs {origin.Y + line.Bounds.Bottom}");
            }
        }

        [Fact]
        public void DrawParagraph_MovesTheInkByExactlyTheOriginOffset()
        {
            var layout = Layout("Offset", 200);
            using var a = NewCanvas(120, 60);
            using var b = NewCanvas(120, 60);

            a.DrawParagraph(layout, new PaintPoint(10, 10), Black);
            b.DrawParagraph(layout, new PaintPoint(31, 17), Black);

            var inkA = Ink(a)!.Value;
            var inkB = Ink(b)!.Value;
            Assert.Equal(21, inkB.Left - inkA.Left);
            Assert.Equal(7, inkB.Top - inkA.Top);
            Assert.Equal(inkA.Right - inkA.Left, inkB.Right - inkB.Left);
        }

        [Fact]
        public void DrawParagraph_PaintsEachRunInTheColorTheCallbackGives()
        {
            var layout = new ParagraphBuilder(new RunStyle(Face, Size))
                .AddText("left ")
                .PushRun(new RunStyle(Face, Size + 1)).AddText("right").PopRun()
                .Build().Layout(300);
            var rightRun = layout.Lines.SelectMany(l => l.Runs).Single(r => r.Style.Size == Size + 1);
            using var canvas = NewCanvas(300, 60);

            canvas.DrawParagraph(layout, new PaintPoint(0, 5), run => new ParagraphPaint(run.Style.Size == Size + 1 ? Blue : Red));

            var blue = Ink(canvas, (r, g, b, a) => b > 200 && r < 50)!.Value;
            var red = Ink(canvas, (r, g, b, a) => r > 200 && b < 50)!.Value;
            Assert.True(blue.Left >= rightRun.X - 1);
            Assert.True(red.Right <= rightRun.X + 1);
        }

        [Fact]
        public void DrawParagraph_DrawsAnUnderlineAtTheFontsPositionAcrossTheRun()
        {
            var layout = Layout("mmm", 200);
            var run = layout.Lines[0].Runs[0];
            var metrics = Face.Metrics;
            using var plain = NewCanvas(100, 60);
            using var underlined = NewCanvas(100, 60);

            plain.DrawParagraph(layout, new PaintPoint(10, 10), Black);
            underlined.DrawParagraph(layout, new PaintPoint(10, 10), _ => new ParagraphPaint(Black, TextDecorations.Underline));

            // Rows the plain text does not touch but the underlined text does: the line.
            var extra = Ink(underlined)!.Value.Bottom - Ink(plain)!.Value.Bottom;
            var lineY = 10 + run.Baseline - metrics.UnderlinePosition * Size / metrics.UnitsPerEm;
            var inked = Ink(underlined, (r, g, b, a) => a > 128)!.Value;
            Assert.True(inked.Bottom >= lineY - 1);
            Assert.True(extra >= 0);

            // The line spans the whole run: sample its row at both ends.
            var buffer = underlined.ToPixelBuffer();
            var row = (int)Math.Round(lineY);
            foreach (var x in new[] { (int)Math.Ceiling(10 + run.X + 1), (int)(10 + run.X + run.Width - 2) })
                Assert.True(buffer.PremultipliedRgba.Span[(row * buffer.Width + x) * 4 + 3] > 0, $"no underline at x={x}, row {row}");
        }

        [Fact]
        public void DrawParagraph_HandsInlineBoxesToTheCallbackWithTheirBoundsMovedByTheOrigin()
        {
            var layout = new ParagraphBuilder(new RunStyle(Face, Size)).AddText("a ").AddInlineBox(new InlineBox(30, 12)).AddText(" b").Build().Layout(300);
            var boxRun = layout.Lines[0].Runs.Single(r => r.InlineBox is not null);
            using var canvas = NewCanvas(300, 60);
            Rect? seen = null;

            canvas.DrawParagraph(layout, new PaintPoint(5, 9), _ => new ParagraphPaint(Black), (c, run, bounds) =>
            {
                Assert.Same(boxRun, run);
                seen = bounds;
                c.DrawRectangle(c.GetSolidBrush(Red), bounds.X, bounds.Y, bounds.Width, bounds.Height);
            });

            Assert.NotNull(seen);
            Assert.Equal(5 + boxRun.InlineBoxBounds.X, seen.Value.X, 6);
            Assert.Equal(9 + boxRun.InlineBoxBounds.Y, seen.Value.Y, 6);
            Assert.Equal(30, seen.Value.Width, 6);
            Assert.NotNull(Ink(canvas, (r, g, b, a) => r > 200 && b < 50));
        }

        [Fact]
        public void DrawGlyphRun_DrawsTheShapedGlyphsWithoutReshaping()
        {
            var run = PeachDrawing.Text.Shaping.Shaper.Shape(Face, "Hi", PeachDrawing.Text.Shaping.ShapeSettings.Default);
            using var canvas = NewCanvas(80, 50);

            canvas.DrawGlyphRun(run, Size, new PaintPoint(6, 30), Black);

            var ink = Ink(canvas)!.Value;
            Assert.True(ink.Left >= 5);
            Assert.True(ink.Bottom <= 32);
            Assert.True(ink.Top < 30);
        }
    }
}
