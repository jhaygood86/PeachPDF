using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// CSS 2.1 §9.5.1: a float that does not fit beside an earlier one drops below it, as high as possible (rule 8), for
/// a right float as for a left one; and §9.5: a line box shortened to nothing is shifted down until content fits.
/// </summary>
public class FloatPlacementTests
{
    private static string Page(string body) =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 400pt; margin: 20pt } " +
        "body { margin:0; font: 10pt/12pt Arial } p { margin:0 }</style></head><body>" + body + "</body></html>";

    private static string TwoFloats(string side, string firstExtra, string secondExtra) =>
        $"<div style='float:{side}; width:200pt; height:30pt;{firstExtra}'>fa</div>" +
        $"<div style='float:{side}; width:200pt; height:30pt;{secondExtra}'>fb</div>";

    // The case that fits: the second right float sits beside the first, to its left, at the same height.
    [Fact]
    public async Task SecondRightFloatThatFitsBesideTheFirst_SitsToItsLeft()
    {
        var html = Page("<div style='float:right; width:100pt; height:30pt'>fa</div>" +
                        "<div style='float:right; width:100pt; height:30pt'>fb</div>");
        var positions = await PaintedWords.PositionsAsync(html);

        Assert.Equal(positions["fa"].Y, positions["fb"].Y, 1);
        Assert.Equal(positions["fa"].X - 100, positions["fb"].X, 1);
    }

    // #1522: two 200pt floats do not fit in 260pt, so the second drops by the first's height (plus margins).
    [Theory]
    [InlineData("left", "", "", 30)]
    [InlineData("right", "", "", 30)]
    [InlineData("right", "margin-bottom: 10pt", "", 40)]
    [InlineData("right", "", "margin-top: 10pt", 40)]
    public async Task SecondFloatThatDoesNotFitBesideTheFirst_DropsBelowIt(
        string side, string firstExtra, string secondExtra, double expectedDrop)
    {
        var positions = await PaintedWords.PositionsAsync(Page(TwoFloats(side, firstExtra, secondExtra)));

        Assert.Equal(expectedDrop, positions["fb"].Y - positions["fa"].Y, 1);
    }
}
