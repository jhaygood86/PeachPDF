using PeachDrawing;

namespace PeachBrackets.Tests;

/// <summary>A CSS <c>filter: url(#id)</c> on an HTML box runs the SVG filter of the document that has that id over the box's pixels.</summary>
public class SvgFilterReferenceTests
{
    private static async Task<(int R, int G, int B)> CenterAsync(string body)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body), new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 200 });
        using var _ = document;
        var bounds = document.GetElementBounds("box")!.Value;
        using var canvas = await TestDocument.PaintAsync(context, document);
        var pixel = TestDocument.Pixel(canvas, (int)(bounds.X + (bounds.Width / 2)), (int)(bounds.Y + (bounds.Height / 2)));
        return (pixel.R, pixel.G, pixel.B);
    }

    [Fact]
    public async Task AFloodFilter_ReplacesTheBoxWithItsColor()
    {
        var color = await CenterAsync("""
            <svg width="0" height="0"><filter id="f"><feFlood flood-color="rgb(255,0,0)"/></filter></svg>
            <div id="box" style="width:60px;height:40px;background:rgb(0,128,0);filter:url(#f)"></div>
            """);

        Assert.Equal((255, 0, 0), color);
    }

    [Fact]
    public async Task AFilterThatIsNotInTheDocument_AppliesNothing()
    {
        var color = await CenterAsync("""<div id="box" style="width:60px;height:40px;background:rgb(0,128,0);filter:url(#nothing)"></div>""");

        Assert.Equal((0, 128, 0), color);
    }

    [Fact]
    public async Task AFilterInAnSvgThatIsNeverShown_StillApplies_AndAnInvalidListIsDropped()
    {
        var hidden = await CenterAsync("""
            <svg style="display:none"><filter id="f"><feFlood flood-color="rgb(0,0,255)"/></filter></svg>
            <div id="box" style="width:60px;height:40px;background:rgb(0,128,0);filter:url(#f)"></div>
            """);
        var invalid = await CenterAsync("""<div id="box" style="width:60px;height:40px;background:rgb(0,128,0);filter:url(#f) banana(3)"></div>""");

        Assert.Equal((0, 0, 255), hidden);
        Assert.Equal((0, 128, 0), invalid);
    }
}
