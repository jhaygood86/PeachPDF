using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class CursorTests
{
    private static (double X, double Y) Center(HtmlDocument document, string id)
    {
        var bounds = document.GetElementBounds(id)!.Value;
        return (bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    [Fact]
    public async Task ACursorKeyword_IsReportedForTheElementUnderThePoint()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div id=a style='height:40px;cursor:crosshair'></div><div id=b style='height:40px'></div>"));

        var (x, y) = Center(document, "a");
        Assert.Equal(CursorShape.Crosshair, document.GetCursorAt(0, x, y).Shape);

        (x, y) = Center(document, "b");
        Assert.Equal(CursorShape.Default, document.GetCursorAt(0, x, y).Shape);
    }

    [Fact]
    public async Task TheCursorIsInherited()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div style='cursor:grab'><div id=a style='height:40px'></div></div>"));

        var (x, y) = Center(document, "a");
        Assert.Equal(CursorShape.Grab, document.GetCursorAt(0, x, y).Shape);
    }

    [Fact]
    public async Task ImagesComeFirstWithTheirHotspot_AndTheKeywordStandsInForThem()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(
            "<div id=a style=\"height:40px;cursor:url('file:///tmp/one.png') 4 6, url(file:///tmp/two.cur), not-allowed\"></div>"));

        var (x, y) = Center(document, "a");
        var cursor = document.GetCursorAt(0, x, y);

        Assert.Equal(CursorShape.NotAllowed, cursor.Shape);
        Assert.Equal(2, cursor.Images.Count);
        Assert.Equal("file:///tmp/one.png", cursor.Images[0].Uri);
        Assert.Equal(4, cursor.Images[0].HotspotX);
        Assert.Equal(6, cursor.Images[0].HotspotY);
        Assert.Equal("file:///tmp/two.cur", cursor.Images[1].Uri);
        Assert.Null(cursor.Images[1].HotspotX);
    }

    [Fact]
    public async Task AnInvalidCursorDeclaration_IsIgnored()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div id=a style='height:40px;cursor:crosshair;cursor:banana'></div>"));

        var (x, y) = Center(document, "a");
        Assert.Equal(CursorShape.Crosshair, document.GetCursorAt(0, x, y).Shape);
    }

    [Fact]
    public async Task Auto_IsThePointerOverALink_TheTextPointerOverText_AndTheDefaultElsewhere()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<p><a id=l href='https://example.com/'>link</a> and <span id=t>plain text</span></p><div id=e style='height:40px'></div>"),
            new HtmlDocumentOptions { RecordTextRuns = true });

        var (x, y) = Center(document, "l");
        Assert.Equal(CursorShape.Pointer, document.GetCursorAt(0, x, y).Shape);

        (x, y) = Center(document, "t");
        Assert.Equal(CursorShape.Text, document.GetCursorAt(0, x, y).Shape);

        (x, y) = Center(document, "e");
        Assert.Equal(CursorShape.Default, document.GetCursorAt(0, x, y).Shape);
    }

    [Fact]
    public async Task PointerEventsNone_LetsThePointFallThroughToWhatIsBeneath()
    {
        const string under = "<div id=under style='position:absolute;left:0;top:0;width:100px;height:100px;cursor:help'></div>";
        var over = "<div id=over style='position:absolute;left:0;top:0;width:100px;height:100px;cursor:wait{0}'></div>";

        var (_, solid) = await TestDocument.LoadAsync(TestDocument.Wrap(under + string.Format(over, "")));
        var (x, y) = Center(solid, "under");
        Assert.Equal(CursorShape.Wait, solid.GetCursorAt(0, x, y).Shape);
        Assert.Equal("over", solid.GetElementAt(0, x, y)?.Id);

        var (_, hole) = await TestDocument.LoadAsync(TestDocument.Wrap(under + string.Format(over, ";pointer-events:none")));
        (x, y) = Center(hole, "under");
        Assert.Equal(CursorShape.Help, hole.GetCursorAt(0, x, y).Shape);
        Assert.Equal("under", hole.GetElementAt(0, x, y)?.Id);
    }

    [Fact]
    public async Task PointerEventsIsInherited_AndAChildCanTakeThemBack()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(
            "<div id=off style='pointer-events:none;cursor:wait'><div id=on style='height:40px;pointer-events:auto;cursor:move'></div><div id=inherits style='height:40px'></div></div><div id=behind style='cursor:help;height:40px'></div>"));

        var (x, y) = Center(document, "on");
        Assert.Equal(CursorShape.Move, document.GetCursorAt(0, x, y).Shape);

        (x, y) = Center(document, "inherits");
        Assert.NotEqual(CursorShape.Wait, document.GetCursorAt(0, x, y).Shape);
    }

    [Fact]
    public async Task ALinkWithPointerEventsNone_IsNotAnAnnotation()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(
            "<p><a href='https://example.com/a'>live</a> <a href='https://example.com/b' style='pointer-events:none'>dead</a></p>"));

        var links = document.Annotations.Links;
        Assert.Single(links);
        Assert.Equal("https://example.com/a", links[0].Uri);
    }

    [Fact]
    public async Task APointOutsideThePages_IsRejected_AndAnEmptyPageGivesTheDefault()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(""));

        Assert.Equal(CursorShape.Default, document.GetCursorAt(0, 5, 5).Shape);
        Assert.Throws<ArgumentOutOfRangeException>(() => document.GetCursorAt(5, 0, 0));
    }
}
