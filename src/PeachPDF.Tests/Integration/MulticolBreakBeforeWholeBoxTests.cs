using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A column that breaks before a box whole moves it on to the next column or page; an earlier column of the same
/// fill that had already laid out part of that box must not keep drawing it as well.
/// </summary>
public class MulticolBreakBeforeWholeBoxTests
{
    [Fact]
    public async Task TableCellMovedOnWhole_IsDrawnOnce()
    {
        var words = string.Join(" ", Enumerable.Range(142, 16).Select(i => $"w15_{i}"));
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt Arial } p { margin:0 0 4pt } td { vertical-align: top }</style></head><body>" +
                   "<div style='height:27pt'><div style='float:left;width:143pt;height:83pt'></div></div>" +
                   $"<div style='columns:2;column-gap:8pt'><table border='1'><p>{words} </p></table></div></body></html>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (lost, doubled) = PaintedWords.Diff(html, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }
}