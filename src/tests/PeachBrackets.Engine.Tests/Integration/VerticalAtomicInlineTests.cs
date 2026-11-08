using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachPDF.Rendering;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// An atomic inline-level box (<c>inline-block</c>, <c>inline-table</c>, <c>inline-flex</c>) inside vertical line flow is one unbreakable item
/// of its column: laid out on its own, wrapped to the next column as a whole, positioned by the flow with its margin box, and painted at that
/// position (css-display-3 section 2.3, CSS 2.1 section 9.4.2, css-writing-modes-4 section 7.3).
/// </summary>
public class VerticalAtomicInlineTests
{
    private const double PointsPerPx = 0.75;

    private static async Task<(CssBox Block, CssBox Atomic, HtmlContainerInt Container)> LayoutAsync(
        string blockStyle, string atomicStyle, string atomicContent = "nested block", string tag = "span")
    {
        var html = LayoutHarness.Wrap(
            $"<div id='el' style='font-size:16px;{blockStyle}'>before <{tag} id='ib' style='{atomicStyle}'>{atomicContent}</{tag}> after</div>");
        var (root, container) = await LayoutHarness.LayoutAsync(html, margin: 0);

        return (LayoutHarness.FindById(root, "el")!, LayoutHarness.FindById(root, "ib")!, container);
    }

    private static CssRect Word(CssBox block, string text) =>
        block.LineBoxes.SelectMany(l => l.Words).First(w => w.Text == text);

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public async Task TheBoxIsNotFlattenedIntoTheColumnsWords(string writingMode)
    {
        var (block, atomic, _) = await LayoutAsync($"writing-mode:{writingMode};width:300px;height:400px",
            "display:inline-block;border:1px solid red;padding:4px");

        var texts = block.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak && w.Text is not null).Select(w => w.Text).ToList();
        Assert.Equal(["before", "after"], texts);

