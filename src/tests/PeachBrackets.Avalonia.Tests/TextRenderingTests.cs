using Avalonia.Media;
using PeachBrackets.Avalonia.Tests.TestSupport;
using PeachDrawing.Skia;
using SkiaSharp;

namespace PeachBrackets.Avalonia.Tests;

public class TextRenderingTests
{
    private const string Page =
        "<html><body style='margin:0;background:#ffffff'><p style='margin:8px;font:16px sans-serif;color:#000000'>Hamburgefonts quick brown fox jumps over the lazy dog</p>"
        + "<div style='height:2400px'></div></body></html>";

    // The strongest disagreement between two color channels of one pixel in the top of the frame, where the text is.
    private static int Fringe(SKBitmap bitmap)
    {
        var most = 0;
        for (var y = 0; y < 60; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                most = Math.Max(most, Math.Max(Math.Abs(c.Red - c.Green), Math.Max(Math.Abs(c.Green - c.Blue), Math.Abs(c.Red - c.Blue))));
            }
        }

        return most;
    }

    private static int Ink(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < 60; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Red < 200 || c.Green < 200 || c.Blue < 200)
                    count++;
            }
        }

        return count;
    }

    // Paints the way the render thread does: from a thread of its own, onto a canvas of its own, with only what was recorded on the UI thread.
    private static async Task<SKBitmap> PaintAsync(HtmlView view, bool tiles)
    {
        view.TilesOnCpuForTests = tiles;
        var paint = view.CapturePaintForTests();
        var bitmap = new SKBitmap(400, 300);
        using var canvas = new SKCanvas(bitmap);
        await Task.Run(() => paint(canvas, 400, 300));
        return bitmap;
    }

    [Fact]
    public async Task TheDefaultIsAutoAndAChangeInvalidatesTheTiles()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 400, 300);
            Assert.Equal(SkiaTextRendering.Auto, view.TextRendering);
            Assert.Equal(SkiaTextRendering.Auto, HtmlView.TextRenderingProperty.GetDefaultValue(typeof(HtmlView)));

            var before = view.TilesGeneration;
            view.TextRendering = SkiaTextRendering.Lcd;
            Assert.True(view.TilesGeneration > before, "the tiles hold text drawn the old way");

            // The frame is painted with what the control said when it was recorded: the context takes the mode in under the lock, on the painting thread.
            using (var lcd = await PaintAsync(view, tiles: false))
                Assert.True(Fringe(lcd) > 40, $"strongest fringe {Fringe(lcd)}");

            view.TextRendering = SkiaTextRendering.Outline;
            using (var outline = await PaintAsync(view, tiles: false))
            {
                Assert.True(Ink(outline) > 200);
                Assert.True(Fringe(outline) <= 1);
            }

            window.Close();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LcdTextHasColoredFringesAndTheOtherModesDoNot(bool tiles)
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 400, 300, v => v.TextRendering = SkiaTextRendering.Lcd);

            using (var lcd = await PaintAsync(view, tiles))
            {
                Assert.True(Ink(lcd) > 200, "the page's text should be drawn");
                Assert.True(Fringe(lcd) > 40, $"LCD text should have colored fringes, strongest {Fringe(lcd)}");
            }

            foreach (var mode in new[] { SkiaTextRendering.Grayscale, SkiaTextRendering.Outline })
            {
                view.TextRendering = mode;
                using var plain = await PaintAsync(view, tiles);
                Assert.True(Ink(plain) > 200, $"{mode}: the page's text should be drawn");
                Assert.True(Fringe(plain) <= 1, $"{mode} text is neutral, strongest {Fringe(plain)}");
            }

            window.Close();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LcdTextIsNotUsedOverATransparentPageBackground(bool tiles)
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync("<html><body style='margin:0'><p style='margin:8px;font:16px sans-serif;color:#000000'>Hamburgefonts quick brown fox</p></body></html>", 400, 300,
                v =>
                {
                    v.TextRendering = SkiaTextRendering.Lcd;
                    v.PageBackground = Colors.Transparent;
                });

            using var bitmap = await PaintAsync(view, tiles);
            var alpha = 0;
            for (var y = 0; y < 60; y++)
            {
                for (var x = 0; x < 400; x++)
                {
                    var c = bitmap.GetPixel(x, y);
                    alpha = Math.Max(alpha, c.Alpha);

                    // Over nothing the text is black with a coverage in its alpha: its color channels are all the same, a per-channel coverage has no meaning there.
                    Assert.True(c.Red == c.Green && c.Green == c.Blue, $"colored fringe over a transparent backdrop at {x},{y}: {c}");
                }
            }

            Assert.True(alpha > 200, "the text should be drawn");
            window.Close();
        });
    }

    [Fact]
    public async Task AZoomedViewDrawsHintedMasksAtTheDeviceSize()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 400, 300, v => v.TextRendering = SkiaTextRendering.Lcd);
            view.SetZoom(1.5, new global::Avalonia.Point(0, 0));
            await HeadlessSession.PumpUntilAsync(() => view.Zoom == 1.5, 5000);

            // Zoomed by a non-integer scale the text is still drawn as masks (the view paints under a scale), with fringes.
            Exception? failure = null;
            view.DocumentFailed += (_, e) => failure = e.Exception;
            using var bitmap = await PaintAsync(view, tiles: true);
            await HeadlessSession.PumpUntilAsync(() => failure is not null, 300);
            Assert.True(failure is null, failure?.ToString());
            Assert.True(Ink(bitmap) > 300, $"ink {Ink(bitmap)} zoom {view.Zoom}");
            Assert.True(Fringe(bitmap) > 40, $"strongest fringe {Fringe(bitmap)}");
            window.Close();
        });
    }
}
