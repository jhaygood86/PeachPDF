using PeachBrackets;

namespace PeachDrawing.Skia.Tests;

/// <summary>
/// HTML pages whose effects the painter can do either in a CPU bitmap or, on a canvas that applies layer effects natively, on the
/// graphics hardware. They check what lands on the page and that no native resource is left behind.
/// </summary>
public class HtmlEffectsTests
{
    private static async Task<Scene> PaintAsync(string body, string css, int width = 120, int height = 100)
    {
        var scene = Scene.Create(width, height);
        var html = $$"""
            <!DOCTYPE html><html><head><style>
              body{margin:0} @page{size:{{width}}pt {{height}}pt;margin:0}
              {{css}}
            </style></head><body>{{body}}</body></html>
            """;

        using var document = await HtmlDocument.LoadAsync(html, scene.Context, new HtmlDocumentOptions { PageWidthPoints = width, PageHeightPoints = height });
        await document.LayoutAsync(scene.Canvas);
        await document.PaintPageAsync(0, scene.Canvas);
        return scene;
    }

    [Theory]
    [InlineData("")]
    [InlineData("border-radius:8px;")]
    public async Task A_blurred_outset_shadow_is_soft_and_leaves_the_boxs_own_area_clear(string extra)
    {
        // The box is 40 x 30 points at (30, 30); the shadow is black, 8 CSS pixels of blur, no offset.
        using var scene = await PaintAsync(
            "<div class='box'></div>",
            ".box{width:53.3333px;height:40px;margin:30pt;background:transparent;box-shadow:0 0 8px rgb(0,0,0);" + extra + "}");

        // Under the element's own border box nothing is painted: the shadow is only drawn outside the border edge.
        Assert.True(scene.IsClear(50, 45), "inside the border box");

        // Just outside the edge the shadow is partly covered: soft, neither absent nor solid.
        var edge = scene.Px(28, 45);
        Assert.InRange(edge.A, 20, 250);

        // Well beyond three deviations (6 pixels) nothing is left.
        Assert.True(scene.IsClear(5, 45), "far from the box");
        Assert.True(scene.IsClear(115, 95), "the far corner");

        Assert.Equal(0, scene.Context.Counts.Total);
    }

    private const string FilteredBox = ".box{width:40pt;height:30pt;margin:30pt;background:rgb(255,0,0);";

    [Fact]
    public async Task A_blur_filter_softens_the_edge_and_keeps_the_middle_solid()
    {
        using var scene = await PaintAsync("<div class='box'></div>", FilteredBox + "filter:blur(4px);}");

        Assert.True(scene.IsRed(50, 45), "the middle is still solid");
        Assert.InRange(scene.Px(30, 45).A, 20, 235);
        Assert.True(scene.IsClear(3, 45), "far outside the blur");
        Assert.Equal(0, scene.Context.Counts.Total);
    }

    [Fact]
    public async Task A_grayscale_filter_turns_red_into_its_luminance()
    {
        using var scene = await PaintAsync("<div class='box'></div>", FilteredBox + "filter:grayscale(1);}");

        var (r, g, b, a) = scene.Px(50, 45);
        Assert.Equal(255, a);
        Assert.InRange(r, 45, 65);
        Assert.InRange(Math.Abs(r - g) + Math.Abs(g - b), 0, 3);
        Assert.True(scene.IsClear(10, 10));
    }

    [Fact]
    public async Task The_filters_run_in_the_order_they_are_written_and_the_elements_own_opacity_comes_last()
    {
        // The red goes to gray, then the half-opacity filter halves its alpha, then the element's opacity halves it again.
        using var scene = await PaintAsync("<div class='box'></div>", FilteredBox + "filter:grayscale(1) opacity(0.5);opacity:0.5;}");

        var (_, _, _, a) = scene.Px(50, 45);
        Assert.InRange(a, 55, 72);
    }

    [Fact]
    public async Task A_drop_shadow_filter_is_drawn_from_the_elements_shape_and_not_doubled()
    {
        using var scene = await PaintAsync("<div class='box'></div>", FilteredBox + "filter:drop-shadow(6px 0 0 rgb(0,0,255));}");

        // The shadow lands to the right of the box (10 points past its right edge at 70), the element stays on top of it.
        Assert.True(scene.IsRed(50, 45), "the element itself");
        var shadow = scene.Px(73, 45);
        Assert.True(shadow.B > 200 && shadow.R < 40 && shadow.A > 200, $"the shadow at the right: {shadow}");
        Assert.True(scene.IsClear(30 - 3, 45), "no shadow on the left");
        Assert.Equal(0, scene.Context.Counts.Total);
    }

    [Theory]
    [InlineData("")]
    [InlineData("border-radius:6px;")]
    public async Task A_backdrop_filter_blurs_what_is_behind_the_element_and_only_inside_its_box(string extra)
    {
        // A red half and a blue half; a panel over the seam, 60 x 40 points at (30, 30), blurs whatever is behind it.
        using var scene = await PaintAsync(
            "<div class='a'></div><div class='b'></div><div class='panel'></div>",
            ".a,.b,.panel{position:absolute;top:0;height:100pt}.a{left:0;width:60pt;background:rgb(255,0,0)}" +
            ".b{left:60pt;width:60pt;background:rgb(0,0,255)}" +
            ".panel{left:30pt;top:30pt;width:60pt;height:40pt;backdrop-filter:blur(8px);" + extra + "}");

        // Outside the panel the seam is sharp.
        Assert.True(scene.IsRed(58, 10), "red just left of the seam");
        Assert.True(scene.Px(62, 10) is { B: > 240, R: < 20 }, "blue just right of the seam");

        // Inside it the seam is smeared: both colours are present at the seam, far from it they are what they were.
        var seam = scene.Px(60, 50);
        Assert.InRange(seam.R, 60, 200);
        Assert.InRange(seam.B, 60, 200);
        Assert.True(scene.Px(36, 50) is { R: > 240, B: < 20 }, "far from the seam it stays red");

        // The blur holds the panel's edge instead of fading toward it: a pixel at the very edge is still opaque.
        Assert.Equal(255, scene.Px(31, 50).A);
        Assert.Equal(0, scene.Context.Counts.Total);
    }

    [Fact]
    public async Task A_shadow_gets_fainter_away_from_the_edge()
    {
        using var scene = await PaintAsync(
            "<div class='box'></div>",
            ".box{width:53.3333px;height:40px;margin:30pt;box-shadow:0 0 12px rgb(0,0,0);}");

        var near = scene.Px(29, 45).A;
        var middle = scene.Px(26, 45).A;
        var far = scene.Px(22, 45).A;
        Assert.True(near > middle, $"{near} > {middle}");
        Assert.True(middle > far, $"{middle} > {far}");
    }
}
