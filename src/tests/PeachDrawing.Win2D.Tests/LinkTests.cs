using PeachDrawing.Core;
using PeachDrawing.Win2D;

namespace PeachDrawing.Win2D.Tests;

public class LinkTests
{
    private sealed class FakeLauncher : ILinkLauncher
    {
        public List<Uri> Launched { get; } = [];

        public bool Succeeds { get; set; } = true;

        public bool Throws { get; set; }

        public Task<bool> LaunchAsync(Uri uri)
        {
            if (Throws)
                throw new InvalidOperationException("no handler");

            Launched.Add(uri);
            return Task.FromResult(Succeeds);
        }
    }

    private static Scene SceneWith(FakeLauncher launcher, Win2dLinkOptions? options = null) =>
        Scene.Create(options: new Win2dComposerOptions { Links = options ?? new Win2dLinkOptions { Launcher = launcher } });

    [Fact]
    public void Links_are_recorded_and_found_by_position()
    {
        var launcher = new FakeLauncher();
        using var scene = SceneWith(launcher);
        scene.Canvas.AddLink(new Rect(5, 5, 10, 10), "https://example.com/a");
        scene.Canvas.AddLink(new Rect(10, 10, 10, 10), "https://example.com/b");

        Assert.Equal(2, scene.Canvas.Links.Links.Count);
        Assert.Equal("https://example.com/a", scene.Canvas.Links.HitTest(6, 6)!.Uri);

        // Where they overlap, the one added last is on top.
        Assert.Equal("https://example.com/b", scene.Canvas.Links.HitTest(12, 12)!.Uri);
        Assert.Null(scene.Canvas.Links.HitTest(30, 30));
    }

    [Fact]
    public void A_rotated_link_is_hit_by_its_quad_not_its_bounding_box()
    {
        using var scene = SceneWith(new FakeLauncher());

        // A diamond: top-left, top-right, bottom-left, bottom-right of the rectangle it started as, rotated 45 degrees about (20, 20).
        PaintPoint[] quad = [new(20, 10), new(30, 20), new(10, 20), new(20, 30)];
        scene.Canvas.AddLink(new Rect(10, 10, 20, 20), "https://example.com/", quad);

        Assert.NotNull(scene.Canvas.Links.HitTest(20, 20));
        Assert.Null(scene.Canvas.Links.HitTest(11, 11));
        Assert.Null(scene.Canvas.Links.HitTest(29, 29));
    }

    [Fact]
    public async Task Activating_a_link_hands_the_address_to_the_launcher()
    {
        var launcher = new FakeLauncher();
        using var scene = SceneWith(launcher);
        scene.Canvas.AddLink(new Rect(0, 0, 10, 10), "https://example.com/page?x=1");
        scene.Canvas.AddLink(new Rect(20, 0, 10, 10), "mailto:someone@example.com");

        Assert.True(await scene.Canvas.Links.TryActivateAsync(5, 5));
        Assert.True(await scene.Canvas.Links.TryActivateAsync(25, 5));
        Assert.False(await scene.Canvas.Links.TryActivateAsync(35, 35));

        Assert.Equal(new[] { "https://example.com/page?x=1", "mailto:someone@example.com" }, launcher.Launched.Select(u => u.OriginalString));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("relative/page.html")]
    [InlineData("#fragment")]
    public async Task A_link_to_anything_but_an_allowed_scheme_is_never_launched(string uri)
    {
        var launcher = new FakeLauncher();
        using var scene = SceneWith(launcher);
        scene.Canvas.AddLink(new Rect(0, 0, 10, 10), uri);

        Assert.False(await scene.Canvas.Links.TryActivateAsync(5, 5));
        Assert.Empty(launcher.Launched);
    }

    [Fact]
    public async Task The_allowed_schemes_can_be_widened_by_the_host()
    {
        var launcher = new FakeLauncher();
        var options = new Win2dLinkOptions { Launcher = launcher };
        options.AllowedSchemes.Add("ftp");
        using var scene = SceneWith(launcher, options);
        scene.Canvas.AddLink(new Rect(0, 0, 10, 10), "ftp://example.com/file");

        Assert.True(await scene.Canvas.Links.TryActivateAsync(5, 5));
    }

    [Fact]
    public async Task A_handler_can_cancel_the_activation()
    {
        var launcher = new FakeLauncher();
        using var scene = SceneWith(launcher);
        scene.Canvas.AddLink(new Rect(0, 0, 10, 10), "https://example.com/");
        Win2dLink? seen = null;
        scene.Canvas.Links.LinkActivating += (_, e) =>
        {
            seen = e.Link;
            e.Cancel = true;
        };

        Assert.False(await scene.Canvas.Links.TryActivateAsync(5, 5));
        Assert.Equal("https://example.com/", seen!.Uri);
        Assert.Empty(launcher.Launched);
    }

    [Fact]
    public async Task A_launcher_that_fails_or_throws_reports_false_and_never_throws()
    {
        var launcher = new FakeLauncher { Succeeds = false };
        using var scene = SceneWith(launcher);
        scene.Canvas.AddLink(new Rect(0, 0, 10, 10), "https://example.com/");

        Assert.False(await scene.Canvas.Links.TryActivateAsync(5, 5));

        launcher.Throws = true;
        Assert.False(await scene.Canvas.Links.TryActivateAsync(5, 5));
    }

    [Fact]
    public async Task A_destination_link_is_resolved_by_name_and_left_to_the_host_to_scroll_to()
    {
        var launcher = new FakeLauncher();
        using var scene = SceneWith(launcher);
        scene.Canvas.AddDestination("chapter-2", 480);
        scene.Canvas.AddDestination("chapter-2", 999);
        scene.Canvas.AddDestinationLink(new Rect(0, 0, 10, 10), "chapter-2");
        var raised = 0;
        scene.Canvas.Links.LinkActivating += (_, e) =>
        {
            Assert.Equal("chapter-2", e.Link.Destination);
            raised++;
        };

        Assert.True(scene.Canvas.Links.TryGetDestination("chapter-2", out var top));
        Assert.Equal(480, top);
        Assert.False(scene.Canvas.Links.TryGetDestination("missing", out _));
        Assert.False(await scene.Canvas.Links.TryActivateAsync(5, 5));
        Assert.Equal(1, raised);
        Assert.Empty(launcher.Launched);
    }

    [Fact]
    public void Links_survive_the_canvas_so_a_host_can_hit_test_after_painting()
    {
        var scene = SceneWith(new FakeLauncher());
        scene.Canvas.AddLink(new Rect(0, 0, 10, 10), "https://example.com/");
        var links = scene.Composer.Links;
        scene.Dispose();

        Assert.NotNull(links.HitTest(5, 5));
    }

    [Fact]
    public void A_tile_or_layer_records_no_links_because_it_is_not_the_page()
    {
        using var scene = SceneWith(new FakeLauncher());
        var (tile, image) = scene.Canvas.CreateTile(10, 10)!.Value;
        tile.AddLink(new Rect(0, 0, 5, 5), "https://example.com/");
        tile.Dispose();
        image.Dispose();

        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 1))!)
            layer.Canvas.AddLink(new Rect(0, 0, 5, 5), "https://example.com/");

        Assert.Empty(scene.Canvas.Links.Links);
    }

    [Fact]
    public void Nothing_is_launched_while_a_document_is_painted()
    {
        var launcher = new FakeLauncher();
        using var scene = SceneWith(launcher);
        scene.Canvas.AddLink(new Rect(0, 0, 10, 10), "https://example.com/");
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);
        scene.Composer.Finish();

        Assert.Empty(launcher.Launched);
    }
}
