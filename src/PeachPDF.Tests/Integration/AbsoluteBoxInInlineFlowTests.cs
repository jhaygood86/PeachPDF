using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// An absolutely positioned box laid out from its containing block's inline flow (a relatively positioned block that holds
/// nothing else) runs as passes of its own and so breaks between its lines at a page foot, like any other absolute box
/// (CSS Fragmentation 3 §4.4). Laid out with the fragmentainer detached it was one unbroken run: its last line crossed the
/// foot and was clipped away.
/// </summary>
public class AbsoluteBoxInInlineFlowTests
{
    private static string Words(string series, int from, int count) =>
        string.Join(' ', Enumerable.Range(from, count).Select(i => $"w{series}_{i}"));

    // A fuzz reduction: 30 words of text, two spacers and a heading put the wrapper low on a 250pt page, and the box's third
    // line meets the foot at 230pt.
    [Fact]
    public async Task AbsoluteBoxLowOnThePage_BreaksBeforeALineThatCrossesTheFoot()
    {
        // Words as wide as the report's (one to a line in the 66pt box), so the box has three lines.
        var body = $"{Words("260", 57, 30)}<div style='height:27pt'></div><h3>{Words("260", 142, 5)}</h3><div style='height:18pt'></div>" +
                   "w260_197<div style='position:relative'><div style='position:absolute;top:16pt;left:57pt;width:66pt'>w260_215 w260_216 w260_217</div></div>";
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt Arial }</style></head><body>" + body + "</body></html>";

        var (visible, pages) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
        Assert.Equal(2, pages);
    }

    // The same box higher up, where it fits whole, is not affected: one page and every word.
    [Fact]
    public async Task AbsoluteBoxThatFits_IsLaidOutWhole()
    {
        var body = "w1_1<div style='position:relative'><div style='position:absolute;top:16pt;left:57pt;width:66pt'>w260_215 w260_216 w260_217</div></div>";
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt Arial }</style></head><body>" + body + "</body></html>";

        var (visible, pages) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (lost, _) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Equal(1, pages);
    }
}
