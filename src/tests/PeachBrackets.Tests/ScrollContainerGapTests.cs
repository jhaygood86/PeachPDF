using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>Scroll containers: sticky boxes, offsets kept across a reload, alignment, writing modes, overflow areas and finding text.</summary>
public class ScrollContainerGapTests
{
    private static HtmlDocumentOptions Screen() => new() { MediaType = MediaType.Screen, PageWidthPoints = 400, ViewportHeightPoints = 300 };

    private static Task<(PeachDrawing.RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string css, string body) =>
        TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>body {{ margin: 0 }} {css}</style>"), Screen());

    private static string Items(int count) => string.Concat(Enumerable.Range(0, count).Select(n => $"<div class=\"i\" id=\"i{n}\"></div>"));

    [Fact]
    public async Task AStickyBoxInAScrollContainer_StaysAtTheTopOfItsScrollport()
    {
        var (_, document) = await LoadAsync("#s { width: 100pt; height: 100pt; overflow: auto } .i { height: 50pt } #h { position: sticky; top: 0; height: 20pt }",
            "<div id=s><div id=h></div>" + Items(10) + "</div>");
        var s = document.GetElement("s")!;
        var port = document.GetScrollMetrics(s)!.Value.Viewport;

        Assert.Equal(port.Y, document.GetElementBounds("h")!.Value.Y, 1);

        document.SetScrollPosition(s, 0, 150);
        Assert.Equal(port.Y, document.GetElementBounds("h")!.Value.Y, 1);

        // The other content moved on without it.
        Assert.Equal(port.Y + 20 + (3 * 50) - 150, document.GetElementBounds("i3")!.Value.Y, 1);

        document.SetScrollPosition(s, 0, 0);
        Assert.Equal(port.Y, document.GetElementBounds("h")!.Value.Y, 1);
    }

    [Fact]
    public async Task AStickyBoxInAScrollContainer_LeavesWithItsContainingBlock()
    {
        var (_, document) = await LoadAsync("#s { width: 100pt; height: 100pt; overflow: auto } .i { height: 50pt } #w { height: 120pt } #h { position: sticky; top: 0; height: 20pt }",
            "<div id=s><div id=w><div id=h></div></div>" + Items(10) + "</div>");
        var s = document.GetElement("s")!;
        var port = document.GetScrollMetrics(s)!.Value.Viewport;

        document.SetScrollPosition(s, 0, 300);

        // The wrapper ends at 120pt, so the header ends there too: 20pt above the wrapper's bottom, 300pt scrolled away.
        Assert.Equal(port.Y + 100 - 300 + 0, document.GetElementBounds("h")!.Value.Y, 1);
    }

    [Fact]
    public async Task AReloadFromAClassChange_KeepsWhereAContainerWasScrolled()
    {
        var (context, document) = await LoadAsync("#s { width: 100pt; height: 100pt; overflow: auto } .i { height: 50pt } #x.wide { width: 80pt } #x { height: 5pt }",
            "<div id=x></div><div id=s>" + Items(8) + "</div>");
        using var measuring = context.CreateCanvas(1, 1);
        var s = document.GetElement("s")!;
        document.SetScrollPosition(s, 0, 120);

        await document.AddClassAsync(document.GetElement("x")!, "wide", measuring);

        Assert.Equal(120, document.GetScrollMetrics(document.GetElement("s")!)!.Value.Y, 1);
    }

    [Theory]
    [InlineData(ScrollAlignment.Start, 150)]
    [InlineData(ScrollAlignment.Center, 125)]
    [InlineData(ScrollAlignment.End, 100)]
    [InlineData(ScrollAlignment.Nearest, 100)]
    public async Task ScrollIntoView_AlignsTheElementAsAsked(ScrollAlignment block, double expected)
    {
        var (_, document) = await LoadAsync("#s { width: 100pt; height: 100pt; overflow: auto } .i { height: 50pt }", "<div id=s>" + Items(8) + "</div>");

        // Item 3 is 150 to 200: from the top, "nearest" brings its end to the bottom of the port (100), "start" its start to the top (150).
        document.ScrollIntoView(document.GetElement("i3")!, ScrollBehavior.Instant, block);

        Assert.Equal(expected, document.GetScrollMetrics(document.GetElement("s")!)!.Value.Y, 1);
    }

