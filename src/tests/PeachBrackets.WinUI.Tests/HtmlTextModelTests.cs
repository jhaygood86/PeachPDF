using PeachBrackets.WinUI;

namespace PeachBrackets.WinUI.Tests;

public class HtmlTextModelTests
{
    private static HtmlTextModel Model(params string[] blocks) => HtmlTextModel.FromBlocks(blocks.Select(b => (b, (PeachBrackets.Accessibility.AccessibilityNode?)null)));

    [Fact]
    public void BlocksAreSeparatedByLineBreaks()
    {
        var model = Model("Hello world", "Second");

        Assert.Equal("Hello world\nSecond\n", model.Text);
        Assert.Equal(new[] { (0, 11), (12, 6) }, model.Segments.Select(s => (s.Start, s.Length)).ToArray());
    }

    [Fact]
    public void ARangeWidensToTheWordItIsIn()
    {
        var model = Model("Hello world");

        Assert.Equal((0, 6), model.Expand(2, 2, TextUnitKind.Word));
        Assert.Equal((6, 12), model.Expand(8, 8, TextUnitKind.Word));
    }

    [Fact]
    public void ARangeWidensToTheLineAndParagraphItIsIn()
    {
        var model = Model("One", "Two");

        Assert.Equal((4, 8), model.Expand(5, 5, TextUnitKind.Line));
        Assert.Equal((0, 4), model.Expand(0, 2, TextUnitKind.Paragraph));
        Assert.Equal((0, 8), model.Expand(3, 3, TextUnitKind.Document));
    }

    [Fact]
    public void ACharacterIsAWholeGraphemeCluster()
    {
        var model = Model("é😀z");

        Assert.Equal((1, 3), model.Expand(1, 1, TextUnitKind.Character));
        Assert.Equal((0, 1), model.Expand(0, 0, TextUnitKind.Character));
        Assert.Equal((1, 3), model.Expand(2, 2, TextUnitKind.Character));
    }

    [Fact]
    public void MovingGoesByWholeUnitsAndSaysHowFarItWent()
    {
        var model = Model("one two three");

        Assert.Equal((4, 8, 1), model.Move(0, 0, TextUnitKind.Word, 1));
        Assert.Equal((8, 14, 2), model.Move(0, 0, TextUnitKind.Word, 2));
        var last = model.Move(8, 14, TextUnitKind.Word, 5);
        Assert.Equal((8, 14, 0), last);
        Assert.Equal((0, 4, -2), model.Move(8, 14, TextUnitKind.Word, -5));
    }

    [Fact]
    public void MovingByZeroOnlyWidens()
    {
        var model = Model("one two");

        Assert.Equal((0, 4, 0), model.Move(1, 1, TextUnitKind.Word, 0));
    }

    [Fact]
    public void AnEndpointMovesAloneAndDragsTheOtherWhenItCrosses()
    {
        var model = Model("one two three");

        var grown = model.MoveEndpoint(0, 4, moveStart: false, TextUnitKind.Word, 1);
        Assert.Equal((0, 8, 1), (grown.Start, grown.End, grown.Moved));
        var crossed = model.MoveEndpoint(0, 4, moveStart: true, TextUnitKind.Word, 2);
        Assert.Equal((8, 8), (crossed.Start, crossed.End));
        var backwards = model.MoveEndpoint(8, 14, moveStart: false, TextUnitKind.Word, -2);
        Assert.Equal((4, 4), (backwards.Start, backwards.End));
    }

    [Fact]
    public void FindingSearchesOnlyTheRange()
    {
        var model = Model("Hello hello world");

        Assert.Equal(6, model.Find("hello", 0, 18, backward: false, ignoreCase: false));
        Assert.Equal(0, model.Find("hello", 0, 18, backward: false, ignoreCase: true));
        Assert.Equal(6, model.Find("hello", 0, 18, backward: true, ignoreCase: true));
        Assert.Equal(-1, model.Find("world", 0, 6, backward: false, ignoreCase: false));
        Assert.Equal(-1, model.Find("", 0, 6, backward: false, ignoreCase: false));
    }

