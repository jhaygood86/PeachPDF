using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A <c>widows</c> rewind names a box and the number of lines to keep, and is carried out after the pass
/// that asked for it. When that pass laid the box out again in the meantime, the box holds fewer lines
/// than the record it was entered with says it completed, and the rewind used to index past them.
/// </summary>
public class WidowsRewindStaleRecordTests
{
    [Fact]
    public async Task RewindRecord_ForABoxWithFewerLinesThanItCompleted_IsDeclined()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
            "<p id='p' style='width:60pt'>one two three four five six seven eight nine ten</p>"));
        var box = LayoutHarness.FindById(root, "p")!;
        var lines = box.LineBoxes.Count;
        Assert.True(lines >= 3);

        InlineBreakToken TokenCompleting(int completed) =>
            new(box, 0, [], 0, completed, LinesKeptHere: completed);

        Assert.True(HtmlContainerInt.TryRebuildForBudget(TokenCompleting(lines), box, lines - 1, out _));
        Assert.False(HtmlContainerInt.TryRebuildForBudget(TokenCompleting(lines + 2), box, lines - 1, out var rebuilt));
        Assert.Equal(TokenCompleting(lines + 2), rebuilt);
    }

    /// <summary>
    /// Nested columns in a table cell, a scroll container with a maximum height, a footnote paragraph and an
    /// absolutely positioned box, on a small page: the shape of a generated document that threw
    /// "Index was out of range" from the widows rewind.
    /// </summary>
    [Fact]
    public async Task NestedColumnsWithAScrollBoxAndAFootnote_LayOutWithoutThrowing()
    {
        var run = (int from, int count) => string.Join(' ', Enumerable.Range(from, count).Select(i => $"w677_{i}"));
        var html = $$"""
            <!DOCTYPE html><html><head><style>
            @page { size: 300pt 160pt; margin: 20pt }
            body { margin: 0; font: 10pt/12pt sans-serif }
            p { margin: 0 0 4pt }
            td { vertical-align: top }
            </style></head><body>
            <div style="columns:2;column-gap:8pt"><div style="position:relative">
              <table><tr><td><div style="columns:3;column-gap:8pt">
                <p>{{run(555, 17)}}</p>
                <div style="display:grid;grid-template-columns:1fr 1fr"><div><p>{{run(630, 6)}}</p></div></div>
              </div></td></tr></table>
              <div style="overflow:scroll;max-height:41pt"><div><p>{{run(638, 14)}}</p></div></div>
              <div><p>{{run(680, 10)}}</p></div>
              <p>{{run(912, 3)}}<span style="float:footnote">{{run(915, 4)}}</span> {{run(919, 3)}}</p>
              <div style="position:absolute;top:14pt;left:70pt;width:70pt">{{run(922, 12)}}</div>
            </div></div>
            </body></html>
            """;

        var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
            html, new PeachPDF.PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);

        Assert.NotNull(container.FragmentTree);
    }
}
