using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A multi-column container narrowed to sit beside a float in one outer column, and continuing into the next outer
/// column, starts at that column's own x: the float is not beside it there.
/// </summary>
public class NestedMulticolBesideFloatContinuationTests
{
    [Fact]
    public async Task ContinuationInNextOuterColumn_IsNotPlacedOverTheFirstColumn()
    {
        string Words(int from, int to) => string.Join(" ", Enumerable.Range(from, to - from + 1).Select(i => $"w222_{i}"));

        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt Arial }</style></head><body>" +
                   "<div style='columns:2;column-gap:8pt'><div style='float:left;width:53pt;height:109pt'></div>" +
                   $"<div style='columns:3;column-gap:8pt'>{Words(127, 154)} <p></p></div></div></body></html>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (lost, doubled) = PaintedWords.Diff(html, visible);

        Assert.Empty(doubled);
        Assert.Empty(lost);
    }
}