    [Fact]
    public void SlicingIsClippedToTheText()
    {
        var model = Model("Hello");

        Assert.Equal("ell", model.Slice(1, 4));
        Assert.Equal("He", model.Slice(0, 99, 2));
        Assert.Equal("Hello\n", model.Slice(-3, 99));
    }

    [Fact]
    public void ABlockIsFoundByPositionAndByRange()
    {
        var model = Model("One", "Two", "Three");

        Assert.Equal(4, model.SegmentAt(5)!.Value.Start);
        Assert.Equal(2, model.SegmentsIn(2, 6).Count());
        Assert.Single(model.SegmentsIn(4, 4));
    }

    [Fact]
    public void AnEmptyDocumentHasNothingToMoveTo()
    {
        var model = Model();

        Assert.Equal(string.Empty, model.Text);
        Assert.Null(model.SegmentAt(0));
        Assert.Equal((0, 0, 0), model.Move(0, 0, TextUnitKind.Word, 3));
    }

    [Theory]
    [InlineData(Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, "Character")]
    [InlineData(Microsoft.UI.Xaml.Automation.Text.TextUnit.Word, "Word")]
    [InlineData(Microsoft.UI.Xaml.Automation.Text.TextUnit.Line, "Line")]
    [InlineData(Microsoft.UI.Xaml.Automation.Text.TextUnit.Paragraph, "Paragraph")]
    [InlineData(Microsoft.UI.Xaml.Automation.Text.TextUnit.Format, "Paragraph")]
    [InlineData(Microsoft.UI.Xaml.Automation.Text.TextUnit.Page, "Document")]
    [InlineData(Microsoft.UI.Xaml.Automation.Text.TextUnit.Document, "Document")]
    public void AutomationUnitsMapOntoTheModelsUnits(Microsoft.UI.Xaml.Automation.Text.TextUnit unit, string expected) =>
        Assert.Equal(expected, HtmlViewLogic.ToUnitKind(unit).ToString());

    [Fact]
    public void ARangeAtTheEndOfTheTextStaysThereWhenWidenedAndMovesBackToTheLastUnit()
    {
        var model = Model("one two");

        Assert.Equal((8, 8), model.Expand(8, 8, TextUnitKind.Word));
        Assert.Equal((4, 8, -1), model.Move(8, 8, TextUnitKind.Word, -1));
        Assert.Equal((8, 8, 0), model.Move(8, 8, TextUnitKind.Word, 1));
    }

    [Fact]
    public void AnEndpointInTheMiddleOfAUnitMovesToTheNextOrPreviousBoundary()
    {
        var model = Model("one two three");

        var forward = model.MoveEndpoint(0, 2, moveStart: false, TextUnitKind.Word, 1);
        Assert.Equal((0, 4, 1), (forward.Start, forward.End, forward.Moved));

        var backward = model.MoveEndpoint(0, 6, moveStart: false, TextUnitKind.Word, -1);
        Assert.Equal((0, 4, -1), (backward.Start, backward.End, backward.Moved));
    }

    [Fact]
    public void AnOccurrenceIsCountedAndFoundTheWayTheDocumentSearchCountsThem()
    {
        var model = Model("dup one dup", "dup");

        Assert.Equal(0, model.OccurrenceAt("dup", 0));
        Assert.Equal(1, model.OccurrenceAt("dup", 8));
        Assert.Equal(2, model.OccurrenceAt("dup", 12));
        Assert.Equal(-1, model.OccurrenceAt("dup", 4));
        Assert.Equal(-1, model.OccurrenceAt("dup", 99));
        Assert.Equal(-1, model.OccurrenceAt(string.Empty, 0));
        Assert.Equal(8, model.OffsetOfOccurrence("dup", 1));
        Assert.Equal(12, model.OffsetOfOccurrence("dup", 2));
        Assert.Equal(-1, model.OffsetOfOccurrence("dup", 3));
        Assert.Equal(-1, model.OffsetOfOccurrence("dup", -1));
        Assert.Equal(-1, model.OffsetOfOccurrence(string.Empty, 0));
        // Occurrences do not overlap, as in the document's own search.
        Assert.Equal(-1, HtmlTextModel.FromBlocks(new (string, PeachBrackets.Accessibility.AccessibilityNode?)[] { ("aaa", null) }).OccurrenceAt("aa", 1));
    }
}
