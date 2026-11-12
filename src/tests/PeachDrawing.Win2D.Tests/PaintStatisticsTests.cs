using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using PeachDrawing.Win2D;
using PeachImage.Formats.Jpeg;
using System.Collections.Concurrent;
using System.Numerics;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Win2D.Tests;

/// <summary>
/// The optional accounting of one paint (<c>PEACH_PAINT_STATS</c>): a line for each top-level canvas disposed, with what it flushed, clipped and drew, and a line about
/// what the render context holds on to. They are written to a sink (or the debugger and a file); these tests give them a sink.
/// </summary>
[Collection("Win2dPaintStatistics")]
public class PaintStatisticsTests
{
    private static byte[] LargeJpeg(int size = 800)
    {
        var raster = new RasterRenderContext();
        using var canvas = raster.CreateCanvas(size, size);
        canvas.DrawRectangle(raster.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(size, size),
            [(PaintColor.FromArgb(255, 255, 0, 0), 0), (PaintColor.FromArgb(255, 0, 0, 255), 1)]), 0, 0, size, size);
        using var stream = new MemoryStream();
        canvas.Save(stream, "jpeg", new JpegEncoderOptions());
        return stream.ToArray();
    }

    private static async Task<ConcurrentQueue<string>> CaptureAsync(Func<Task> work)
    {
        var lines = new ConcurrentQueue<string>();
        Win2dPaintStatistics.Sink = lines.Enqueue;
        Win2dPaintStatistics.Enabled = true;
        try
        {
            await work();
        }
        finally
        {
            Win2dPaintStatistics.Enabled = false;
            Win2dPaintStatistics.Sink = null;
        }

        return lines;
    }

    [Fact]
    public async Task APaintThatClipsDrawsAndLoadsAnImage_WritesALineOfWhatItDid_AndOneOfWhatTheContextHolds()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        await using (var stream = File.OpenRead(BundledFonts.Ttf))
            await context.AddFont(stream, "Source Sans 3");
        context.TextRendering = Win2dTextRendering.Lcd;

        var lines = await CaptureAsync(() =>
        {
            using var scene = new Scene(context, 120, 60);
            using var image = context.ImageFromStream(new MemoryStream(LargeJpeg()));
            using var path = scene.Canvas.GetGraphicsPath();
            path.Start(5, 5);
            path.LineTo(115, 5);
            path.LineTo(115, 55);
            path.LineTo(5, 55);
            path.CloseFigure();
            scene.Canvas.PushClip(new Rect(0, 0, 120, 60));
            scene.Canvas.PushClip(path);
            scene.Canvas.DrawImage(image, new Rect(10, 10, 40, 40));
            scene.Canvas.DrawString("Hello", context.GetFont("Source Sans 3", 16, PaintFontStyle.Regular)!, PaintColor.Black, new PaintPoint(60, 10), new Size(50, 20));
            scene.Canvas.PopClip();
            scene.Canvas.PopClip();
            scene.Canvas.Dispose();

            // Written about once a second as paints end; asked for here so the test does not depend on when the last one was.
            Win2dPaintStatistics.NoteMemory(context, force: true);
            return Task.CompletedTask;
        });

        Assert.Contains(lines, l => l.StartsWith("PEACH_PAINT_STATS(win2d)") && l.Contains("RectClip="));
        var memory = lines.Last(l => l.StartsWith("PEACH_PAINT_STATS(memory)"));
        Assert.Contains("lcdScratch=", memory);
        Assert.Contains("imageBitmaps=", memory);
        Assert.Contains("surfacesMade=", memory);
    }

    [Fact]
    public async Task WithoutASink_ALineGoesToTheDebuggerAndTheFileTheEnvironmentNames()
    {
        var file = Path.Combine(Path.GetTempPath(), $"win2d-paint-stats-{Guid.NewGuid():N}.txt");
        Environment.SetEnvironmentVariable("PEACH_PAINT_STATS_FILE", file);
        try
        {
            Win2dPaintStatistics.Write("to the file");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PEACH_PAINT_STATS_FILE", null);
        }

        Assert.Contains("to the file", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
        File.Delete(file);
    }
}
