using PeachDrawing.PDF;

namespace PeachReader.Core.Tests;

public class SelectionAndSearchTests
{
    private static Task<SearchResults> Run(ReaderDocument document, string query, PdfTextSearchOptions options = PdfTextSearchOptions.None,
        IProgress<SearchProgress>? progress = null) =>
        DocumentSearch.RunAsync(document, query, options, progress, TestContext.Current.CancellationToken);

    private static ReaderDocument Document() => ReaderDocument.Open(TestDocument.Build(
    [
        new TestPage(300, 200, TestDocument.Show("The quick brown fox", 30) + " " + TestDocument.Show("jumps over the lazy dog", 50)),
        new TestPage(300, 200, TestDocument.Show("A second page about the fox", 30)),
        new TestPage(300, 200, "0 0 10 10 re f"),                                   // no text at all
        new TestPage(300, 200, TestDocument.Show("The end", 30)),
    ]));

    [Fact]
    public void Drag_SelectsBetweenTheAnchorAndTheFocusInEitherDirection()
    {
        var doc = Document();
        var selection = new TextSelectionModel(doc.GetText, doc.PageCount);
        int changes = 0;
        selection.Changed += (_, _) => changes++;
        Assert.False(selection.HasSelection);

        selection.Begin(0, 4);
        Assert.False(selection.HasSelection);
        selection.Extend(0, 9);
        Assert.True(selection.HasSelection);
        Assert.Equal("quick", selection.GetSelectedText());

        selection.Extend(0, 0);                                             // dragged back past the anchor
        Assert.Equal("The ", selection.GetSelectedText());
        Assert.Equal(new TextPosition(0, 0), selection.Range!.Value.Start);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void ASelectionAcrossLinesAndPages_CopiesAllOfItAndHighlightsEachLine()
    {
        var doc = Document();
        var selection = new TextSelectionModel(doc.GetText, doc.PageCount);
        selection.Begin(0, 10);                                             // from "brown"
        selection.Extend(1, 8);                                             // to "A second"

        Assert.Equal("brown fox\njumps over the lazy dog\n\nA second", selection.GetSelectedText());
        Assert.Equal(2, selection.RectsOnPage(0).Count);
        Assert.Single(selection.RectsOnPage(1));
        Assert.Empty(selection.RectsOnPage(2));
        Assert.Equal(new PdfTextRange(10, doc.GetText(0).Length - 10), selection.SelectedOnPage(0));
        Assert.Equal(new PdfTextRange(0, 8), selection.SelectedOnPage(1));
    }

    [Fact]
    public void SelectWord_And_SelectLine_ExpandAroundACaret()
    {
        var doc = Document();
        var selection = new TextSelectionModel(doc.GetText, doc.PageCount);

        selection.SelectWord(0, 6);
        Assert.Equal("quick", selection.GetSelectedText());

        selection.SelectLine(0, 25);
        Assert.Equal("jumps over the lazy dog", selection.GetSelectedText());

        selection.SelectWord(0, doc.GetText(0).Length);                     // a caret at the very end belongs to the last word
        Assert.Equal("dog", selection.GetSelectedText());

        selection.SelectWord(2, 0);                                         // a page with no text selects nothing new
        Assert.Equal("dog", selection.GetSelectedText());
    }

    [Fact]
    public void SelectAll_CoversEveryPageWithText_AndClearEmptiesIt()
    {
        var doc = Document();
        var selection = new TextSelectionModel(doc.GetText, doc.PageCount);

        selection.SelectAll();

        Assert.Equal("The quick brown fox\njumps over the lazy dog\n\nA second page about the fox\n\nThe end", selection.GetSelectedText());
        selection.Clear();
        Assert.False(selection.HasSelection);
        Assert.Equal("", selection.GetSelectedText());
        Assert.Null(selection.Range);
        selection.Clear();                                                  // already clear: no change to report
    }

    [Fact]
    public void Extend_WithNoBegin_StartsASelectionAtThatPoint()
    {
        var doc = Document();
        var selection = new TextSelectionModel(doc.GetText, doc.PageCount);

        selection.Extend(0, 3);

        Assert.Equal(new TextPosition(0, 3), selection.Range!.Value.Start);
        Assert.False(selection.HasSelection);
        Assert.True(new TextPosition(0, 1) < new TextPosition(1, 0));
        Assert.True(new TextPosition(2, 5) > new TextPosition(2, 4));
    }

    [Fact]
    public void AnEmptyDocument_HasNothingToSelect()
    {
        var selection = new TextSelectionModel(_ => PdfPageSource.Open(TestDocument.Build([new TestPage(10, 10, "")])).GetText(0), 0);

        selection.SelectAll();

        Assert.False(selection.HasSelection);
        Assert.Throws<ArgumentNullException>(() => new TextSelectionModel(null!, 1));
    }

    [Fact]
    public async Task Search_FindsEveryOccurrenceInReadingOrderAndCountsThemPerPage()
    {
        var doc = Document();
        var progress = new List<SearchProgress>();

        var results = await Run(doc, "the", progress: new SyncProgress(progress.Add));

        Assert.Equal(4, results.Count);                                      // The, the (lazy), the (fox), The
        Assert.Equal([0, 0, 1, 3], results.Hits.Select(h => h.Page));
        Assert.Equal(2, results.CountOnPage(0));
        Assert.Equal(0, results.CountOnPage(2));
        Assert.Equal(0, results.CountOnPage(99));
        Assert.Equal(2, results.HitsOnPage(0).Count());
        Assert.Equal(4, progress.Count);
        Assert.Equal(new SearchProgress(4, 4, 4), progress[^1]);
        Assert.Equal(0, results.CurrentIndex);
    }

    [Fact]
    public async Task Search_HonorsTheOptions()
    {
        var doc = Document();

        Assert.Equal(2, (await Run(doc, "The", PdfTextSearchOptions.MatchCase)).Count);
        Assert.Equal(4, (await Run(doc, "the", PdfTextSearchOptions.WholeWord)).Count);
        Assert.Empty((await Run(doc, "ox", PdfTextSearchOptions.WholeWord)).Hits);
        Assert.Equal(2, (await Run(doc, "ox")).Count);
        Assert.Empty((await Run(doc, "   ")).Hits);
        Assert.Empty((await Run(doc, "zebra")).Hits);
    }

    [Fact]
    public async Task NextAndPrevious_WrapAroundAndMoveToTheFirstHitFromAPage()
    {
        var doc = Document();
        var results = await Run(doc, "fox");           // page 0, page 1

        Assert.Equal(0, results.Current!.Value.Page);
        Assert.Equal(1, results.Next()!.Value.Page);
        Assert.Equal(0, results.Next()!.Value.Page);                        // wrapped
        Assert.Equal(1, results.Previous()!.Value.Page);                    // wrapped back
        Assert.Equal(1, results.MoveToFirstFrom(1)!.Value.Page);
        Assert.Equal(0, results.MoveToFirstFrom(3)!.Value.Page);            // nothing after page 3: the first overall

        var none = await Run(doc, "zebra");
        Assert.Null(none.Next());
        Assert.Null(none.Previous());
        Assert.Null(none.MoveToFirstFrom(0));
        Assert.Null(none.Current);
        Assert.Equal(-1, none.CurrentIndex);
    }

    [Fact]
    public async Task Search_StopsWhenCancelled()
    {
        var doc = Document();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DocumentSearch.RunAsync(doc, "fox", cancellationToken: cts.Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Run(doc, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => DocumentSearch.RunAsync((ReaderDocument)null!, "x", cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>Reports on the thread that reports, so a test reads the reports in order without waiting for a synchronization context.</summary>
    private sealed class SyncProgress(Action<SearchProgress> report) : IProgress<SearchProgress>
    {
        public void Report(SearchProgress value) => report(value);
    }
}
