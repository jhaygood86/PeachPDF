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