    [Fact]
    public async Task ARightToLeftContainer_StartsAtItsRight_AndScrollsOnToTheLeft()
    {
        var (_, document) = await LoadAsync("#s { width: 100pt; height: 100pt; overflow: auto; direction: rtl } #w { width: 300pt; height: 20pt; background: red }", "<div id=s><div id=w></div></div>");
        var s = document.GetElement("s")!;

        var metrics = document.GetScrollMetrics(s)!.Value;
        Assert.Equal(0, metrics.X, 1);
        Assert.Equal(-200, metrics.MinX, 1);
        Assert.Equal(0, metrics.MaxX, 1);
        Assert.Equal(300, metrics.ContentWidth, 1);

        // The content's right edge is at the port's right edge until it is scrolled.
        var port = metrics.Viewport;
        Assert.Equal(port.Right, document.GetElementBounds("w")!.Value.Right, 1);

        document.SetScrollPosition(s, -150, double.NaN);
        Assert.Equal(-150, document.GetScrollMetrics(s)!.Value.X, 1);
        Assert.Equal(port.Right + 150, document.GetElementBounds("w")!.Value.Right, 1);

        // Past the far end it stops.
        document.SetScrollPosition(s, -999, double.NaN);
        Assert.Equal(-200, document.GetScrollMetrics(s)!.Value.X, 1);
    }

    [Fact]
    public async Task ALeftToRightContainer_CannotScrollToContentBeforeItsStart()
    {
        var (_, document) = await LoadAsync("#s { width: 100pt; height: 100pt; overflow: auto } #w { width: 50pt; height: 20pt; margin-left: -80pt }", "<div id=s><div id=w></div></div>");

        var metrics = document.GetScrollMetrics(document.GetElement("s")!)!.Value;
        Assert.Equal(0, metrics.MinX, 1);
    }

    [Fact]
    public async Task ATransformedBox_CountsWhereItIsMovedTo_InTheScrollableOverflow()
    {
        var (_, document) = await LoadAsync("#s { width: 100pt; height: 100pt; overflow: auto } #w { width: 50pt; height: 20pt; transform: translateY(250pt) }", "<div id=s><div id=w></div></div>");

        var metrics = document.GetScrollMetrics(document.GetElement("s")!)!.Value;
        Assert.Equal(270, metrics.ContentHeight, 1);
        Assert.Equal(170, metrics.MaxY, 1);
    }

    [Fact]
    public async Task AShadowAndAnOutline_AreInkOverflow_NotScrollableOverflow()
    {
        var (_, document) = await LoadAsync("#s { width: 100pt; height: 100pt; overflow: auto } #w { width: 50pt; height: 20pt; box-shadow: 0 300pt 0 red; outline: 200pt solid blue }", "<div id=s><div id=w></div></div>");

        Assert.Equal(0, document.GetScrollMetrics(document.GetElement("s")!)!.Value.MaxY, 1);
    }

    [Fact]
    public async Task AFoundMatchInAScrollContainer_IsScrolledIntoView()
    {
        var options = Screen();
        options.RecordTextRuns = true;
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(
            "<div id=s>" + string.Concat(Enumerable.Range(0, 30).Select(n => $"<p>line number {n}</p>")) + "<p>needle here</p></div>",
            "<style>body { margin: 0 } #s { width: 150pt; height: 80pt; overflow: auto } p { margin: 0; height: 20pt }</style>"), options);
        var s = document.GetElement("s")!;
        var port = document.GetScrollMetrics(s)!.Value.Viewport;

        var found = document.FindText("needle");
        Assert.Single(found);
        Assert.NotEmpty(document.ScrollTextIntoView(found[0]));

        var rect = document.GetSelectionRects(found[0].Start, found[0].End)[0];
        Assert.True(rect.Y >= port.Y - 0.5 && rect.Bottom <= port.Bottom + 0.5, $"{rect} not in {port}");
        Assert.True(document.GetScrollMetrics(s)!.Value.Y > 0);
    }

    [Fact]
    public async Task TheCursorOverAScrollbar_IsTheArrow()
    {
        var (_, document) = await LoadAsync("#s { width: 100pt; height: 100pt; overflow: auto; cursor: text } .i { height: 50pt }", "<div id=s>" + Items(6) + "</div>");
        var port = document.GetScrollMetrics(document.GetElement("s")!)!.Value.Viewport;

        Assert.Equal(CursorShape.Text, document.GetCursorAt(0, port.X + 10, port.Y + 50).Shape);
        Assert.Equal(CursorShape.Default, document.GetCursorAt(0, port.Right - 5, port.Y + 12).Shape);
    }
}
