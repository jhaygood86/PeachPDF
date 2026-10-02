using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A table row in a balanced multi-column container whose cell holds a float does not divide, so the even share the
/// columns are balanced to is shorter than the row. The fill that finished at the full budget is the answer then.
/// </summary>
public class MulticolBalanceFallbackTests
{
    [Fact]
    public async Task TableCellWithAFloat_FinishesInsteadOfDeferringPageAfterPage()
    {
        const string html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                            "body { margin:0; font: 10pt/12pt Arial }</style></head><body>" +
                            "<div style='columns:3;column-gap:8pt'><table><tr><td><div style='float:right;width:72pt;height:69pt'></div>w235_45 </td></tr></table></div></body></html>";

        // Without the fallback the container is deferred to the next page for ever (a hundred thousand passes), so a
        // render that does not return is the failure.
        var (visible, pages) = await Task.Run(() => PaintedWords.LayOutAndCollectVisibleAsync(html))
            .WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        var (lost, doubled) = PaintedWords.Diff(html, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
        Assert.Equal(1, pages);
    }
}