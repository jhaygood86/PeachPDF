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