        // Its own words are in its own lines.
        var own = atomic.LineBoxes.SelectMany(l => l.Words).Select(w => w.Text).ToList();
        Assert.Equal(["nested", "block"], own);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public async Task TheBoxSitsBetweenItsNeighboursAlongTheColumn_WithItsMarginBox(string writingMode)
    {
        var (block, atomic, _) = await LayoutAsync($"writing-mode:{writingMode};width:300px;height:400px",
            "display:inline-block;border:1px solid red;padding:4px;margin:6px 0");

        var before = Word(block, "before");
        var after = Word(block, "after");

        Assert.True(atomic.Location.Y >= before.Bottom + 6 * PointsPerPx - 0.01, "the top margin lies between the box and the word before it");
        Assert.True(after.Top >= atomic.ActualBottom + 6 * PointsPerPx - 0.01, "the bottom margin lies between the box and the word after it");
        Assert.Equal(atomic.ActualHeight, atomic.ActualBottom - atomic.Location.Y, 2);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public async Task TheBoxIsInsideTheColumnsBlockExtent_AndRegisteredOnTheColumn(string writingMode)
    {
        var (block, atomic, _) = await LayoutAsync($"writing-mode:{writingMode};width:300px;height:400px",
            "display:inline-block;border:1px solid red;padding:4px");

        var column = block.LineBoxes.Single(l => l.Words.Any(w => w.Text == "before"));
        var rect = Assert.Single(atomic.Rectangles).Value;

        Assert.Same(column, Assert.Single(atomic.Rectangles).Key);
        Assert.Equal(atomic.Location.X, rect.X, 3);
        Assert.Equal(atomic.Location.Y, rect.Y, 3);
        Assert.Equal(atomic.ActualBoxSizingWidth, rect.Width, 3);
        Assert.Equal(atomic.ActualBoxSizingHeight, rect.Height, 3);

        // The text inside it moved with it.
        foreach (var word in atomic.LineBoxes.SelectMany(l => l.Words))
        {
            Assert.InRange(word.Left, atomic.Location.X - 0.01, atomic.ActualRight + 0.01);
            Assert.InRange(word.Top, atomic.Location.Y - 0.01, atomic.ActualBottom + 0.01);
        }
    }

    [Fact]
    public async Task ABoxTooTallForWhatIsLeftOfTheColumn_MovesToTheNextColumnWhole()
    {
        var (block, atomic, _) = await LayoutAsync("writing-mode:vertical-rl;width:400px;height:150px",
            "display:inline-block;height:120px;width:30px;background:red", atomicContent: "x");

        Assert.True(block.LineBoxes.Count >= 2);
        var rect = Assert.Single(atomic.Rectangles);
        Assert.NotSame(block.LineBoxes[0], rect.Key);
        // vertical-rl: later columns are to the left.
        Assert.True(atomic.Location.X < Word(block, "before").Left);
        Assert.Equal(120 * PointsPerPx, atomic.ActualBottom - atomic.Location.Y, 2);
    }

    [Fact]
    public async Task AnOrthogonalBox_KeepsItsOwnWritingModesLines()
    {
        var (_, atomic, _) = await LayoutAsync("writing-mode:vertical-rl;width:300px;height:400px",
            "display:inline-block;writing-mode:horizontal-tb;width:120px", "nested block");

        var words = atomic.LineBoxes.SelectMany(l => l.Words).Where(w => w.Text is not null).ToList();
        Assert.Equal(2, words.Count);
        Assert.Equal(words[0].Top, words[1].Top, 2);
        Assert.True(words[1].Left > words[0].Left, "a horizontal box's words run left to right");
        Assert.InRange(words[0].Left, atomic.Location.X, atomic.ActualRight);
    }

    [Fact]
    public async Task TheColumnIsAsThickAsTheBoxesMarginBox()
    {
        var (plain, _, _) = await LayoutAsync("writing-mode:vertical-rl;width:300px;height:400px", "display:inline-block;width:20px;height:40px", "");
        var (padded, atomic, _) = await LayoutAsync("writing-mode:vertical-rl;width:300px;height:400px",
            "display:inline-block;width:20px;height:40px;margin:0 30px", "");

        var gap = Word(padded, "before").Left - Word(plain, "before").Left;
        Assert.True(atomic.ActualMarginLeft + atomic.ActualMarginRight > 0);
        Assert.Equal(0, gap, 2); // the column holding it is still anchored to the box's right edge
        var thickness = padded.LineBoxes[0].Words.Max(w => w.Right) - padded.LineBoxes[0].Words.Min(w => w.Left);
        Assert.True(thickness >= atomic.ActualBoxSizingWidth + 60 * PointsPerPx - 0.01,
            $"the column is {thickness} thick but its margin box needs {atomic.ActualBoxSizingWidth + 60 * PointsPerPx}");
    }

    [Fact]
    public async Task AnInlineFlexBox_IsLaidOutAsAnAtomicUnitToo()
    {
        var (block, atomic, _) = await LayoutAsync("writing-mode:vertical-rl;width:300px;height:400px",
            "display:inline-flex;width:60px;height:40px;background:red", "<i id='fi'>x</i>");

        Assert.Equal(["before", "after"], block.LineBoxes.SelectMany(l => l.Words).Where(w => w.Text is not null).Select(w => w.Text).ToList());
        Assert.Equal(40 * PointsPerPx, atomic.ActualBottom - atomic.Location.Y, 2);
        Assert.Equal(60 * PointsPerPx, atomic.ActualRight - atomic.Location.X, 2);
        Assert.Single(atomic.Rectangles);
    }

    [Fact]
    public async Task ItsBackgroundPaintsAtTheBoxesPlaceInTheColumn()
    {
        var (block, atomic, container) = await LayoutAsync("writing-mode:vertical-rl;width:300px;height:400px",
            "display:inline-block;width:40px;height:60px;background:rgb(255,0,0)", "");

        var recording = new RecordingGraphics(new HtmlRenderContext());
        FragmentPaintHarness.PaintBox(container, atomic, recording);

        var fill = Assert.Single(recording.Log, op => op.Kind == PaintOpKind.FillRect);
        Assert.Equal(atomic.Location.X, fill.Bounds.X, 2);
        Assert.Equal(atomic.Location.Y, fill.Bounds.Y, 2);
        Assert.Equal(40 * PointsPerPx, fill.Bounds.Width, 2);
        Assert.Equal(60 * PointsPerPx, fill.Bounds.Height, 2);
        Assert.True(atomic.Location.Y > Word(block, "before").Top, "it is after the word before it down the column");
    }

    [Fact]
    public async Task ASpanAroundTheBox_CoversItsRectangle()
    {
        var html = LayoutHarness.Wrap(
            "<div id='el' style='font-size:16px;writing-mode:vertical-lr;width:300px;height:400px'>"
            + "<span id='sp' style='background:yellow'><span id='ib' style='display:inline-block;width:20px;height:50px'></span></span></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, margin: 0);
        var span = LayoutHarness.FindById(root, "sp")!;
        var atomic = LayoutHarness.FindById(root, "ib")!;

        var spanRect = Assert.Single(span.Rectangles).Value;
        Assert.True(spanRect.Top <= atomic.Location.Y + 0.01 && spanRect.Bottom >= atomic.ActualBottom - 0.01);
        Assert.True(spanRect.Left <= atomic.Location.X + 0.01 && spanRect.Right >= atomic.ActualRight - 0.01);
    }
}
