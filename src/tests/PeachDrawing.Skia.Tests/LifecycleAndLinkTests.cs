using PeachDrawing.Core;
using PeachDrawing.Skia;

namespace PeachDrawing.Skia.Tests;

/// <summary>That every native resource a canvas makes is released, and what the link map does with what a document drew.</summary>
public class LifecycleAndLinkTests
{
    private sealed class RecordingLauncher : ISkiaLinkLauncher
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
        Assert.Equal(1, counts.Recorders);
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
    public void Decoded_images_are_counted_until_they_are_disposed()
    {
        using var context = new SkiaRenderContext(imageCodec: SkiaImageCodec.Native);
        var image = context.ImageFromStream(new MemoryStream(Scene.TwoColourPng()));
        Assert.Equal(1, context.Counts.Surfaces);

        image.Dispose();
        Assert.Equal(0, context.Counts.Surfaces);
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
        using var scene = Scene.Create(options: new SkiaComposerOptions { Links = new SkiaLinkOptions { Launcher = launcher } });
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
        SkiaLink? seen = null;
        scene.Composer.Links.LinkActivating += (_, e) => seen = e.Link;

        Assert.False(await scene.Composer.Links.TryActivateAsync(5, 5));
        Assert.Equal("https://example.com/", seen!.Uri);
    }

    [Fact]
    public async Task A_cancelled_activation_is_not_followed()
    {
        var launcher = new RecordingLauncher();
        using var scene = Scene.Create(options: new SkiaComposerOptions { Links = new SkiaLinkOptions { Launcher = launcher } });
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
        using var context = new SkiaRenderContext();

        Assert.NotEqual(PaintColor.Empty, context.GetColor("red"));
        Assert.Equal(PaintColor.Empty, context.GetColor("not-a-colour"));
        Assert.Equal("screen", context.GetCssMediaType(["screen", "print"]));
        Assert.Equal("all", context.GetCssMediaType(["print"]));
    }
}
