using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

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

    // A float that runs into the next page's column takes the in-flow content after it with it. That content was
    // placed from the bottom of the paragraph before the float - in the column the fill had left, a page back - so it
    // was laid out there and drawn on no page (css-break-3 §4.4: no content may be lost at a break). Each case is a
    // reduction of a generated document; the markup is kept as it is because the line breaks decide where the float
    // meets the page foot.
    [Theory]
    [InlineData(
        "<div style='height:60pt'></div><div style='columns:2'><p style='margin:0'>w9000_1 w9000_2 w9000_3 w9000_4 w9000_5 w9000_6 w9000_7 w9000_8 w9000_9 w9000_10</p><div style='float:left'>w9000_11 w9000_12 w9000_13 w9000_14 w9000_15 w9000_16 w9000_17 w9000_18 w9000_19 w9000_20</div><p style='margin:0'>w9000_21</p></div>", "300pt 160pt")]
    [InlineData(
        "<p>w9000_1</p><div style='columns:3'><div style='float:right'>w9000_2 w9000_3 w9000_4 w9000_5 w9000_6 w9000_7 w9000_8 w9000_9 w9000_10 w9000_11 w9000_12 w9000_13 w9000_14 w9000_15 w9000_16 w9000_17 w9000_18 w9000_19 w9000_20 w9000_21 w9000_22 w9000_23 w9000_24 w9000_25 w9000_26 w9000_27 w9000_28 w9000_29 w9000_30 w9000_31 w9000_32 w9000_33 w9000_34 w9000_35 w9000_36 w9000_37 w9000_38 w9000_39 w9000_40 w9000_41 w9000_42 w9000_43 w9000_44 w9000_45 w9000_46 w9000_47 w9000_48 w9000_49 w9000_50</div><p>w9000_51 w9000_52 w9000_53 w9000_54 w9000_55 w9000_56 w9000_57 w9000_58</p></div>", "300pt 200pt")]
    [InlineData(
        "<p style=''></p><p style='margin:0 0 3pt'> w79_143 w79_144 w79_145 w79_146 w79_147 w79_148 </p><div style='columns:2'><p style='margin:0 0 3pt'> w79_197 w79_198 w79_199 w79_200 w79_201 </p><p style='margin:0 0 3pt'> w79_204 w79_205 w79_206 w79_207 w79_208 </p><div style='float:left'> w79_221 w79_222 w79_223 w79_224 w79_225 </div><h1 style='font-size:15pt'> w79_227 </h1></div>", "300pt 160pt")]
    [InlineData(
        "<div style='height:60pt'></div><div style='columns:2'><p style='margin:0'>w9000_1 w9000_2 w9000_3 w9000_4 w9000_5 w9000_6 w9000_7 w9000_8 w9000_9 w9000_10</p><div style='float:left'>w9000_11 w9000_12 w9000_13 w9000_14 w9000_15 w9000_16 w9000_17 w9000_18 w9000_19 w9000_20 w9000_21 w9000_22 w9000_23 w9000_24 w9000_25 w9000_26</div><div style='display:none'>hidden</div> <p style='margin:0'>w9000_31 w9000_32</p></div>", "300pt 160pt")]
    [InlineData(
        "<div style='columns:2'><div style='float:right'><p>w5031_474 w5031_475 w5031_476 w5031_477 w5031_478 w5031_479 w5031_480 w5031_481</p></div><p>w5031_503 w5031_504 w5031_505</p><div style='float:right'></div></div>", "200pt 250pt")]
    public async Task ContentAfterAFloatThatContinuesInTheNextColumn_KeepsEveryWord(string body, string pageSize)
    {
        var html = $"<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page {{ size: {pageSize}; margin: 20pt }} " +
                   "body { margin:0; font: 12pt Arial }</style></head><body>" + body + "</body></html>";

        var (painted, _) = await PaintedWords.LayOutAndPaintAsync(html);
        var (lost, doubled) = PaintedWords.Diff(body, painted);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // Reductions of generated documents (#2312, #2319, #2320): text, a second float, or a table cell around a float after a float that
    // continues in the next column. The documents are verbatim, default paragraph margins included.
    [Theory]
    [InlineData("<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page{size:300pt 160pt;margin:20pt} body{margin:0;font:12pt Arial}</style></head><body><div style='height:60pt'></div><div style='columns:2'><p style='margin:0'>w9000_1 w9000_2 w9000_3 w9000_4 w9000_5 w9000_6 w9000_7 w9000_8 w9000_9 w9000_10</p><div style='float:left'>w9000_11 w9000_12 w9000_13 w9000_14 w9000_15 w9000_16 w9000_17 w9000_18 w9000_19 w9000_20</div>w9000_21 w9000_22 <p style='margin:0'>w9000_23 w9000_24</p></div></body></html>", "2312-text")]
    [InlineData("<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page{size:300pt 160pt;margin:20pt} body{margin:0;font:12pt Arial}</style></head><body><div style='height:60pt'></div><div style='columns:2'><p style='margin:0'>w9000_1 w9000_2 w9000_3 w9000_4 w9000_5 w9000_6 w9000_7 w9000_8 w9000_9 w9000_10</p><div style='float:left'>w9000_11 w9000_12 w9000_13 w9000_14 w9000_15 w9000_16 w9000_17 w9000_18 w9000_19 w9000_20</div><span>w9000_21 w9000_22</span> <p style='margin:0'>w9000_23 w9000_24</p></div></body></html>", "2312-span")]
    [InlineData("<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page{size:300pt 160pt;margin:20pt} body{margin:0;font:12pt Arial}</style></head><body><div style='height:60pt'></div><div style='columns:2'><p style='margin:0'>w9000_1 w9000_2 w9000_3 w9000_4 w9000_5 w9000_6 w9000_7 w9000_8 w9000_9 w9000_10</p><div style='float:left'>w9000_11 w9000_12 w9000_13 w9000_14 w9000_15 w9000_16 w9000_17 w9000_18 w9000_19 w9000_20</div>w9000_21 w9000_22 <div style='float:right'>w9000_25</div> <p style='margin:0'>w9000_23 w9000_24</p></div></body></html>", "float-text-float")]
    [InlineData("<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page{size:200pt 120pt;margin:20pt}body{margin:0;font:10pt Arial}</style></head><body><div style=\"columns:2\"><p style=\"\"> w7043_139 w7043_140 w7043_141 w7043_142 w7043_143  w7043_145 </p><div style=\"float:left\"> w7043_198 w7043_199 w7043_200 w7043_201 w7043_202 w7043_203 w7043_204 w7043_205 w7043_206 w7043_207 w7043_208 w7043_209 w7043_210 w7043_211 w7043_212 w7043_213 w7043_214 w7043_215 w7043_216 </div><div style=\"float:left\"><p style=\"\"> w7043_316 </p></div><p style=\"\"> w7043_318 </p></div></body></html>", "2319")]
    [InlineData("<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page{size:200pt 120pt;margin:20pt}body{margin:0;font:10pt Arial}</style></head><body><div style=\"columns:2\"><p style=\"\"> w7043_139 w7043_140 w7043_141 w7043_142 w7043_143  w7043_145 </p><div style=\"float:left\"> w7043_198 w7043_199 w7043_200 w7043_201 w7043_202 w7043_203 w7043_204 w7043_205 w7043_206 w7043_207 w7043_208 w7043_209 w7043_210 w7043_211 w7043_212 w7043_213 w7043_214 w7043_215 w7043_216 </div><div style=\"float:right\">w7043_300</div><div style=\"float:left\"><p style=\"\"> w7043_316 </p></div><p style=\"\"> w7043_318 </p></div></body></html>", "2319-right-between")]
    [InlineData("<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page{size:300pt 250pt;margin:20pt}body{margin:0;font:10pt Arial}</style></head><body><div style=\"columns:3\"><table style=\"\"><tbody><tr><td style=\"\"><div style=\"display:grid\"><div style=\"border:1pt dotted #888\"><div style=\"float:right\"><p style=\"\"> w6045_757 w6045_758 w6045_759 w6045_760 w6045_761 w6045_762 w6045_763 w6045_764 w6045_765 w6045_766 w6045_767 w6045_768 </p><p style=\"\"> w6045_769 w6045_770 w6045_771 w6045_772 w6045_773 w6045_774 w6045_775 w6045_776 w6045_777 w6045_778 w6045_779 w6045_780 w6045_781 w6045_782 w6045_783 w6045_784 w6045_785 w6045_786 w6045_787 w6045_788 w6045_789 w6045_790 w6045_791 w6045_792 w6045_793 w6045_794 w6045_795 w6045_796 w6045_797 w6045_798 w6045_799 w6045_800 w6045_801 w6045_802 w6045_803 </p><p style=\"margin:0 0 3pt\"> w6045_804 w6045_805 w6045_806 w6045_807 w6045_808 w6045_809 w6045_810 w6045_811 w6045_812 w6045_815 w6045_816 w6045_817 w6045_818 w6045_819 w6045_820 w6045_821 w6045_822 w6045_823 w6045_824 w6045_825 w6045_826 w6045_827 w6045_828 w6045_829 w6045_830 w6045_831 w6045_832 w6045_833 w6045_834 w6045_835 w6045_836 w6045_837 w6045_838 w6045_839 w6045_840 w6045_841 w6045_842 w6045_843 w6045_844 </p></div><p style=\"\"> w6045_848  w6045_872 </p></div></div></td></tr></tbody></table></div></body></html>", "2320-1-doubles")]
    public async Task WordsAfterFloatsThatContinueInTheNextColumn_AreKept(string html, string name)
    {
        var (painted, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (lost, doubled) = PaintedWords.Diff(html, painted);

        Assert.True(lost.Count == 0, $"{name}: lost {string.Join(' ', lost)}");
        Assert.True(doubled.Count == 0, $"{name}: doubled {string.Join(' ', doubled)}");
    }

    // #1554: a float wider than its column, in a multi-column container inside another one. The lines beside it have
    // no room, were pushed past every column's inline extent and so claimed by none of them, and were lost. They move
    // below the float instead (CSS 2.1 §9.5), inside the column the float is in.
    [Theory]
    [InlineData(146, true)]
    [InlineData(146, false)]
    [InlineData(40, true)]
    public async Task FloatWiderThanItsColumn_InNestedColumns_KeepsEveryWord(int floatWidth, bool nested)
    {
        var inner = $"<div style=\"float: left; width: {floatWidth}pt; height: 37pt\">w1_1 w1_2 w1_3 w1_4</div>" +
                    "<p>" + string.Join(' ', Enumerable.Range(1, 24).Select(i => $"w2_{i}")) + "</p>";
        var body = nested
            ? $"<div style=\"columns: 2; column-gap: 8pt\"><div style=\"columns: 2; column-gap: 8pt\">{inner}</div></div>"
            : $"<div style=\"columns: 2; column-gap: 8pt\">{inner}</div>";

        var (painted, _) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
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

    // #1561: a balanced container's column band starts as an even share of its content, shorter than one line, so a
    // line that crossed the page's foot was "too tall for any column" and overflowed it, clipped. A container whose
    // first line does not fit what is left of the page moves to the next one, whatever follows the spacer.
    [Theory]
    [InlineData(185, "<p>w9_71 w9_72</p>")]
    [InlineData(199, "<div>w9_71 w9_72</div>")]
    [InlineData(200, "<div>w9_71 w9_72</div>")]
    [InlineData(190, "<p>w9_71 w9_72</p>")]
    [InlineData(185, "<h3>w9_71</h3>")]
    public async Task SingleLineContainerCrossingThePageFoot_MovesToTheNextPage(int spacer, string content)
    {
        var body = $"<div style='height:{spacer}pt'></div><div style='columns:3;column-gap:8pt'>{content}</div>";

        var (visible, pages) = await PaintedWords.LayOutAndCollectVisibleAsync(Page(body));
        var (lost, doubled) = PaintedWords.Diff(body, visible);

        Assert.Empty(lost);
        Assert.Empty(doubled);
    }

    // The same container with room for the line keeps it where it is: only a line that crosses the foot moves.
    [Fact]
    public async Task SingleLineContainerWithRoom_StaysOnItsPage()
    {
        var body = "<div style='height:150pt'></div><div style='columns:3;column-gap:8pt'><div>w9_71 w9_72</div></div>";

        var positions = await PaintedWords.PositionsAsync(Page(body));

        Assert.InRange(positions["w9_71"].Y, 170, 175);
    }}
