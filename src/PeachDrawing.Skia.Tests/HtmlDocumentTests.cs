using PeachBrackets;
using PeachDrawing.Core;
using PeachDrawing.Skia;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Skia.Tests;

/// <summary>A real HTML page painted onto a Skia canvas through the PeachBrackets public API, the way a host on a phone would show one.</summary>
public class HtmlDocumentTests
{
    [Theory]
    [InlineData(SkiaTextEngine.PeachDrawing)]
    [InlineData(SkiaTextEngine.Native)]
    public async Task A_page_is_painted_with_its_background_text_and_links(SkiaTextEngine engine)
    {
        using var scene = Scene.Create(300, 200, engine: engine);
        await using (var stream = File.OpenRead(BundledFonts.Ttf))
            await scene.Context.AddFont(stream, "Source Sans 3");

        var html = """
            <!DOCTYPE html><html><head><style>
              body{font:16px 'Source Sans 3';margin:0} @page{size:300pt 200pt;margin:0}
              .band{background:rgb(255,0,0);height:40px}
            </style></head><body>
              <div class="band"></div>
              <p><a href="https://example.com/x">a link</a></p>
            </body></html>
            """;

        using var document = await HtmlDocument.LoadAsync(html, scene.Context, new HtmlDocumentOptions { PageWidthPoints = 300, PageHeightPoints = 200 });
        await document.LayoutAsync(scene.Canvas);
        await document.PaintPageAsync(0, scene.Canvas);
        document.PaintLinks(0, scene.Canvas);

        Assert.True(scene.IsRed(150, 10), "the band's background");
        // The band is 40 CSS pixels (30 points) tall, so text adds to 300 x 30.
        Assert.True(scene.OpaquePixels() > 300 * 30 + 50, "the band plus text");
        var link = Assert.Single(scene.Composer.Links.Links);
        Assert.Equal("https://example.com/x", link.Uri);
        Assert.NotNull(scene.Composer.Links.HitTest(link.Bounds.X + 1, link.Bounds.Y + 1));
    }

    [Fact]
    public async Task Effects_that_need_pixels_are_drawn_through_the_raster_seam()
    {
        using var scene = Scene.Create(120, 80);
        var html = """
            <!DOCTYPE html><html><head><style>
              body{margin:0} @page{size:120pt 80pt;margin:0}
              .box{width:60px;height:40px;margin:10px;background:rgb(0,0,255);filter:blur(4px)}
            </style></head><body><div class="box"></div></body></html>
            """;

        using var document = await HtmlDocument.LoadAsync(html, scene.Context, new HtmlDocumentOptions { PageWidthPoints = 120, PageHeightPoints = 80 });
        await document.LayoutAsync(scene.Canvas);
        await document.PaintPageAsync(0, scene.Canvas);

        // The middle of the box is solid blue; its edge is soft, so some pixels are partly covered.
        Assert.True(scene.IsBlue(30, 25));
        Assert.Contains(Enumerable.Range(0, 120), x => scene.Px(x, 25).A is > 0 and < 255);
    }
}
