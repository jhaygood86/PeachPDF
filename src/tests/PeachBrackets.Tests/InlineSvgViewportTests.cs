namespace PeachBrackets.Tests;

/// <summary>A percentage inside an inline <c>svg</c> resolves against its viewport, which a CSS length gives it when it has no attribute size.</summary>
public class InlineSvgViewportTests
{
    private static readonly (byte, byte, byte) Green = (0, 128, 0);
    private static readonly (byte, byte, byte) White = (255, 255, 255);

    [Fact]
    public async Task APercentageRectFillsAnSvgSizedOnlyByCss()
    {
        using var canvas = await PageRaster.RenderAsync(
            "<svg style='width:60px;height:40px'><rect width='100%' height='100%' fill='green'/></svg>", width: 120, height: 80);

        Assert.Equal(Green, PageRaster.Rgb(canvas, 30, 20));
        Assert.Equal(Green, PageRaster.Rgb(canvas, 58, 38));
        Assert.Equal(White, PageRaster.Rgb(canvas, 70, 20));
    }

    [Fact]
    public async Task AnAttributeSizeStillWinsOverTheCssOne()
    {
        using var canvas = await PageRaster.RenderAsync(
            "<svg width='30' height='20' style='width:60px;height:40px'><rect width='100%' height='100%' fill='green'/></svg>", width: 120, height: 80);

        Assert.Equal(Green, PageRaster.Rgb(canvas, 10, 10));
    }
}
