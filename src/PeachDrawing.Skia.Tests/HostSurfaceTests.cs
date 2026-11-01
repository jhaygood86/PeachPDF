using PeachDrawing.Core;
using PeachDrawing.Skia;
using SkiaSharp;
using System.Numerics;

namespace PeachDrawing.Skia.Tests;

/// <summary>A canvas over a surface the host supplied: borrowed, never disposed, and put back as it was found.</summary>
public class HostSurfaceTests
{
    private static SKSurface HostSurface(int size = 40) =>
        SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));

    private static SKColor PixelOf(SKSurface surface, int x, int y)
    {
        using var snapshot = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(snapshot);
        return bitmap.GetPixel(x, y);
    }

    [Fact]
    public void A_canvas_draws_into_the_hosts_canvas_and_leaves_it_usable()
    {
        using var surface = HostSurface();
        using var context = new SkiaRenderContext();

        var canvas = SkiaCanvas.FromCanvas(context, surface.Canvas, new Rect(0, 0, 40, 40));
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 20, 40);
        canvas.Dispose();

        // The host's canvas is still alive and draws on.
        using var paint = new SKPaint { Color = SKColors.Blue };
        surface.Canvas.DrawRect(20, 0, 20, 40, paint);

        Assert.Equal(SKColors.Red, PixelOf(surface, 5, 5));
        Assert.Equal(SKColors.Blue, PixelOf(surface, 30, 5));
    }

    [Fact]
    public void Disposing_restores_the_hosts_matrix_clip_and_save_level()
    {
        using var surface = HostSurface();
        using var context = new SkiaRenderContext();
        surface.Canvas.Translate(3, 4);
        var matrix = surface.Canvas.TotalMatrix;
        var saves = surface.Canvas.SaveCount;

        var canvas = SkiaCanvas.FromCanvas(context, surface.Canvas, new Rect(0, 0, 40, 40));
        canvas.PushTransform(Matrix3x2.CreateScale(2, 2));
        canvas.PushClip(new Rect(0, 0, 5, 5));
        canvas.PushBlendMode(PaintBlendMode.Multiply);

        // Left pushed on purpose: dispose pops them.
        canvas.Dispose();

        Assert.Equal(matrix, surface.Canvas.TotalMatrix);
        Assert.Equal(saves, surface.Canvas.SaveCount);
        Assert.True(surface.Canvas.DeviceClipBounds.Width >= 37);
    }

    [Fact]
    public void A_canvas_draws_under_a_matrix_the_host_already_set()
    {
        using var surface = HostSurface(80);
        using var context = new SkiaRenderContext();
        surface.Canvas.Scale(2, 2);

        using (var canvas = SkiaCanvas.FromCanvas(context, surface.Canvas, new Rect(0, 0, 40, 40)))
            canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 10, 10, 10, 10);

        Assert.Equal(SKColors.Red, PixelOf(surface, 30, 30));
        Assert.Equal(0, PixelOf(surface, 10, 10).Alpha);
    }

    [Fact]
    public void Links_are_hit_tested_in_layout_units_or_in_the_hosts_own_pixels()
    {
        using var surface = HostSurface(80);
        using var context = new SkiaRenderContext();
        surface.Canvas.Scale(2, 2);

        using var canvas = SkiaCanvas.FromCanvas(context, surface.Canvas, new Rect(0, 0, 40, 40));
        canvas.AddLink(new Rect(10, 10, 10, 10), "https://example.com/");

        Assert.Same(canvas.Links.Links[0], canvas.Links.HitTest(15, 15));
        Assert.Null(canvas.Links.HitTest(30, 30));

        // The host's own coordinates are the surface's pixels: layout (15, 15) is device pixel (30, 30).
        Assert.Same(canvas.Links.Links[0], canvas.Links.HitTestDevice(30, 30));
        Assert.Null(canvas.Links.HitTestDevice(15, 15));
    }

    [Fact]
    public void A_picture_recorders_canvas_is_as_good_a_target_as_a_surface()
    {
        using var context = new SkiaRenderContext();
        using var recorder = new SKPictureRecorder();
        var recording = recorder.BeginRecording(SKRect.Create(0, 0, 40, 40));

        using (var canvas = SkiaCanvas.FromCanvas(context, recording, new Rect(0, 0, 40, 40)))
            canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 40, 40);

        using var picture = recorder.EndRecording();
        Assert.True(picture.ApproximateOperationCount > 0);
    }

    [Fact]
    public void The_composer_borrows_the_surface_and_leaves_its_canvas_as_it_found_it()
    {
        using var surface = HostSurface();
        using var context = new SkiaRenderContext();
        var saves = surface.Canvas.SaveCount;

        using (var composer = new SkiaComposer(context, surface, 20, 20))
        {
            composer.Canvas.DrawRectangle(composer.Canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 10, 20);
            composer.Finish();
            Assert.Equal(saves, surface.Canvas.SaveCount);
        }

        // 20-point page on a 40-pixel surface: scaled by 2.
        Assert.Equal(SKColors.Red, PixelOf(surface, 15, 15));
        Assert.Equal(0, PixelOf(surface, 25, 15).Alpha);
    }

    [Fact]
    public void The_composer_can_clear_to_a_background_or_leave_the_surface_alone()
    {
        using var cleared = Scene.Create(options: new SkiaComposerOptions { Background = PaintColor.FromArgb(255, 0, 255, 0) });
        Assert.Equal((0, 255, 0, 255), cleared.Px(5, 5));

        using var left = HostSurface();
        left.Canvas.Clear(SKColors.Blue);
        using var context = new SkiaRenderContext();
        using (var composer = new SkiaComposer(context, left, 40, 40, new SkiaComposerOptions { Background = null }))
            composer.Finish();

        Assert.Equal(SKColors.Blue, PixelOf(left, 5, 5));
    }

    [Fact]
    public void The_composer_saves_the_drawing_as_an_image_file()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);

        foreach (var format in Enum.GetValues<SkiaImageFormat>())
        {
            using var stream = new MemoryStream();
            scene.Composer.Save(stream, format, 80);
            Assert.True(stream.Length > 20, format.ToString());
        }
    }

    [Fact]
    public void A_page_of_no_size_is_refused()
    {
        using var surface = HostSurface();
        using var context = new SkiaRenderContext();

        Assert.Throws<ArgumentOutOfRangeException>(() => new SkiaComposer(context, surface, 0, 10));
        Assert.Throws<ArgumentNullException>(() => new SkiaComposer(context, null!, 10, 10));
        Assert.Throws<ArgumentNullException>(() => SkiaCanvas.FromCanvas(context, null!, new Rect(0, 0, 1, 1)));
    }
}
