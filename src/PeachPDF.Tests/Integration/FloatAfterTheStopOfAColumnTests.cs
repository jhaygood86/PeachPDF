using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A float the flow of a column has not reached is placed by the column that resumes the flow. The column that stopped
/// before it does not draw it from the geometry a later column gave the box.
/// </summary>
public class FloatAfterTheStopOfAColumnTests
{
    private static string Words(string prefix, int from, int to) =>
        string.Join(" ", Enumerable.Range(from, to - from + 1).Select(i => $"{prefix}_{i}"));

    [Fact]
    public async Task WideFloatAfterTheLastWordsOfAColumn_IsDrawnOnce()
    {
        var body = $"<p>{Words("w112", 1, 20)} </p>{Words("w112", 21, 29)} <p>{Words("w112", 30, 55)} </p>" +
                   "<div style='columns:3;column-gap:8pt'><div style='columns:3;column-gap:8pt'><div style='float:right;width:75pt;height:129pt'></div></div>" +
                   $"{Words("w112", 73, 83)} <div style='float:right;width:122pt;height:90pt'>w112_84 w112_85 </div></div>";
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt Arial } p { margin:0 0 4pt }</style></head><body>" + body + "</body></html>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (lost, doubled) = PaintedWords.Diff(html, visible);

        Assert.Empty(doubled);
        Assert.Empty(lost);
    }
}