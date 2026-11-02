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
        _ = scene.Composer.ToSvg();
        scene.Canvas.AddLink(new Rect(0, 0, 1, 1), "late");
        scene.Canvas.AddDestination("late", 1);

        Assert.DoesNotContain("late", scene.Composer.ToSvg());
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
        await document.LayoutAsync(new SvgComposer(context, 300, 400).Canvas);
        var page = new SvgComposer(context, 300, 400);
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

    [Fact]
    public void ACursorAreaIsAnUnpaintedRectangleWithACursorStyleUnderTheLinks()
    {
        using var scene = Create();
        scene.Canvas.AddLink(new Rect(0, 0, 5, 5), "https://example.com/");
        scene.Canvas.AddCursor(new Rect(10, 20, 30, 8), CursorShape.ResizeDiagonalDown, [new CursorImage("a'b.png", 3, 4)]);

        var drawn = scene.Drawn().ToList();
        Assert.Equal([Ns + "rect", Ns + "a"], drawn.Select(e => e.Name)); // below the link, so a link it covers is still clickable
        Assert.Equal(("10", "20", "30", "8"), (Attr(drawn[0], "x"), Attr(drawn[0], "y"), Attr(drawn[0], "width"), Attr(drawn[0], "height")));
        Assert.Equal(("none", "all"), (Attr(drawn[0], "fill"), Attr(drawn[0], "pointer-events")));
        Assert.Equal("cursor:url('a\\'b.png') 3 4, nwse-resize", Attr(drawn[0], "style"));
    }

    [Fact]
    public void EveryShapeIsWrittenAsACssCursorKeyword()
    {
        foreach (var shape in Enum.GetValues<CursorShape>())
        {
            using var scene = Create();
            scene.Canvas.AddCursor(new Rect(0, 0, 10, 10), shape);

            var style = Attr(Assert.Single(scene.Drawn()), "style");
            Assert.StartsWith("cursor:", style);
            Assert.False(style.EndsWith(':'), shape.ToString());
        }
    }

    [Fact]
    public void AnImageWithoutAHotspotIsListedBeforeTheKeyword_AndATileIgnoresCursors()
    {
        using var scene = Create();
        scene.Canvas.AddCursor(new Rect(0, 0, 10, 10), CursorShape.Pointer, [new CursorImage("a.png", null, null)]);
        var (tile, _) = scene.Canvas.CreateTile(10, 10)!.Value;
        tile.AddCursor(new Rect(0, 0, 1, 1), CursorShape.Wait);
        tile.Dispose();

        var rect = Assert.Single(scene.Drawn());
        Assert.Equal("cursor:url('a.png'), pointer", Attr(rect, "style"));
    }
    [Fact]
    public void ASemanticRegionIsAnUnpaintedRectangleWithItsRoleNameAndStates()
    {
        using var scene = Create();
        scene.Canvas.AddSemanticRegion(new Rect(1, 2, 30, 8), new SemanticInfo("heading")
        {
            Name = "A <b> & \"c\"",
            Description = "more",
            Language = "fr",
            Level = 2,
            Id = "e7",
            Properties = new Dictionary<string, string> { ["aria-expanded"] = "false" },
        });
        scene.Canvas.AddSemanticRegion(new Rect(0, 0, 1, 1), new SemanticInfo(string.Empty)); // no role: nothing to say
        var (tile, _) = scene.Canvas.CreateTile(10, 10)!.Value;
        tile.AddSemanticRegion(new Rect(0, 0, 1, 1), new SemanticInfo("button")); // a tile is not the page
        tile.Dispose();

        var rect = Assert.Single(scene.Drawn());
        Assert.Equal(("1", "2", "30", "8"), (Attr(rect, "x"), Attr(rect, "y"), Attr(rect, "width"), Attr(rect, "height")));
        Assert.Equal(("none", "none"), (Attr(rect, "fill"), Attr(rect, "pointer-events")));
        Assert.Equal(("heading", "A <b> & \"c\"", "more", "fr", "2", "node-e7", "false"),
            (Attr(rect, "role"), Attr(rect, "aria-label"), Attr(rect, "aria-description"), Attr(rect, "lang"), Attr(rect, "aria-level"), Attr(rect, "id"), Attr(rect, "aria-expanded")));
    }

    [Fact]
    public async Task ADocumentPaintsItsAccessibilityTreeAsRolesInReadingOrder()
    {
        var context = new SvgRenderContext();
        await using (var stream = File.OpenRead(BundledFonts.Ttf))
            await context.AddFont(stream, "Source Sans 3");
        var html = """
            <!DOCTYPE html><html lang="en"><head><style>body{font:16px 'Source Sans 3'} @page{size:300pt 400pt;margin:10pt}</style></head><body>
            <nav aria-label="Sections"><a href="#a">Jump</a></nav><h2 id="a">Title</h2><button aria-pressed="true">Go</button>
            </body></html>
            """;

        using var document = await HtmlDocument.LoadAsync(html, context, new HtmlDocumentOptions { PageWidthPoints = 300, PageHeightPoints = 400 });
        await document.LayoutAsync(new SvgComposer(context, 300, 400).Canvas);
        var page = new SvgComposer(context, 300, 400);
        await document.PaintPageAsync(0, page.Canvas);
        document.PaintAccessibility(0, page.Canvas);

        var roles = XDocument.Parse(page.ToSvg()).Root!.Elements(Ns + "rect").Where(r => r.Attribute("role") is not null).ToList();
        Assert.Equal(["navigation", "link", "heading", "button"], roles.Select(r => Attr(r, "role")));
        Assert.Equal("Sections", Attr(roles[0], "aria-label"));
        Assert.Equal("Jump", Attr(roles[1], "aria-label"));
        Assert.Equal(("Title", "2"), (Attr(roles[2], "aria-label"), Attr(roles[2], "aria-level")));
        Assert.Equal(("Go", "true"), (Attr(roles[3], "aria-label"), Attr(roles[3], "aria-pressed")));
        Assert.All(roles, r => Assert.Equal("en", Attr(r, "lang")));
    }
}
