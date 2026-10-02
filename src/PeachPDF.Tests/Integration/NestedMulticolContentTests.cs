using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A multi-column container nested in another one's column is walked once per outer column when its fragments are built.
/// Each word must be drawn by one column only (css-multicol-1 section 2): the container's content was drawn a second time,
/// at its final position, by every outer column that did not hold it.
/// </summary>
public class NestedMulticolContentTests
{
    private static string Page(string body) =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
        "body { margin:0; font: 10pt/12pt Arial } p { margin:0 0 4pt }</style></head><body>" + body + "</body></html>";

    private static string Words(string series, int from, int to) =>
        string.Join(' ', Enumerable.Range(from, to - from + 1).Select(i => $"w{series}_{i}")) + " ";

    // Bare text and a paragraph in an inner container of two columns, in the first of three outer columns.
    [Fact]
    public async Task InnerContainerWithBareText_DrawsEachWordOnce()
    {
        var body = $"<div style='columns:3;column-gap:8pt'><div style='columns:2;column-gap:8pt'>{Words("76", 36, 67)}<p>{Words("76", 68, 70)}</p></div></div>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // Three inner columns narrower than a word: a word spilling out of its column into the next outer column's.
    [Fact]
    public async Task InnerColumnsNarrowerThanAWord_DoNotHandTheWordToTheNextOuterColumn()
    {
        var body = $"<div style='columns:3;column-gap:8pt'><div style='columns:3;column-gap:8pt'>{Words("118", 17, 31)} <p>{Words("118", 32, 66)}</p></div></div>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // The same nesting continuing onto a second page keeps the words of its first column there: they are not lost.
    [Fact]
    public async Task InnerContainerContinuingOntoTheNextPage_KeepsEveryWord()
    {
        var body = $"{Words("43", 16, 24)}<p>{Words("43", 29, 38)}</p><p>{Words("43", 39, 73)}</p>" +
                   $"<div style='height:50pt'></div><div style='columns:3;column-gap:8pt'><div style='columns:2;column-gap:8pt'><p>{Words("43", 74, 109)}</p></div></div>";

        var (visible, pages) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
        Assert.True(pages >= 2, $"expected the columns to run onto a second page, got {pages}");
    }
}
