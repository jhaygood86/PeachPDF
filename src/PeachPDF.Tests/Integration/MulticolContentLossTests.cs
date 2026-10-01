using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A multi-column container never loses or doubles content at a page break (css-break-3 §4.4: content must not be
/// lost at a break; css-multicol-1 §2). Each case is a fuzz-found reduction, kept with its own markup because the
/// line breaks decide which line meets a page edge.
/// </summary>
public class MulticolContentLossTests
{
    private static string Page(string body, string pageSize = "300pt 250pt") =>
        $"<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page {{ size: {pageSize}; margin: 20pt }} " +
        "body { margin:0; font: 10pt/12pt Arial } p { margin:0 0 4pt }</style></head><body>" + body + "</body></html>";

    // #1538: a single-column container is still a multicol container (css-multicol-1 §2), and a paginated one
    // continues on the next page. The single-column fallback laid every child out in one pass and recorded no break.
    [Theory]
    [InlineData("columns: 1")]
    [InlineData("column-count: 1")]
    [InlineData("columns: 2; column-gap: 8pt")]
    [InlineData("")]
    public async Task SingleColumnContainerTallerThanAPage_PaginatesWithEveryWord(string style)
    {
        static string Para(int n) => "<p>" + string.Join(' ', Enumerable.Range(1, 30).Select(i => $"w{n}_{i}")) + "</p>";
        var body = $"<div style=\"{style}\">{Para(1)}{Para(2)}{Para(3)}</div>";

        var (painted, pages) = await PaintedWords.LayOutAndPaintAsync(Page(body, "300pt 170pt"));
        var (lost, doubled) = PaintedWords.Diff(body, painted);

        Assert.Empty(lost);
        Assert.Empty(doubled);
        Assert.Equal(2, pages);
    }

    // #1538 variants: spacer above, a relatively positioned wrapper, one long paragraph.
    [Theory]
    [InlineData("<div style='height:30pt'></div><div style='columns: 1'>{0}</div>")]
    [InlineData("<div style='columns: 1'><div style='position:relative'>{0}</div></div>")]
    public async Task SingleColumnContainerVariants_KeepEveryWord(string template)
    {
        var inner = string.Concat(Enumerable.Range(1, 3)
            .Select(n => "<p>" + string.Join(' ', Enumerable.Range(1, 30).Select(i => $"w{n}_{i}")) + "</p>"));
        var body = template.Replace("{0}", inner);

        var (painted, _) = await PaintedWords.LayOutAndPaintAsync(Page(body, "300pt 170pt"));
        var (lost, doubled) = PaintedWords.Diff(body, painted);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // #1532: a container starting where less than a line of room is left moves to the next page, as a plain block
    // does (css-break-3 §4.4). Twelve 16pt fillers leave 8pt of a 200pt band; ten, eleven and thirteen are controls.
    [Theory]
    [InlineData(10, 1)]
    [InlineData(11, 1)]
    [InlineData(12, 2)]
    [InlineData(13, 2)]
    public async Task ContainerStartingTooCloseToThePageFoot_MovesToTheNextPageWithItsText(int fillers, int pages)
    {
        var body = string.Concat(Enumerable.Range(1, fillers).Select(i => $"<p>w0_{i}</p>"))
                   + "<div style=\"columns: 2; column-gap: 8pt\"><p>w1_1 w1_2 w1_3 w1_4 w1_5</p></div>";

        var (painted, pageCount) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body, "300pt 240pt"));
        var (lost, doubled) = PaintedWords.Diff(body, painted);

        Assert.Empty(lost);
        Assert.Empty(doubled);
        Assert.Equal(pages, pageCount);
    }

    // #1486: a float that is the container's only child left after a break made the resumed pass look up a real
    // child that did not exist, and layout threw ArgumentOutOfRangeException.
    [Fact]
    public async Task ContainerWhoseResumeBoundaryIsAFloat_DoesNotThrow_AndKeepsEveryWord()
    {
        const string body = """
            <h3>w50062_540 w50062_541 w50062_542 w50062_543 w50062_544 w50062_545</h3>
            w50062_977 w50062_978 w50062_979 w50062_980 w50062_981
            <h3>w50062_982 w50062_983 w50062_984 w50062_985 w50062_986 w50062_987 w50062_988 w50062_989 w50062_990 w50062_991</h3>
            w50062_992 w50062_993 w50062_994 w50062_995 w50062_996<div style="column-count:2"><h3>w50062_1096</h3><div style="margin-bottom:10pt;padding:5pt;float:left;width:120pt"><p>w50062_1193 w50062_1194 w50062_1195 w50062_1196 w50062_1197 w50062_1198 w50062_1199 w50062_1200</p>w50062_1201 w50062_1202 w50062_1203 w50062_1204 w50062_1205 w50062_1206 w50062_1207 w50062_1208 w50062_1209 w50062_1210</div></div>
            """;

        var (painted, _) = await PaintedWords.LayOutAndPaintAsync(Page(body.ReplaceLineEndings("")));
        var (lost, doubled) = PaintedWords.Diff(body, painted);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }
}
