using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A child that reaches past the end of its block (a large relative offset, a negative bottom margin, content
/// overflowing a fixed-height child) does not change where the block ends (CSS 2.1 §9.4.3 and §10.5), so the content
/// after the block is still placed after it and keeps every word (css-break-3 §4.4). The break taken inside such a
/// child used to end the layout pass beyond the parent, and the content after it was placed on a page already emitted.
/// </summary>
public class ChildReachingPastItsParentTests
{
    private static string Para(string prefix, int count) =>
        string.Concat(Enumerable.Range(1, count).Select(i => $"<p>{prefix}_{i}</p>"));

    private static string Document(string body) =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 200pt; margin: 20pt } " +
        "body { margin:0; font: 10pt/12pt Arial } p { margin:0 0 4pt }</style></head><body>" + body + "</body></html>";

    // Words are w<series>_<n>: w1 before, w2 in the block, w3 its odd last child, w4 after.
    private static string Words(string series, int count) =>
        string.Concat(Enumerable.Range(1, count).Select(i => $"<p>w{series}_{i}</p>"));

    public static TheoryData<string, string> Cases => new()
    {
        { "relative offset", "<div style='position:relative;top:300pt'>w3_1</div>" },
        { "negative margin-bottom", "<p style='margin-bottom:-100pt'>w3_1</p>" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ContentAfterABlock_KeepsEveryWord_WhenALastChildReachesPastTheBlock(string name, string lastChild)
    {
        var body = Words("1", 5) + "<div>" + Words("2", 20) + lastChild + "</div>" + Words("4", 6);

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Document(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.True(lost.Count == 0, $"{name}: lost {string.Join(' ', lost)}");
        Assert.Empty(doubled);
    }

    // A fixed-height child holding more content than fits it: the content overflows visibly and the block ends where
    // the fixed height says.
    [Fact]
    public async Task ContentAfterABlock_KeepsEveryWord_WhenAFixedHeightChildOverflows()
    {
        var body = Words("1", 5) + "<div><div style='height:30pt'>" + Words("2", 20) + "</div></div>" + Words("4", 6);

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Document(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // A multi-column container whose only child is a float reaching past the page foot (found by a generated corpus):
    // the float's own passes leave the cursor on a later band, and the paragraph after the container, placed beside
    // the float on the first page, had its last line across the foot.
    [Fact]
    public async Task ContentAfterAColumnsContainerHoldingOnlyATallFloat_KeepsEveryWord()
    {
        var body = "<div style='height:110pt'></div><div style='columns:2;column-gap:8pt'>" +
                   "<div style='float:left;width:47pt;height:135pt'>w1_1 w1_2</div></div>" +
                   "<h3>w2_1 w2_2 w2_3 w2_4 w2_5 w2_6 w2_7</h3><p>" +
                   string.Join(' ', Enumerable.Range(1, 27).Select(i => $"w3_{i}")) + "</p>";
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt Arial } p { margin:0 0 4pt }</style></head><body>" + body + "</body></html>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // A heading set tighter than its font has words that poke above their line box, so a heading at the top of a page has words that start in the page before. Asked of the word's top, the cursor in the later page and the word in the earlier one, they were held to the earlier page's foot and broke again, and the page's last line was left across its own foot and clipped (found by a generated corpus).
    [Fact]
    public async Task HeadingAtAPageTop_DoesNotCostTheLastLineItsBreak()
    {
        static string Run(int from, int to) => string.Join(' ', Enumerable.Range(from, to - from + 1).Select(i => $"w88_{i}")) + " ";

        var body = $"<h3>{Run(29, 34)}</h3>" +
                   $"<table border='1'><td><div style='columns:3;column-gap:8pt'><h3>{Run(35, 37)}</h3><h3>{Run(40, 42)}</h3>{Run(43, 51)}</div></td>{Run(52, 52)}</table>" +
                   $"{Run(55, 55)}<table border='1'><td><h3>{Run(61, 65)}</h3></td>{Run(66, 66)}</table>{Run(71, 85)}" +
                   $"<p>{Run(86, 117)}</p>{Run(118, 147)}";
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt Arial } p { margin:0 0 4pt } td { vertical-align: top }</style></head><body>" +
                   body + "</body></html>";

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        // The cell's own first line sits a point above the next page's top edge, which the strict check also reports and
        // which is a separate matter: it is the paragraph's words after the table that must not be clipped.
        Assert.DoesNotContain(lost, word => int.Parse(word[4..]) > 66);
        Assert.Empty(doubled);
    }

    // The same documents with an auto-height scroll container in place of the plain div, which breaks like a block.
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ContentAfterAScrollContainer_KeepsEveryWord_WhenALastChildReachesPastIt(string name, string lastChild)
    {
        var body = Words("1", 5) + "<div style='overflow:hidden'>" + Words("2", 20) + lastChild + "</div>" + Words("4", 6);

        var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Document(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.True(lost.Count == 0, $"{name}: lost {string.Join(' ', lost)}");
        Assert.Empty(doubled);
    }
}
