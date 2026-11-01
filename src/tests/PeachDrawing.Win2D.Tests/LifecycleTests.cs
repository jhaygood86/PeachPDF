using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using PeachDrawing.Win2D;
using System.Numerics;

namespace PeachDrawing.Win2D.Tests;

/// <summary>Every native resource a canvas, a tile, a layer or a composer makes is released, however the drawing ends.</summary>
public class LifecycleTests
{
    [Fact]
    public void Clips_hold_a_layer_until_popped()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushClip(new Rect(0, 0, 30, 30));
        scene.Canvas.PushClipExclude(new Rect(5, 5, 5, 5));
        Assert.Equal(2, scene.Context.Counts.Layers);

        scene.Canvas.PopClip();
        Assert.Equal(1, scene.Context.Counts.Layers);
        scene.Canvas.PopClip();
        Assert.Equal(0, scene.Context.Counts.Layers);
    }

    [Fact]
    public void Disposing_a_canvas_pops_whatever_is_still_pushed()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushClip(new Rect(0, 0, 30, 30));
        scene.Canvas.PushTransform(Matrix3x2.CreateTranslation(5, 5));
        scene.Canvas.PushClip(new Rect(0, 0, 10, 10));
        scene.Canvas.PushBlendMode(PaintBlendMode.Plus);
        Assert.Equal(2, scene.Context.Counts.Layers);

        scene.Canvas.Dispose();
        Assert.Equal(0, scene.Context.Counts.Total);
    }

    [Fact]
    public void Suspending_and_resuming_clipping_round_trips_the_clips()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushClip(new Rect(0, 0, 30, 30));
        scene.Canvas.PushClip(new Rect(0, 0, 20, 20));

        scene.Canvas.SuspendClipping();
        Assert.Equal(0, scene.Context.Counts.Layers);
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);

        scene.Canvas.ResumeClipping();
        Assert.Equal(2, scene.Context.Counts.Layers);
        scene.Canvas.PopClip();
        scene.Canvas.PopClip();
        Assert.Equal(0, scene.Context.Counts.Layers);
        Assert.True(scene.IsRed(35, 35));
    }

    [Fact]
    public void Popping_with_nothing_pushed_is_an_error_and_leaves_the_canvas_usable()
    {
        using var scene = Scene.Create();
        Assert.Throws<InvalidOperationException>(scene.Canvas.PopClip);
        Assert.Throws<InvalidOperationException>(scene.Canvas.PopTransform);
        Assert.Throws<InvalidOperationException>(scene.Canvas.PopBlendMode);

        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);
        Assert.True(scene.IsRed(5, 5));
    }

    [Fact]
    public void A_disposed_canvas_refuses_further_drawing_and_disposes_again_harmlessly()
    {
        using var scene = Scene.Create();
        scene.Canvas.Dispose();
        scene.Canvas.Dispose();

        Assert.Throws<ObjectDisposedException>(() => scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 1, 1));
        Assert.Throws<ObjectDisposedException>(() => scene.Canvas.PushClip(new Rect(0, 0, 1, 1)));
        Assert.Throws<ObjectDisposedException>(() => scene.Canvas.CreateTile(1, 1));
        Assert.Throws<ObjectDisposedException>(() => scene.Canvas.BeginLayer(new LayerOptions(Opacity: 1)));
    }

    [Fact]
    public void Tiles_are_counted_until_their_canvas_and_image_are_disposed()
    {
        using var scene = Scene.Create();
        var (tile, image) = scene.Canvas.CreateTile(10, 10)!.Value;
        Assert.Equal(1, scene.Context.Counts.Tiles);
        Assert.Equal(1, scene.Context.Counts.CommandLists);
        Assert.Equal(2, scene.Context.Counts.Sessions);

        tile.Dispose();
        Assert.Equal(0, scene.Context.Counts.Tiles);
        Assert.Equal(1, scene.Context.Counts.CommandLists);

        image.Dispose();
        image.Dispose();
        Assert.Equal(0, scene.Context.Counts.Total - 1);
    }

    [Fact]
    public void Disposing_a_tile_image_closes_a_tile_canvas_that_is_still_open()
    {
        using var scene = Scene.Create();
        var (_, image) = scene.Canvas.CreateTile(10, 10)!.Value;
        image.Dispose();

        Assert.Equal(0, scene.Context.Counts.Tiles);
        Assert.Equal(0, scene.Context.Counts.CommandLists);
        Assert.Equal(1, scene.Context.Counts.Sessions);
    }

    [Fact]
    public void A_layer_not_finished_by_the_time_the_canvas_is_disposed_is_abandoned_cleanly()
    {
        using var scene = Scene.Create();
        var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.5))!;
        layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);
        Assert.Equal(1, scene.Context.Counts.CommandLists);

        scene.Canvas.Dispose();
        Assert.Equal(0, scene.Context.Counts.Total);

        // Finishing it afterwards is harmless.
        layer.Dispose();
        Assert.Equal(0, scene.Context.Counts.Total);
    }

    [Fact]
    public void Layers_release_their_resources_when_finished_and_when_finished_twice()
    {
        using var scene = Scene.Create();
        var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.5, Effects: new LayerEffect[] { new BlurEffect(2) }))!;
        layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 5, 5, 10, 10);
        layer.Dispose();
        layer.Dispose();

        Assert.Equal(0, scene.Context.Counts.Total - 1);
    }

    [Fact]
    public void An_exception_while_drawing_does_not_leak_what_was_pushed()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushClip(new Rect(0, 0, 30, 30));

        // An image that can neither be drawn nor converted.
        Assert.Throws<NotSupportedException>(() => scene.Canvas.DrawImage(new PixellessImage(), new Rect(0, 0, 5, 5)));

        scene.Canvas.Dispose();
        Assert.Equal(0, scene.Context.Counts.Total);
    }

    [Fact]
    public void A_borrowed_session_is_never_closed_and_gets_its_state_back()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        using var target = new CanvasRenderTarget(context.Device, 40, 40, 96);
        using (var session = target.CreateDrawingSession())
        {
            var transform = Matrix3x2.CreateTranslation(3, 0);
            session.Transform = transform;
            session.Antialiasing = CanvasAntialiasing.Aliased;

            using (var canvas = Win2dCanvas.FromSession(context, session, new Rect(0, 0, 40, 40)))
            {
                canvas.PushTransform(Matrix3x2.CreateScale(2));
                canvas.PushBlendMode(PaintBlendMode.Plus);
                canvas.PushAntiAlias(true);
                canvas.PushClip(new Rect(0, 0, 10, 10));
                canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 5, 5);
            }

            // The caller's session is exactly as it was and still draws.
            Assert.Equal(transform, session.Transform);
            Assert.Equal(CanvasBlend.SourceOver, session.Blend);
            Assert.Equal(CanvasAntialiasing.Aliased, session.Antialiasing);
            session.FillRectangle(20, 20, 5, 5, Windows.UI.Color.FromArgb(255, 0, 0, 255));
            Assert.Equal(0, context.Counts.Total);
        }

        var pixels = target.GetPixelBytes();
        Assert.True(Px(pixels, 40, 5 + 3, 5).Red > 200);
        Assert.True(Px(pixels, 40, 25, 22).Blue > 200);
    }

    [Fact]
    public void A_borrowed_device_outlives_its_context_and_an_owned_one_does_not()
    {
        using var device = new CanvasDevice(true);
        var borrowing = new Win2dRenderContext(device);
        Assert.Same(device, borrowing.Device);
        borrowing.Dispose();
        borrowing.Dispose();

        // The caller's device still works.
        using (var target = new CanvasRenderTarget(device, 4, 4, 96))
            Assert.Equal(4, (int)target.SizeInPixels.Width);

        var owning = Win2dRenderContext.CreateSoftware();
        var owned = owning.Device;
        owning.Dispose();
        Assert.Throws<ObjectDisposedException>(() => owning.Device);
        Assert.ThrowsAny<Exception>(() => new CanvasRenderTarget(owned, 4, 4, 96));
    }

    [Fact]
    public void The_composer_finishes_once_and_refuses_drawing_afterwards()
    {
        using var scene = Scene.Create();
        scene.Canvas.PushClip(new Rect(0, 0, 10, 10));
        scene.Composer.Finish();
        scene.Composer.Finish();

        Assert.Equal(0, scene.Context.Counts.Total);
        Assert.Throws<ObjectDisposedException>(() => scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 1, 1));
        _ = scene.Composer.ToPixelBuffer();
    }

    [Fact]
    public void Disposing_a_composer_without_finishing_releases_everything()
    {
        var context = Win2dRenderContext.CreateSoftware();
        var composer = new Win2dComposer(context, 20, 20);
        composer.Canvas.PushClip(new Rect(0, 0, 5, 5));
        composer.Dispose();
        composer.Dispose();

        Assert.Equal(0, context.Counts.Total);
        Assert.Throws<ObjectDisposedException>(composer.Finish);
        context.Dispose();
    }

    [Fact]
    public void A_composer_refuses_an_empty_page()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        Assert.Throws<ArgumentOutOfRangeException>(() => new Win2dComposer(context, 0, 10));
        Assert.Throws<ArgumentNullException>(() => new Win2dComposer(null!, 10, 10));
    }

    [Fact]
    public void Images_made_by_a_context_survive_being_drawn_on_two_devices()
    {
        using var first = Scene.Create();
        using var second = Scene.Create();
        using var image = first.Context.ImageFromStream(RedPng());

        first.Canvas.DrawImage(image, new Rect(0, 0, 10, 10));
        second.Canvas.DrawImage(image, new Rect(0, 0, 10, 10));

        Assert.True(first.IsRed(5, 5));
        Assert.True(second.IsRed(5, 5));
    }

    [Fact]
    public void A_disposed_image_cannot_be_drawn()
    {
        using var scene = Scene.Create();
        var image = scene.Context.ImageFromStream(RedPng());
        scene.Canvas.DrawImage(image, new Rect(0, 0, 10, 10));
        image.Dispose();
        image.Dispose();

        Assert.Throws<ObjectDisposedException>(() => scene.Canvas.DrawImage(image, new Rect(0, 0, 10, 10)));
    }

    [Fact]
    public void Hatch_brushes_release_the_tile_they_make()
    {
        using var scene = Scene.Create();
        var hatch = new HatchBrush(HatchStyle.Cross, PaintColor.FromArgb(255, 255, 0, 0), PaintColor.White);
        scene.Canvas.DrawRectangle(hatch, 0, 0, 40, 40);

        Assert.Equal(0, scene.Context.Counts.Tiles);
        Assert.Equal(0, scene.Context.Counts.CommandLists);
        Assert.True(scene.OpaquePixels() > 0);
    }

    private static (int Red, int Green, int Blue) Px(byte[] bgra, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return (bgra[i + 2], bgra[i + 1], bgra[i]);
    }

    private static MemoryStream RedPng()
    {
        var raster = new RasterRenderContext();
        using var canvas = raster.CreateCanvas(4, 4);
        canvas.DrawRectangle(raster.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 4, 4);
        var stream = new MemoryStream();
        canvas.Save(stream, "png", new PeachImage.Formats.Png.PngEncoderOptions());
        stream.Position = 0;
        return stream;
    }

    private sealed class PixellessImage : PeachDrawing.Core.Image
    {
        public override double Width => 4;

        public override double Height => 4;

        public override bool Interpolate { get; set; } = true;

        public override void Dispose()
        {
        }
    }
}
