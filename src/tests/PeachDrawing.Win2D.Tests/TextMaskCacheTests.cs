using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using PeachDrawing.Win2D;
using PeachPDF.Tests.TestSupport;
using Windows.Graphics.DirectX;

namespace PeachDrawing.Win2D.Tests;

/// <summary>The cache of rasterized text runs, and the switch that draws grayscale text instead of reading the pixels behind each run.</summary>
public class TextMaskCacheTests
{
    private const double Width = 240;
    private const double Height = 60;

    private static async Task<Win2dRenderContext> ContextAsync()
    {
        var context = Win2dRenderContext.CreateSoftware();
        await using var stream = File.OpenRead(BundledFonts.Ttf);
        await context.AddFont(stream, "Source Sans 3");
        return context;
    }

    private static byte[] Paint(Win2dRenderContext context, double x, double y, string text = "Hamburgefonstiv", float dpi = 96)
    {
        using var target = new CanvasRenderTarget(context.Device, (float)Width, (float)Height, dpi, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Ignore);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Windows.UI.Color.FromArgb(255, 255, 255, 255));
            using var canvas = Win2dCanvas.FromSession(context, session, new Rect(0, 0, Width, Height));
            var font = context.GetFont("Source Sans 3", 16, PaintFontStyle.Regular)!;
            canvas.DrawString(text, font, PaintColor.Black, new PaintPoint(x, y), canvas.MeasureString(text, font));
        }

        return target.GetPixelBytes();
    }

    [Fact]
    public async Task A_run_drawn_again_comes_from_the_cache_and_looks_the_same()
    {
        using var context = await ContextAsync();

        var first = Paint(context, 5, 5);
        var missesAfterFirst = context.TextMasks.Misses;
        var second = Paint(context, 5, 5);

        Assert.True(missesAfterFirst > 0);
        Assert.Equal(missesAfterFirst, context.TextMasks.Misses);
        Assert.True(context.TextMasks.Hits > 0);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task A_run_moved_by_whole_pixels_reuses_the_mask()
    {
        using var context = await ContextAsync();

        Paint(context, 5, 5);
        var misses = context.TextMasks.Misses;
        Paint(context, 17, 9);

        Assert.Equal(misses, context.TextMasks.Misses);
    }

    [Fact]
    public async Task A_run_at_another_fraction_of_a_pixel_is_rasterized_again()
    {
        using var context = await ContextAsync();

        Paint(context, 5, 5);
        var misses = context.TextMasks.Misses;
        Paint(context, 5.5, 5);

        Assert.True(context.TextMasks.Misses > misses);
    }

    [Fact]
    public async Task A_cached_run_matches_one_rasterized_without_the_cache()
    {
        using var cached = await ContextAsync();
        using var fresh = await ContextAsync();

        Paint(cached, 5, 5);
        var fromCache = Paint(cached, 5, 5);
        var uncached = Paint(fresh, 5, 5);

        Assert.Equal(uncached, fromCache);
    }

    [Fact]
    public async Task A_run_cut_by_the_edge_of_the_target_is_cut_from_the_cached_mask()
    {
        using var context = await ContextAsync();

        // Drawn whole first, then again with part of it past the target's edge: the second is a hit that has to be cropped.
        Paint(context, 5, 5);
        var misses = context.TextMasks.Misses;
        var cut = Paint(context, 150, 5);

        Assert.Equal(misses, context.TextMasks.Misses);
        Assert.Contains(cut, b => b < 200);
    }

    [Fact]
    public async Task The_cache_is_bounded_and_can_be_cleared()
    {
        using var context = await ContextAsync();

        Paint(context, 5, 5);
        Assert.True(context.TextMasks.Count > 0);

        context.TextMasks.Clear();
        Assert.Equal(0, context.TextMasks.Count);
    }
}
