using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// An inline-block or inline-table "may also be considered monolithic" (css-break-3 §4.1), and a monolithic box that fits a fresh
/// fragmentainer is moved there (§4.4) rather than sliced where it crosses the foot, which clipped a line at the foot of the page.
/// </summary>
public class AtomicInlineAtThePageFootTests
{
    private static string Page(string body) =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
        "body { margin:0; font-family: Arial; font-size: 10pt; line-height: 12pt }</style></head><body>" + body + "</body></html>";

    // The table's cell lines reach y=229, 1pt above the foot of a page whose content ends at 230: the last of them is a line that
    // cannot be placed there, and the issue's document lost two words (w384_391, w384_395) to the slice.
    [Fact]
    public async Task InlineBlockTableCrossingThePageFoot_MovesToTheNextPageWhole()
    {
        var html = Page("<div style='height:145.5pt'>w384_1</div><div style='display:inline-block;width:150pt'><table border='1'><tr>" +
                        "<td>w384_387 w384_388 lorem w384_389 w384_390 w384_391 w384_392</td>" +
                        "<td>w384_393 lorem w384_394 lorem w384_395</td></tr></table></div>");

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (lost, doubled) = PaintedWords.Diff(html, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);

        var positions = await PaintedWords.PositionsAsync(html);
        Assert.Equal(1, positions["w384_391"].Page);
        Assert.Equal(1, positions["w384_395"].Page);
    }

    [Fact]
    public async Task InlineBlockThatFitsOnThePage_StaysWhereItIs()
    {
        var html = Page("<div style='height:20pt'>w384_1</div><div style='display:inline-block;width:150pt'>w384_2 w384_3 w384_4</div>");

        var positions = await PaintedWords.PositionsAsync(html);

        Assert.Equal(0, positions["w384_2"].Page);
        Assert.InRange(positions["w384_2"].Y, 40, 46);
    }
}