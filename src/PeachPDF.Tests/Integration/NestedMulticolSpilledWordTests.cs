using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A word wider than a narrow inner column spills into the neighbouring outer column. Only the column it starts in
/// draws it, even when the container holding it is filled once under each of two outer columns.
/// </summary>
public class NestedMulticolSpilledWordTests
{
    [Fact]
    public async Task WordSpillingAcrossOuterColumns_IsDrawnOnce()
    {
        string Words(int from, int to) => string.Join(" ", Enumerable.Range(from, to - from + 1).Select(i => $"w252_{i}"));

        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt Arial }</style></head><body><div style='height:72pt'></div>" +
                   $"<div style='columns:3;column-gap:8pt'>{Words(150, 157)} <div style='columns:3;column-gap:8pt'>{Words(190, 205)}<p></p></div></div></body></html>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (_, doubled) = PaintedWords.Diff(html, visible);

        Assert.Empty(doubled);
    }
}