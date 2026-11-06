using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A table cell aligns its content in the room the content leaves, so what the content reaches down to decides where it goes. The words'
/// own rectangles stop short of a line's leading, of a line that holds only a <c>&lt;br&gt;</c> and of a bottom margin, so measured from them
/// a cell with no room to spare was thought to have some and its content was moved down by half of it: HTML e-mail that ends a cell's text
/// with <c>&lt;br/&gt;</c> had its text lower than in a browser.
/// </summary>
public class CellVerticalAlignContentExtentTests
{
    private const string Paragraph = "<p id='p' style='margin:10pt 0;font-size:10pt;line-height:15pt'>Hello</p>";

    // No margin below: one that is followed by an empty block with a margin of its own is a margin-collapsing case this test is not about.
    private const string ParagraphWithoutBottomMargin = "<p id='p' style='margin:10pt 0 0;font-size:10pt;line-height:15pt'>Hello</p>";

    private static string ParagraphBefore(string trailing) => trailing.Contains("margin-bottom") ? ParagraphWithoutBottomMargin : Paragraph;

    private static async Task<(CssBox Cell, CssBox P)> LayoutAsync(string content, string cellStyle, string otherCell = "")
    {
        var html = LayoutHarness.Wrap(
            $"<table cellpadding='0' cellspacing='0'><tr><td id='c' style='{cellStyle};width:150pt'>{content}</td>{otherCell}</tr></table>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        return (LayoutHarness.FindById(root, "c")!, LayoutHarness.FindById(root, "p")!);
    }

    // The cell is exactly as tall as its content, so there is nothing to align: the paragraph is where its margin puts it.
    [Theory]
    [InlineData("vertical-align:middle", "")]
    [InlineData("vertical-align:middle", "<br/>")]
    [InlineData("vertical-align:middle", "<br/><br/>")]
    [InlineData("vertical-align:middle", "<div style='height:0;margin-bottom:12pt'></div>")]
    [InlineData("vertical-align:bottom", "")]
    [InlineData("vertical-align:bottom", "<br/>")]
    [InlineData("vertical-align:bottom", "<div style='height:0;margin-bottom:12pt'></div>")]
    [InlineData("vertical-align:middle;padding:6pt 4pt", "<br/>")]
    public async Task CellWithNoRoomToSpare_LeavesTheContentWhereItsMarginPutsIt(string cellStyle, string trailing)
    {
        var (cell, p) = await LayoutAsync(ParagraphBefore(trailing) + trailing, cellStyle);

        Assert.Equal(cell.ClientTop + 10, p.Location.Y, 1);
    }

    // A row made taller by another cell does leave room: the content is centred in it with its trailing line and margin counted as content.
    [Theory]
    [InlineData("", 100 - 10 - 15 - 10)]
    [InlineData("<br/>", 100 - 10 - 15 - 10 - 15)]
    [InlineData("<div style='height:0;margin-bottom:12pt'></div>", 100 - 10 - 15 - 12)]
    public async Task CellInATallerRow_CentresTheContentIncludingItsTrailingSpace(string trailing, double room)
    {
        var (cell, p) = await LayoutAsync(ParagraphBefore(trailing) + trailing, "vertical-align:middle",
            "<td style='width:20pt;height:100pt;vertical-align:top'></td>");

        Assert.Equal(cell.ClientTop + room / 2 + 10, p.Location.Y, 1);
    }

    [Fact]
    public async Task BottomAlignedCellInATallerRow_PutsTheTrailingMarginAtTheBottom()
    {
        var (cell, p) = await LayoutAsync(ParagraphWithoutBottomMargin + "<div style='height:0;margin-bottom:12pt'></div>", "vertical-align:bottom",
            "<td style='width:20pt;height:100pt;vertical-align:top'></td>");

        // The 12pt margin ends at the cell's bottom edge.
        Assert.Equal(cell.ClientBottom - 12 - 15, p.Location.Y, 1);
    }

    // Text the cell holds itself: a line is as tall as its line-height, not as its glyphs, and that is what is centred. The glyphs of a line sit
    // in the middle of it, so the word is in the middle of the 100pt row whichever way the line is measured, once the leading under it counts.
    [Fact]
    public async Task TextInACell_IsCentredByItsLineBox()
    {
        var html = LayoutHarness.Wrap(
            "<table cellpadding='0' cellspacing='0'><tr><td id='c' style='vertical-align:middle;width:150pt;font-size:10pt;line-height:30pt'>Hello</td>" +
            "<td style='width:20pt;height:100pt;vertical-align:top'></td></tr></table>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);
        var cell = LayoutHarness.FindById(root, "c")!;

        static IEnumerable<CssRect> WordsOf(CssBox box) => box.Words.Concat(box.Boxes.SelectMany(WordsOf));

        var word = WordsOf(cell).Single();

        Assert.Equal(cell.ClientTop + 50, word.Top + word.Height / 2, 1);
    }

    // A box that is not content of the cell does not count: an absolutely positioned one hangs off it (CSS 2.1 section 10.6.3).
    [Fact]
    public async Task AbsolutelyPositionedBox_IsNotContentOfTheCell()
    {
        var (cell, p) = await LayoutAsync(Paragraph + "<div style='position:absolute;top:0;left:0;margin-bottom:200pt;height:5pt'></div>",
            "vertical-align:middle;position:relative");

        Assert.Equal(cell.ClientTop + 10, p.Location.Y, 1);
    }
}
