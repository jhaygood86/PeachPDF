using PeachDrawing.Core;
using PeachDrawing.CoreGraphics;

namespace PeachDrawing.CoreGraphics.Tests;

/// <summary>That every native resource a canvas makes is released, and what the link map does with what a document drew.</summary>
public class LifecycleAndLinkTests
{
    private sealed class RecordingLauncher : ICoreGraphicsLinkLauncher
    {
        public List<Uri> Opened { get; } = [];

        public Task<bool> LaunchAsync(Uri uri)
        {
            Opened.Add(uri);
            return Task.FromResult(true);
        }
    }

    [Fact]
    public void Tiles_layers_and_images_are_all_released_once_the_drawing_is_finished()
    {
        var scene = Scene.Create();
        var counts = scene.Context.Counts;

        var tile = scene.Canvas.CreateTile(10, 10)!.Value;
        tile.Graphics.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);
        Assert.Equal(1, counts.Tiles);

        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.5)))
        {
            Assert.Equal(1, counts.Layers);
            scene.Canvas.DrawImage(tile.Image, new Rect(0, 0, 10, 10));
        }

        Assert.Equal(0, counts.Layers);
        tile.Image.Dispose();
        scene.Dispose();

        Assert.Equal(0, counts.Total);
    }

    [Fact]
    public void A_layer_left_open_is_ended_when_its_canvas_is_disposed()
    {
        var scene = Scene.Create();
        var counts = scene.Context.Counts;
        var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.5, Effects: [new BlurEffect(2)]));
        layer!.Canvas.PushClip(new Rect(0, 0, 5, 5));

        scene.Dispose();
        layer.Dispose();

        Assert.Equal(0, counts.Total);
    }

    [Fact]
    public void A_tile_never_composited_is_released_with_its_image()
    {
        var scene = Scene.Create();
        var counts = scene.Context.Counts;
        var tile = scene.Canvas.CreateTile(10, 10)!.Value;

        tile.Image.Dispose();
        Assert.Equal(0, counts.Total);
        scene.Dispose();
    }

    [Fact]
    public void Images_are_counted_from_the_first_draw_until_they_are_disposed()
    {
        using var scene = Scene.Create(codec: CoreGraphicsImageCodec.Native);
        var counts = scene.Context.Counts;
        var image = scene.Context.ImageFromStream(new MemoryStream(Scene.TwoColourPng()));
        scene.Canvas.DrawImage(image, new Rect(0, 0, 8, 4));
        Assert.Equal(1, counts.Surfaces);

        image.Dispose();
        Assert.Equal(0, counts.Surfaces);
    }

    [Fact]
    public void A_borrowed_context_gets_its_graphics_state_back()
    {
        using var context = new CoreGraphicsRenderContext();
        using var bitmap = new global::CoreGraphics.CGBitmapContext(IntPtr.Zero, 20, 20, 8, 0, CoreGraphicsCanvas.Rgb, global::CoreGraphics.CGImageAlphaInfo.PremultipliedLast);
        var before = bitmap.GetCTM();

        var canvas = CoreGraphicsCanvas.FromContext(context, bitmap, new Rect(0, 0, 20, 20));
        canvas.PushTransform(System.Numerics.Matrix3x2.CreateScale(3, 3));
        canvas.PushClip(new Rect(0, 0, 5, 5));
        canvas.PushBlendMode(PaintBlendMode.Multiply);
        canvas.Dispose();

        var after = bitmap.GetCTM();
        Assert.Equal((double)before.A, (double)after.A);
        Assert.Equal((double)before.D, (double)after.D);
        Assert.Equal((double)before.Ty, (double)after.Ty);

        // The context is still the caller's and still draws, unclipped.
        bitmap.SetFillColor(new global::CoreGraphics.CGColor(CoreGraphicsCanvas.Rgb, [1, 0, 0, 1]));
        bitmap.FillRect(new global::CoreGraphics.CGRect(15, 15, 5, 5));
        var span = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.Data + 2 * (int)bitmap.BytesPerRow + 17 * 4, span, 0, 4);
        Assert.Equal(255, span[0]);
    }

    [Fact]
    public void A_context_whose_origin_is_already_top_left_is_not_flipped()
    {
        using var context = new CoreGraphicsRenderContext();
        using var bitmap = new global::CoreGraphics.CGBitmapContext(IntPtr.Zero, 20, 20, 8, 0, CoreGraphicsCanvas.Rgb, global::CoreGraphics.CGImageAlphaInfo.PremultipliedLast);

        // The host does the flip UIKit does.
        bitmap.TranslateCTM(0, 20);
        bitmap.ScaleCTM(1, -1);
        using (var canvas = CoreGraphicsCanvas.FromContext(context, bitmap, new Rect(0, 0, 20, 20), originAtBottomLeft: false))
            canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 20, 5);   // the top rows

        var top = new byte[4];
        var bottom = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.Data + 2 * (int)bitmap.BytesPerRow + 10 * 4, top, 0, 4);
        System.Runtime.InteropServices.Marshal.Copy(bitmap.Data + 17 * (int)bitmap.BytesPerRow + 10 * 4, bottom, 0, 4);
        Assert.Equal(255, top[0]);
        Assert.Equal(0, bottom[3]);
    }

    [Fact]
    public void Disposing_a_canvas_twice_is_harmless()
    {
        using var scene = Scene.Create();
        scene.Composer.Finish();
        scene.Canvas.Dispose();
        scene.Composer.Finish();
    }

    [Fact]
    public void Links_and_destinations_are_recorded_for_the_host_to_hit_test()
    {
        using var scene = Scene.Create();
        scene.Canvas.AddLink(new Rect(0, 0, 10, 10), "https://example.com/a");
        scene.Canvas.AddDestinationLink(new Rect(20, 0, 10, 10), "section");
        scene.Canvas.AddDestination("section", 120);

        Assert.Equal("https://example.com/a", scene.Composer.Links.HitTest(5, 5)!.Uri);
        Assert.Equal("section", scene.Composer.Links.HitTest(25, 5)!.Destination);
        Assert.Null(scene.Composer.Links.HitTest(15, 15));
        Assert.True(scene.Composer.Links.TryGetDestination("section", out var top));
        Assert.Equal(120, top);
        Assert.False(scene.Composer.Links.TryGetDestination("elsewhere", out _));
    }

    [Fact]
    public void A_link_over_a_rotated_element_hits_only_inside_its_quad()
    {
        using var scene = Scene.Create();
        // A diamond: top-left, top-right, bottom-left, bottom-right of the rectangle it started as.
        scene.Canvas.AddLink(new Rect(0, 0, 20, 20), "https://example.com/",
            [new PaintPoint(10, 0), new PaintPoint(20, 10), new PaintPoint(0, 10), new PaintPoint(10, 20)]);

        Assert.NotNull(scene.Composer.Links.HitTest(10, 10));
        Assert.Null(scene.Composer.Links.HitTest(1, 1));
    }

    [Fact]
    public async Task An_address_is_opened_only_for_an_allowed_scheme_and_through_the_hosts_launcher()
    {
        var launcher = new RecordingLauncher();
        using var scene = Scene.Create(options: new CoreGraphicsComposerOptions { Links = new CoreGraphicsLinkOptions { Launcher = launcher } });
        scene.Canvas.AddLink(new Rect(0, 0, 10, 10), "https://example.com/ok");
        scene.Canvas.AddLink(new Rect(10, 0, 10, 10), "file:///etc/passwd");
        scene.Canvas.AddLink(new Rect(20, 0, 10, 10), "relative/path");

        Assert.True(await scene.Composer.Links.TryActivateAsync(5, 5));
        Assert.False(await scene.Composer.Links.TryActivateAsync(15, 5));
        Assert.False(await scene.Composer.Links.TryActivateAsync(25, 5));
        Assert.False(await scene.Composer.Links.TryActivateAsync(35, 5));
        Assert.Equal([new Uri("https://example.com/ok")], launcher.Opened);
    }

    [Fact]
    public async Task With_no_launcher_the_host_follows_a_link_itself_from_the_event()
    {
        using var scene = Scene.Create();
        scene.Canvas.AddLink(new Rect(0, 0, 10, 10), "https://example.com/");
        CoreGraphicsLink? seen = null;
        scene.Composer.Links.LinkActivating += (_, e) => seen = e.Link;

        Assert.False(await scene.Composer.Links.TryActivateAsync(5, 5));
        Assert.Equal("https://example.com/", seen!.Uri);
    }

    [Fact]
    public async Task A_cancelled_activation_is_not_followed()
    {
        var launcher = new RecordingLauncher();
        using var scene = Scene.Create(options: new CoreGraphicsComposerOptions { Links = new CoreGraphicsLinkOptions { Launcher = launcher } });
        scene.Canvas.AddLink(new Rect(0, 0, 10, 10), "https://example.com/");
        scene.Composer.Links.LinkActivating += (_, e) => e.Cancel = true;

        Assert.False(await scene.Composer.Links.TryActivateAsync(5, 5));
        Assert.Empty(launcher.Opened);
    }

    [Fact]
    public void Links_inside_a_tile_or_layer_are_not_recorded()
    {
        using var scene = Scene.Create();
        var tile = scene.Canvas.CreateTile(10, 10)!.Value;
        tile.Graphics.AddLink(new Rect(0, 0, 5, 5), "https://example.com/tile");
        tile.Image.Dispose();

        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.5)))
            layer!.Canvas.AddLink(new Rect(0, 0, 5, 5), "https://example.com/layer");

        Assert.Empty(scene.Composer.Links.Links);
    }

    [Fact]
    public void A_context_hands_out_the_same_colours_as_the_other_backends()
    {
        using var context = new CoreGraphicsRenderContext();

        Assert.NotEqual(PaintColor.Empty, context.GetColor("red"));
        Assert.Equal(PaintColor.Empty, context.GetColor("not-a-colour"));
        Assert.Equal("screen", context.GetCssMediaType(["screen", "print"]));
        Assert.Equal("all", context.GetCssMediaType(["print"]));
    }

    [Fact]
    public void Cursor_areas_are_recorded_for_the_host_and_the_topmost_one_wins()
    {
        using var scene = Scene.Create();
        scene.Canvas.AddCursor(new Rect(0, 0, 20, 20), CursorShape.Crosshair);
        scene.Canvas.AddCursor(new Rect(10, 10, 20, 20), CursorShape.Grab, [new CursorImage("https://example.com/c.png", 1, 2)]);

        Assert.Equal(2, scene.Composer.Links.Cursors.Count);
        Assert.Equal(CursorShape.Crosshair, scene.Composer.Links.CursorAt(2, 2)!.Shape);
        var top = scene.Composer.Links.CursorAt(15, 15)!;
        Assert.Equal(CursorShape.Grab, top.Shape);
        Assert.Equal("https://example.com/c.png", Assert.Single(top.Images).Uri);
        Assert.Null(scene.Composer.Links.CursorAt(40, 40));
    }

    [Fact]
    public void A_cursor_added_on_a_tile_is_not_recorded()
    {
        using var scene = Scene.Create();
        var (tile, _) = scene.Canvas.CreateTile(10, 10)!.Value;
        tile.AddCursor(new Rect(0, 0, 5, 5), CursorShape.Wait);
        tile.Dispose();

        Assert.Empty(scene.Composer.Links.Cursors);
    }

    [Fact]
    public void Semantic_regions_are_recorded_and_the_innermost_one_is_found_by_position()
    {
        using var scene = Scene.Create();
        scene.Canvas.AddSemanticRegion(new Rect(0, 0, 40, 40), new SemanticInfo("navigation") { Name = "Sections" });
        scene.Canvas.AddSemanticRegion(new Rect(10, 10, 10, 10), new SemanticInfo("link") { Name = "Jump" });
        var (tile, _) = scene.Canvas.CreateTile(10, 10)!.Value;
        tile.AddSemanticRegion(new Rect(0, 0, 5, 5), new SemanticInfo("button"));
        tile.Dispose();

        Assert.Equal(2, scene.Composer.Links.Semantics.Count);
        Assert.Equal("navigation", scene.Composer.Links.SemanticAt(2, 2)!.Info.Role);
        Assert.Equal("Jump", scene.Composer.Links.SemanticAt(15, 15)!.Info.Name);
        Assert.Null(scene.Composer.Links.SemanticAt(50, 50));
        Assert.Equal("Jump", scene.Composer.Links.SemanticAtDevice(15, 15)!.Info.Name);
    }
}
