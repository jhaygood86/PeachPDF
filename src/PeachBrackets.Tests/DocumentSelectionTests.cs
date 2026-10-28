using PeachBrackets.Selection;
using PeachBrackets.Text;

namespace PeachBrackets.Tests;

public class DocumentSelectionTests
{
    private const string TwoParagraphs = "<p>Lunch at noon</p><p>second <strong>paragraph</strong> here</p>";

    private static async Task<HtmlDocument> LoadAsync(string body = TwoParagraphs, bool styling = false)
    {
        var options = new HtmlDocumentOptions { RecordTextRuns = true, EnableSelectionStyling = styling };
        return (await TestDocument.LoadAsync(TestDocument.Wrap(body, "<style>body { font-size: 20pt } p { margin: 4pt 0 } ::selection { background: rgb(255, 0, 0) }</style>"), options)).Document;
    }

    private static (double X, double Y) Centre(HtmlDocument document, int run, int from, int to)
    {
        var r = document.TextRuns[run];
        return ((r.CharacterOffsets[from] + r.CharacterOffsets[to]) / 2, r.Baseline - (r.Ascent / 2));
    }

    [Fact]
    public async Task AClickPutsTheCaret_AndAShiftClickExtendsFromIt()
    {
        var document = await LoadAsync();
        var selection = document.Selection;
        Assert.False(selection.HasSelection);
        Assert.Equal(0, selection.RangeCount);

        var (x, y) = Centre(document, 0, 6, 8);
        Assert.True(selection.SelectAt(0, x - 5, y));
        Assert.True(selection.IsCollapsed);
        Assert.Equal(1, selection.RangeCount);
        Assert.Equal(string.Empty, selection.GetText());

        var (x2, y2) = Centre(document, 0, 9, 13);
        Assert.True(selection.ExtendTo(0, x2 + 20, y2));
        Assert.False(selection.IsCollapsed);
        Assert.Equal("at noon", selection.GetText());
        Assert.Equal(selection.GetText(), selection.ToString());
        Assert.False(selection.IsBackward);

        // Dragging back past the anchor reverses the selection but keeps the same ordered range.
        selection.ExtendTo(0, 0, y);
        Assert.True(selection.IsBackward);
        Assert.Equal("Lunch ", selection.GetText());
        Assert.True(selection.GetRange()!.Value.Start.CompareTo(selection.GetRange()!.Value.End) < 0);
    }

    [Fact]
    public async Task DoubleAndTripleClick_SelectTheWordAndTheBlock()
    {
        var document = await LoadAsync();
        var selection = document.Selection;

        var (x, y) = Centre(document, 1, 0, 2);
        Assert.True(selection.SelectWordAt(0, x, y));
        Assert.Equal("second", selection.GetText());

        Assert.True(selection.SelectBlockAt(0, x, y));
        Assert.Equal("second paragraph here", selection.GetText());

        selection.SelectAll();
        Assert.Equal("Lunch at noon\nsecond paragraph here", selection.GetText());
    }

    [Fact]
    public async Task CharacterMoves_StepThroughTheText_AndStopAtTheEnds()
    {
        var document = await LoadAsync("<p>ab<strong>cd</strong></p><p>e\U0001F600f</p>");
        var selection = document.Selection;
        selection.Collapse(new TextPosition(0, 0));

        Assert.False(selection.Modify(extend: false, SelectionDirection.Backward, SelectionGranularity.Character));

        var visited = new List<string>();
        while (selection.Modify(extend: true, SelectionDirection.Forward, SelectionGranularity.Character))
            visited.Add(selection.GetText());

        // The emoji is one step, and the step between the paragraphs crosses the break.
        Assert.Equal("abcd\ne\U0001F600f", visited[^1]);
        Assert.Contains("abcd\ne\U0001F600", visited);
        Assert.DoesNotContain(visited, text => text.EndsWith('\uD83D'));
        // Four characters, the step across the break (which selects no text yet), then e, the emoji and f.
        Assert.Equal(8, visited.Count);
    }

    [Fact]
    public async Task AMoveThatDoesNotExtend_OverASelection_CollapsesToTheNearEdge()
    {
        var document = await LoadAsync();
        var selection = document.Selection;
        selection.SetBaseAndExtent(new TextPosition(0, 2), new TextPosition(0, 7));

        Assert.True(selection.Modify(extend: false, SelectionDirection.Forward, SelectionGranularity.Character));
        Assert.True(selection.IsCollapsed);
        Assert.Equal(new TextPosition(0, 7), selection.Focus);

        selection.SetBaseAndExtent(new TextPosition(0, 2), new TextPosition(0, 7));
        selection.Modify(extend: false, SelectionDirection.Backward, SelectionGranularity.Character);
        Assert.Equal(new TextPosition(0, 2), selection.Focus);

        selection.SetBaseAndExtent(new TextPosition(0, 2), new TextPosition(0, 7));
        selection.CollapseToEnd();
        Assert.Equal(new TextPosition(0, 7), selection.Anchor);
        selection.SetBaseAndExtent(new TextPosition(0, 7), new TextPosition(0, 2));
        selection.CollapseToStart();
        Assert.Equal(new TextPosition(0, 2), selection.Anchor);
    }

