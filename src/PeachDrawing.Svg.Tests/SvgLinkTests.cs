using PeachBrackets;
using PeachDrawing.Core;
using PeachPDF.Tests.TestSupport;
using System.Xml.Linq;
using static PeachDrawing.Svg.Tests.SvgScene;

namespace PeachDrawing.Svg.Tests;

public class SvgLinkTests
{
    [Fact]
    public void ALinkIsAnAnchorAroundAnUnpaintedClickableRectangleAfterEverythingDrawn()
    {
        using var scene = Create();
        var g = scene.Canvas;
        Assert.Null(g.AddLink(new Rect(10, 20, 30, 8), "https://example.com/?a=1&b=2")); // added first; written last
        g.PushClip(new Rect(0, 0, 50, 50));
        g.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 5, 5);

        var drawn = scene.Drawn().ToList();
        Assert.Equal([Ns + "g", Ns + "a"], drawn.Select(e => e.Name)); // the open clip group is closed before the link, so the link is not inside it
        var anchor = drawn[1];
        Assert.Equal("https://example.com/?a=1&b=2", Attr(anchor, "href"));
        var rect = Assert.Single(anchor.Elements());
        Assert.Equal(("10", "20", "30", "8"), (Attr(rect, "x"), Attr(rect, "y"), Attr(rect, "width"), Attr(rect, "height")));
        Assert.Equal(("none", "all"), (Attr(rect, "fill"), Attr(rect, "pointer-events")));
    }

    [Fact]
    public void ALinkOverARotatedElementFollowsItsShape()
    {
        using var scene = Create();
        // top-left, top-right, bottom-left, bottom-right of the rectangle it started as
        scene.Canvas.AddLink(new Rect(0, 0, 20, 20), "x", [new PaintPoint(5, 0), new PaintPoint(20, 5), new PaintPoint(0, 15), new PaintPoint(15, 20)]);

        var polygon = Assert.Single(Assert.Single(scene.Drawn()).Elements());
        Assert.Equal(Ns + "polygon", polygon.Name);
        Assert.Equal("5,0 20,5 15,20 0,15", Attr(polygon, "points")); // round the shape
    }

    [Fact]
    public void ADestinationIsAnElementAtItsHeightAndALinkToItIsAFragmentLink()
    {
        using var scene = Create();
        scene.Canvas.AddDestination("section 1/a", 42.5);
        scene.Canvas.AddDestination("section 1/a", 99); // a name is defined once
        scene.Canvas.AddDestinationLink(new Rect(0, 0, 10, 10), "section 1/a");

        var drawn = scene.Drawn().ToList();
        var target = drawn[0];
        Assert.Equal(("anchor-section 1/a", "42.5"), (Attr(target, "id"), Attr(target, "y")));
        Assert.Equal(2, drawn.Count);
        Assert.Equal("#anchor-section%201%2Fa", Attr(drawn[1], "href"));
    }

    [Fact]
    public void ATileAndAFinishedPageWriteNoLinks()
    {
        using var scene = Create();
        var (tile, _) = scene.Canvas.CreateTile(10, 10)!.Value;
        Assert.Null(tile.AddLink(new Rect(0, 0, 1, 1), "x"));
        tile.AddDestination("t", 1);
        tile.Dispose();
        _ = scene.Page.ToSvg();
        scene.Canvas.AddLink(new Rect(0, 0, 1, 1), "late");
        scene.Canvas.AddDestination("late", 1);

        Assert.DoesNotContain("late", scene.Page.ToSvg());
        Assert.Empty(scene.Drawn());
    }

    [Fact]
    public async Task LinksOfAnHtmlDocumentGoOnTheirPagesAndAnAnchorLeadsToItsHeight()
    {
        var context = new SvgRenderContext();
        await using (var stream = File.OpenRead(BundledFonts.Ttf))
            await context.AddFont(stream, "Source Sans 3");
        var html = """
            <!DOCTYPE html><html><head><style>body{font:16px 'Source Sans 3'} @page{size:300pt 400pt;margin:10pt}</style></head><body>
            <p><a href="https://example.com/x">outside</a> and <a href="#target">inside</a></p>
            <div style="height:100px"></div><h2 id="target">Target</h2></body></html>
            """;

        using var document = await HtmlDocument.LoadAsync(html, context, new HtmlDocumentOptions { PageWidthPoints = 300, PageHeightPoints = 400 });
        using var composer = new SvgComposer(context);
        await document.LayoutAsync(composer.AddPage(300, 400).Canvas);
        var page = composer.AddPage(300, 400);
        await document.PaintPageAsync(0, page.Canvas);
        document.PaintLinks(0, page.Canvas);

        var svg = XDocument.Parse(page.ToSvg());
        var anchors = svg.Root!.Elements(Ns + "a").ToDictionary(a => Attr(a, "href"));
        Assert.Contains("https://example.com/x", anchors.Keys);
        var external = Assert.Single(anchors["https://example.com/x"].Elements());
        Assert.InRange(double.Parse(Attr(external, "y")), 5, 60);
        Assert.True(double.Parse(Attr(external, "width")) > 20);

        var inside = Assert.Single(anchors["#anchor-target"].Elements());
        var destination = Assert.Single(svg.Root.Elements(Ns + "rect"), r => Attr(r, "id") == "anchor-target");
        Assert.True(double.Parse(Attr(destination, "y")) > 100, "the heading is below the spacer");
        Assert.True(double.Parse(Attr(inside, "x")) > double.Parse(Attr(external, "x")), "the second link is further along the line");
    }

    [Fact]
    public void ACanvasThatDoesNotWriteLinksTakesTheCallsAsNoOps()
    {
        var raster = new RasterRenderContext();
        using var canvas = raster.CreateCanvas(10, 10);

        Assert.Null(canvas.AddLink(new Rect(0, 0, 1, 1), "x"));
        Assert.Null(canvas.AddDestinationLink(new Rect(0, 0, 1, 1), "x"));
        canvas.AddDestination("x", 1);
    }
}
