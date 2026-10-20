using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A float that follows the last word of the line a fragmentainer keeps is placed there. The flow that resumes at the next word
/// does not place it again, though its position in the flow is that word's.
/// </summary>
public class FloatAtTheResumeBoundaryTests
{
    private static string Words(string prefix, int from, int to) =>
        string.Join(" ", Enumerable.Range(from, to - from + 1).Select(i => $"{prefix}_{i}"));

    [Fact]
    public async Task FloatAfterTheLastWordOfAKeptLine_IsDrawnOnce()
    {
        var body = $"<p>{Words("w112", 1, 20)} </p>{Words("w112", 21, 29)} <p>{Words("w112", 30, 55)} </p>" +
                   "<div style='columns:3;column-gap:8pt'><div style='columns:3;column-gap:8pt'>" +
                   $"{Words("w112", 56, 65)} <div style='float:right;width:75pt;height:129pt'>w112_66 </div>w112_67 </div></div>";
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt Arial } p { margin:0 0 4pt }</style></head><body>" + body + "</body></html>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (_, doubled) = PaintedWords.Diff(html, visible);

        Assert.Empty(doubled);
    }
}