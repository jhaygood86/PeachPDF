using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A float wider than the inner column it sits in sticks out to the left of that column's extent. The column that holds it
/// draws it, once, wherever the later fills of the container under other outer columns carry the same box.
/// </summary>
public class NestedWideFloatClaimTests
{
    private static string Page(string body) =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
        "body { margin:0; font: 10pt/12pt Arial } p { margin:0 0 4pt }</style></head><body>" + body + "</body></html>";

    private static string Words(string prefix, int from, int to) =>
        string.Join(" ", Enumerable.Range(from, to - from + 1).Select(i => $"{prefix}_{i}"));

    private static string NestedContainer(string prefix) =>
        "<div style='columns:3;column-gap:8pt'><div style='columns:3;column-gap:8pt'>" +
        $"<p>{Words(prefix, 56, 65)} </p><div style='float:right;width:75pt;height:129pt'>{prefix}_66 </div>{Words(prefix, 67, 72)} </div>" +
        $"{Words(prefix, 73, 78)} </div>";

    // With enough above it that the inner container's third column sits at the foot of the outer column's band.
    [Fact]
    public async Task FloatWiderThanItsInnerColumn_IsNotLost()
    {
        var body = $"<p>{Words("w112", 1, 20)} </p>{Words("w112", 21, 29)} <p>{Words("w112", 30, 55)} </p>" + NestedContainer("w112");

        var positions = await PaintedWords.PositionsAsync(Page(body));

        Assert.True(positions.ContainsKey("w112_66"), "the float's word is in no fragment");
    }

    [Fact]
    public async Task FloatWiderThanItsInnerColumn_IsDrawnOnce()
    {
        var html = Page("<div style='height:140pt'></div>" + NestedContainer("w2"));

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (_, doubled) = PaintedWords.Diff(html, visible);

        Assert.Empty(doubled);
    }
}