    [Fact]
    public async Task WordMoves_GoToWordEdges_SkippingSpaces_AndAcrossBlocks()
    {
        var document = await LoadAsync();
        var selection = document.Selection;
        selection.Collapse(new TextPosition(0, 0));

        selection.Modify(false, SelectionDirection.Forward, SelectionGranularity.Word);
        Assert.Equal(new TextPosition(0, 5), selection.Focus);
        selection.Modify(false, SelectionDirection.Forward, SelectionGranularity.Word);
        Assert.Equal(new TextPosition(0, 8), selection.Focus);
        selection.Modify(false, SelectionDirection.Forward, SelectionGranularity.Word);
        Assert.Equal(new TextPosition(0, 13), selection.Focus);
        selection.Modify(false, SelectionDirection.Forward, SelectionGranularity.Word);
        Assert.Equal(1, selection.Focus!.Value.RunIndex);

        selection.Collapse(new TextPosition(0, 13));
        selection.Modify(false, SelectionDirection.Backward, SelectionGranularity.Word);
        Assert.Equal(new TextPosition(0, 9), selection.Focus);
        selection.Modify(false, SelectionDirection.Backward, SelectionGranularity.Word);
        Assert.Equal(new TextPosition(0, 6), selection.Focus);

        // Shift+Ctrl+Right selects a word from the caret.
        selection.Collapse(new TextPosition(0, 6));
        selection.Modify(true, SelectionDirection.Forward, SelectionGranularity.Word);
        Assert.Equal("at", selection.GetText());
    }

    [Fact]
    public async Task LineMoves_KeepTheirHorizontalPosition_AndHomeEndGoToTheLineEdges()
    {
        var document = await LoadAsync();
        var selection = document.Selection;
        var first = document.TextRuns[0];
        var second = document.TextRuns[1];

        selection.Collapse(new TextPosition(0, 9));
        var x = first.CharacterOffsets[9];

        Assert.True(selection.Modify(false, SelectionDirection.Forward, SelectionGranularity.Line));
        Assert.Equal(1, selection.Focus!.Value.RunIndex);
        var landed = second.CharacterOffsets[selection.Focus.Value.CharIndex];
        Assert.InRange(landed, x - 12, x + 12);

        // Back up lands where it started, through the remembered position.
        Assert.True(selection.Modify(false, SelectionDirection.Backward, SelectionGranularity.Line));
        Assert.Equal(new TextPosition(0, 9), selection.Focus);

        // On the last line, down goes to the end of the line; on the first, up to its start.
        selection.Collapse(new TextPosition(1, 3));
        selection.Modify(false, SelectionDirection.Forward, SelectionGranularity.Line);
        Assert.Equal(new TextPosition(1, second.Text.Length), selection.Focus);
        selection.Collapse(new TextPosition(0, 5));
        selection.Modify(false, SelectionDirection.Backward, SelectionGranularity.Line);
        Assert.Equal(new TextPosition(0, 0), selection.Focus);

        selection.Collapse(new TextPosition(1, 3));
        selection.Modify(false, SelectionDirection.Backward, SelectionGranularity.LineBoundary);
        Assert.Equal(new TextPosition(1, 0), selection.Focus);
        selection.Modify(false, SelectionDirection.Forward, SelectionGranularity.LineBoundary);
        Assert.Equal(new TextPosition(1, second.Text.Length), selection.Focus);
    }

    [Fact]
    public async Task BlockAndDocumentMoves_GoToTheEdges()
    {
        var document = await LoadAsync();
        var selection = document.Selection;

        selection.Collapse(new TextPosition(0, 3));
        selection.Modify(false, SelectionDirection.Forward, SelectionGranularity.Block);
        Assert.Equal(new TextPosition(0, 13), selection.Focus);
        selection.Modify(false, SelectionDirection.Forward, SelectionGranularity.Block);
        Assert.Equal(1, selection.Focus!.Value.RunIndex);
        Assert.Equal(document.TextRuns[1].Text.Length, selection.Focus.Value.CharIndex);

        selection.Modify(true, SelectionDirection.Backward, SelectionGranularity.Document);
        Assert.Equal("Lunch at noon\nsecond paragraph here", selection.GetText());
        Assert.False(selection.Modify(true, SelectionDirection.Backward, SelectionGranularity.Document));
    }

