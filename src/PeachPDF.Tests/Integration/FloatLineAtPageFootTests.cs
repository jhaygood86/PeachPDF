using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A float among inline content is laid out as one unbroken run and each page shows the slice that falls in it. A line
/// that straddles a page's foot is in neither slice, so it was drawn on no page; the line moves to the next page whole
/// instead (css-break-3 §4.1: a line box is monolithic).
/// </summary>
public class FloatLineAtPageFootTests
{
    private static string Page(string body) =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
        "body { margin:0; font: 10pt/12pt Arial }</style></head><body>" + body + "</body></html>";

    [Theory]
    [InlineData(180)]
    [InlineData(190)]
    [InlineData(196)]
    public async Task InlineFloatContinuingOntoTheNextPage_KeepsTheLineThatCrossesTheFoot(int spacer)
    {
        var body = $"<div style='height:{spacer}pt'></div>w9_1 " +
                   "<div style='float:left;width:53pt;height:60pt'>w9_72 w9_73 w9_74 w9_75 w9_76 w9_77</div>w9_2 w9_3 ";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // The lines that fit stay where they are: only the one that would cross the foot moves.
    [Fact]
    public async Task InlineFloat_KeepsItsLinesThatFitTheFirstPage()
    {
        var body = "<div style='height:180pt'></div>w9_1 " +
                   "<div style='float:left;width:53pt;height:60pt'>w9_72 w9_73 w9_74 w9_75 w9_76 w9_77</div>w9_2 ";

        var positions = await PaintedWords.PositionsAsync(Page(body));

        Assert.InRange(positions["w9_72"].Y, 199.5, 201);
        Assert.InRange(positions["w9_73"].Y, 211.5, 213);
        Assert.Equal(1, positions["w9_74"].Page);
        Assert.InRange(positions["w9_74"].Y, 19.5, 21);
    }
}
