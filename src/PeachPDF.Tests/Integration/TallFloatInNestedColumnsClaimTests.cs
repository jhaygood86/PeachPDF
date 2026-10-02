using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A float taller than the page, in columns nested in columns, is one box with one position. A container with a fragment in each of
/// several outer columns is walked once per fragment, and the float's words are drawn by the first fragment that claims them.
/// </summary>
public class TallFloatInNestedColumnsClaimTests
{
    private static string Words(int from, int to) =>
        string.Join(" ", Enumerable.Range(from, to - from + 1).Select(i => $"w30_{i}"));

    [Fact]
    public async Task FloatTallerThanThePageInNestedColumns_IsDrawnOnce()
    {
        var body = "<div style='float:left;width:121pt;height:167pt'></div>" + Words(31, 36) + " " +
                   $"<h3>{Words(53, 59)} </h3><div style='columns:3;column-gap:8pt'><div style='height:29pt'>{Words(60, 80)} </div>" +
                   $"<div style='position:relative'><div style='position:absolute;top:63pt;left:34pt;width:82pt'>{Words(81, 91)} </div></div></div>" +
                   $"<div style='columns:2;column-gap:8pt'><div style='columns:3;column-gap:8pt'><h3>{Words(95, 100)} </h3>" +
                   "<div style='float:right;width:43pt;height:507pt'>w30_101 </div></div></div>";
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt Arial } p { margin:0 0 4pt } td { vertical-align: top }</style></head><body>" + body + "</body></html>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (_, doubled) = PaintedWords.Diff(html, visible);

        Assert.Empty(doubled);
    }
}