    [Fact]
    public async Task AChange_RaisesOnceWithTheRegionsToRepaint_AndNoChangeRaisesNothing()
    {
        var document = await LoadAsync();
        var selection = document.Selection;
        var events = new List<SelectionChangedEventArgs>();
        selection.SelectionChanged += (_, e) => events.Add(e);

        selection.SetBaseAndExtent(new TextPosition(0, 0), new TextPosition(0, 5));
        var first = Assert.Single(events);
        var run = document.TextRuns[0];
        Assert.All(first.DirtyRegions, region => Assert.Equal(0, region.PageIndex));
        Assert.Contains(first.DirtyRegions, region => region.Bounds.Left <= run.CharacterOffsets[0] && region.Bounds.Right >= run.CharacterOffsets[5]);

        selection.SetBaseAndExtent(new TextPosition(0, 0), new TextPosition(0, 5));
        Assert.Single(events);

        // Moving it reports where it was as well as where it is.
        selection.SetBaseAndExtent(new TextPosition(1, 0), new TextPosition(1, 3));
        var moved = events[^1];
        Assert.Contains(moved.DirtyRegions, region => region.Bounds.Top <= run.Baseline && region.Bounds.Bottom >= run.Baseline);
        Assert.Contains(moved.DirtyRegions, region => region.Bounds.Top <= document.TextRuns[1].Baseline && region.Bounds.Bottom >= document.TextRuns[1].Baseline);

        selection.RemoveAllRanges();
        Assert.False(selection.HasSelection);
        Assert.Equal(3, events.Count);
        Assert.Null(events[^1].Focus);
        Assert.False(selection.Modify(true, SelectionDirection.Forward, SelectionGranularity.Character));
    }

    [Fact]
    public async Task ACaret_HasBounds_AndItsOwnRepaintRegion()
    {
        var document = await LoadAsync();
        var selection = document.Selection;
        Assert.Null(selection.GetCaretBounds());

        var seen = new List<SelectionChangedEventArgs>();
        selection.SelectionChanged += (_, e) => seen.Add(e);
        selection.Collapse(new TextPosition(0, 4));

        var caret = selection.GetCaretBounds(2)!.Value;
        var run = document.TextRuns[0];
        Assert.Equal(0, caret.PageIndex);
        Assert.Equal(run.CharacterOffsets[4], caret.Bounds.X + 1, 3);
        Assert.Equal(run.Ascent + run.Descent, caret.Bounds.Height, 3);
        Assert.NotEmpty(Assert.Single(seen).DirtyRegions);
    }

    [Fact]
    public async Task WithSelectionStyling_TheSelectionIsPaintedAndClearedAsItChanges()
    {
        var options = new HtmlDocumentOptions { RecordTextRuns = true, EnableSelectionStyling = true };
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<p>Hello world</p>", "<style>body { font-size: 40pt } ::selection { background: rgb(255, 0, 0) }</style>"), options);
        var run = document.TextRuns[0];
        var row = (int)(run.Baseline + (run.Descent * 0.5));
        var x = (int)run.CharacterOffsets[2];

        document.Selection.SetBaseAndExtent(new TextPosition(0, 0), new TextPosition(0, 5));
        Assert.Single(document.SelectedRanges);
        using (var painted = await TestDocument.PaintAsync(context, document))
            Assert.Equal((255, 0, 0, 255), TestDocument.Pixel(painted, x, row));

        // A caret selects nothing, so nothing is painted.
        document.Selection.Collapse(new TextPosition(0, 3));
        Assert.Empty(document.SelectedRanges);
        using (var cleared = await TestDocument.PaintAsync(context, document))
            Assert.Equal((255, 255, 255, 255), TestDocument.Pixel(cleared, x, row));
    }

    [Fact]
    public async Task WithoutSelectionStyling_TheModelStillWorks_AndPaintsNothing()
    {
        var document = await LoadAsync(styling: false);
        document.Selection.SelectAll();

        Assert.Contains("Lunch", document.Selection.GetText());
        Assert.Empty(document.SelectedRanges);
    }

    [Fact]
    public async Task BadPositions_AreRejected_AndTheElementOfAPositionIsFound()
    {
        var document = await LoadAsync();
        var selection = document.Selection;

        Assert.Throws<ArgumentOutOfRangeException>(() => selection.Collapse(new TextPosition(9, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.Collapse(new TextPosition(0, 99)));
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.Extend(new TextPosition(-1, 0)));

        var run = document.TextRuns[1];
        var inStrong = run.Text.IndexOf("paragraph", StringComparison.Ordinal) + 2;
        Assert.Equal("strong", document.GetElementAt(new TextPosition(1, inStrong))?.TagName);
        Assert.Equal("p", document.GetElementAt(new TextPosition(1, 1))?.TagName);
        Assert.Null(document.GetElementAt(new TextPosition(9, 0)));
    }

    [Fact]
    public async Task SelectionNeedsTheTextRuns()
    {
        var options = new HtmlDocumentOptions { RecordTextRuns = false };
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<p>x</p>"), options);

        Assert.Throws<InvalidOperationException>(() => document.Selection);
    }
}
