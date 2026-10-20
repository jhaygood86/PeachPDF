using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A multi-column container is a formatting context root, so it keeps clear of the floats beside it (CSS 2.1 §9.5) rather than
/// laying its columns under them. Left to the line flow, a float pushed the lines inside a column past every column's extent,
/// where nothing claimed them, and their words were lost.
/// </summary>
public class MulticolBesideFloatTests
{
    private static string Page(string body, string pageSize = "300pt 250pt") =>
        $"<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page {{ size: {pageSize}; margin: 20pt }} " +
        "body { margin:0; font: 10pt/12pt Arial } p { margin:0 0 4pt }</style></head><body>" + body + "</body></html>";

    private static string Words(string series, int from, int count) =>
        string.Join(' ', Enumerable.Range(from, count).Select(i => $"w{series}_{i}"));

    // Fuzz reductions of the same bug: a heading in columns right after a float lost its text.
    [Theory]
    [InlineData("left", 108, 93)]
    [InlineData("left", 109, 130)]
    [InlineData("right", 100, 80)]
    public async Task ColumnsAfterAFloat_KeepEveryWord(string side, int width, int height)
    {
        var body = $"<div style='float:{side};width:{width}pt;height:{height}pt'></div>" +
                   "<div style='columns:3;column-gap:8pt'><h3>w94_11</h3></div>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // The container keeps the extent it was narrowed to on the pages it continues onto: the float is not beside it there,
    // and asked again the columns would have started at the narrowed left edge with the full width.
    [Fact]
    public async Task ColumnsAfterAFloat_ThatRunOnToTheNextPage_KeepTheirExtent()
    {
        var body = "<div style='float:left;width:101pt;height:53pt'></div>" +
                   $"<div style='columns:2;column-gap:8pt'><p>{Words("33", 1, 120)}</p>{Words("33", 121, 30)}</div>";

        var (visible, pages) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
        Assert.True(pages >= 2, $"expected the columns to run onto a second page, got {pages}");
    }

    // Narrowing can leave columns narrower than a word. A word wider than its column spills into the next column's extent and
    // belongs to the one it starts in; it used to be emitted by both.
    [Fact]
    public async Task WordWiderThanItsColumn_IsEmittedOnce()
    {
        var body = "<div style='float:left;width:147pt;height:23pt'></div>" +
                   "<div style='columns:3;column-gap:8pt'><p>w41_107 w41_108 w41_109</p></div>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // A float wider than its column overflows leftward into the previous column's extent; its words belong to the column
    // that holds the float, not to the one whose extent they lie in.
    [Fact]
    public async Task FloatWiderThanItsColumn_KeepsItsWords()
    {
        var body = "<div style='columns:3;column-gap:8pt'><p></p>w112_73 " +
                   "<div style='float:right;width:122pt;height:90pt'>w112_84 w112_85 </div></div>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // An absolutely positioned box whose containing block spans several columns is drawn once, where the first fragment
    // puts it, and on the page its containing block's flow is laid out on.
    [Fact]
    public async Task AbsoluteBoxInColumnsThatRunOnToANewPage_IsDrawnOnce()
    {
        var body = $"{Words("27", 44, 33)}<p>{Words("27", 77, 37)}</p>" +
                   $"<div style='columns:3;column-gap:8pt'><div style='position:relative'>{Words("27", 119, 17)} " +
                   "<div style='position:absolute;top:19pt;left:79pt;width:108pt'>w27_136 </div></div></div>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // One column is laid out by the block flow, which does not use a narrowed extent: the container stays as it was, so the
    // text still avoids the float itself.
    [Fact]
    public async Task SingleColumnContainerAfterAFloat_StillAvoidsTheFloat()
    {
        var body = "<div style='float:left;width:80pt;height:60pt'></div>" +
                   $"<div style='columns:1'><p>{Words("7", 1, 6)}</p></div>";

        var positions = await PaintedWords.PositionsAsync(Page(body));

        Assert.True(positions["w7_1"].X >= 20 + 80 - 0.5, $"the text started at x={positions["w7_1"].X}, under the float");
    }
}
