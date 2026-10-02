using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A table cell whose flow restarts at its first word in the carried record has put nothing in the column run that ended: what an
/// earlier column laid out of it is not drawn there as well.
/// </summary>
public class MulticolTableCellRestartTests
{
    [Fact]
    public async Task CellThatRestartsAtItsFirstWord_IsDrawnOnce()
    {
        var words = string.Join(" ", Enumerable.Range(142, 16).Select(i => $"w15_{i}"));
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt Arial } td { vertical-align: top }</style></head><body>" +
                   "<div style='height:27pt'></div><div style='columns:2;column-gap:8pt'>" +
                   $"<table border='1'><div>{words}</div><td>w15_158</td></table></div></body></html>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (lost, doubled) = PaintedWords.Diff(html, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }
}