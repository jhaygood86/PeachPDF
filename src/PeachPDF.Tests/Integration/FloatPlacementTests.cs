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

    // #1531: text below two floats that leave no room (or none at all) between them keeps every word. CSS 2.1 §9.5:
    // a line box shortened to nothing is shifted down until content fits or no float remains.
    [Theory]
    [InlineData(120, 38)]
    [InlineData(129, 38)]
    [InlineData(139, 20)]
    public async Task TextBelowTwoFloatsWithNoRoomBetweenThem_KeepsEveryWord(int rightFloatWidth, double firstLineTop)
    {
        static string Words(int from, int count) => string.Join(' ', Enumerable.Range(from, count).Select(i => $"w299_{i}"));

        var body = $"<div style='float:left;width:131pt'><p>{Words(238, 14)}</p></div>" +
                   $"<div style='float:right;width:{rightFloatWidth}pt'><div style='height:6pt'></div><div>w299_7</div></div>" +
                   $"<p style='margin:0 0 4pt'>{Words(469, 40)}</p>";
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 160pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt Arial } p { margin:0 0 4pt }</style></head><body>" + body + "</body></html>";

        var (painted, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
        var (lost, doubled) = PaintedWords.Diff(body, painted);

        Assert.Empty(lost);
        Assert.Empty(doubled);

        // With 120 or 129pt for the right float the first line has no room between the floats, so it is shifted below
        // the right float (20pt margin + 6pt spacer + a 12pt line = 38), where only the left float remains. At 139pt
        // the two floats do not fit side by side (CSS 2.1 §9.5.1 rule 3), the right one drops below the left, and the
        // first line sits beside the left float at the top. Either way it starts clear of the left float.
        var positions = await PaintedWords.PositionsAsync(html);
        Assert.True(positions["w299_469"].Y >= firstLineTop - 0.5, $"first word at y={positions["w299_469"].Y}, above {firstLineTop}");
        Assert.True(positions["w299_469"].Y < firstLineTop + 1, $"first word at y={positions["w299_469"].Y}, below {firstLineTop}");
        Assert.True(positions["w299_469"].X >= 20 + 131 - 0.5, $"first word at x={positions["w299_469"].X}, inside the left float");
    }

    // A line with no room beside floats that are taller than the page is shifted below them (CSS 2.1 §9.5), which is in a later
    // fragmentainer: the floats were placed before the line and every page leaves less of them to clear.
    [Fact]
    public async Task CrowdedLine_BelowFloatsTallerThanThePage_GoesBelowThem()
    {
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 160pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt Arial }</style></head><body>" +
                   "<div style='float:left;width:131pt;height:300pt'>w1_1</div>" +
                   "<div style='float:right;width:129pt;height:300pt'>w1_2</div>" +
                   "<p style='margin:0'>w2_1 w2_2 w2_3</p></body></html>";

        var positions = await PaintedWords.PositionsAsync(html);

        // The floats end at y=320 of the flow, and the pages hold 120pt of it each: the line is in the band that point falls in, 60pt below its top.
        Assert.True(positions["w2_1"].Page > 0, $"the first word is still on the page where the floats start, at y={positions["w2_1"].Y}");
        Assert.InRange(positions["w2_1"].Y, 78, 82);
    }

    // A tall right float inside a column of a container that starts partway down the page narrows the lines of the
    // columns after it too. Shifting each column's first line below it never settled and layout spun forever
    // (found by a generated corpus: a 3-column container, a 75pt x 129pt right float, 150pt down the page).
    [Fact]
    public async Task TallFloatInAColumn_DoesNotSendLayoutIntoALoop()
    {
        var html = Page("<div style='height:150pt'></div><div style='columns:3;column-gap:8pt'>" +
                        "<p>w1_56 w1_57 w1_58 w1_59 w1_60 w1_61 w1_62 w1_63 w1_64 w1_65</p>" +
                        "<div style='float:right;width:75pt;height:129pt'>w1_66</div>" +
                        "w1_67 w1_68 w1_69 w1_70 w1_71 w1_72</div>");

        var layout = Task.Run(() => PaintedWords.LayOutAndCollectVisibleAsync(html));
        var finished = await Task.WhenAny(layout, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)) == layout;

        Assert.True(finished, "layout did not finish within 30 seconds");
        Assert.True((await layout).Pages >= 1);
    }

    // #1535: an empty block with `clear` (the clearfix div) is given clearance (CSS 2.1 §9.5.2), so what follows it is
    // below the float, not beside it. With text, a height or overflow: hidden it already was.
    [Theory]
    [InlineData("<div style='clear: both'></div>", 132)]
    [InlineData("<div style='clear: both'>clr</div>", 144)]
    [InlineData("<div style='clear: both; height: 1pt'></div>", 133)]
    [InlineData("<div style='clear: both; overflow: hidden'></div>", 132)]
    public async Task EmptyBlockWithClear_ClearsTheFloatAboveIt(string clearing, double afterY)
    {
        var html = Page("<div>top</div><div style='float: right; width: 50pt; height: 100pt'>f</div>" +
                        clearing + "<p style='margin: 0'>after</p>");
        var positions = await PaintedWords.PositionsAsync(html);

        // The word's top is its line's top plus half-leading, so compare against the line box rather than exactly.
        Assert.True(Math.Abs(positions["after"].Y - afterY) < 1.5,
            $"'after' at y={positions["after"].Y}, expected about {afterY}");
    }

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
