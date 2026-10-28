using PeachBrackets.Text;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class TextEditTests
{
    private const string Head = "<style>body { font-size: 14pt } p { margin: 6pt 0 }</style>";

    private static HtmlDocumentOptions Options() => new() { RecordTextRuns = true, EnableIncrementalUpdates = true, PageWidthPoints = 300 };

    private static async Task<(RasterRenderContext Context, HtmlDocument Document, Canvas Measuring)> LoadAsync(string body, string head = Head, HtmlDocumentOptions? options = null)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, head), options ?? Options());
        return (context, document, context.CreateCanvas(1, 1));
    }

    // The edited document must lay out exactly as a fresh load of the markup the edit describes.
    private static async Task AssertLaysOutLike(HtmlDocument edited, string expectedBody, string head = Head)
    {
        var (_, fresh, _) = await LoadAsync(expectedBody, head);

        Assert.Equal(fresh.Pages.Count, edited.Pages.Count);
        Assert.Equal(fresh.ContentExtent, edited.ContentExtent);
        Assert.Equal(fresh.TextRuns.Select(r => r.Text), edited.TextRuns.Select(r => r.Text));
        for (var i = 0; i < fresh.TextRuns.Count; i++)
        {
            var a = fresh.TextRuns[i];
            var b = edited.TextRuns[i];
            Assert.Equal(a.PageIndex, b.PageIndex);
            Assert.Equal(a.Baseline, b.Baseline, 3);
            Assert.Equal(a.Bounds.X, b.Bounds.X, 3);
            Assert.Equal(a.Bounds.Width, b.Bounds.Width, 3);
            Assert.Equal(a.CharacterOffsets.Count, b.CharacterOffsets.Count);
            for (var k = 0; k < a.CharacterOffsets.Count; k++)
                Assert.Equal(a.CharacterOffsets[k], b.CharacterOffsets[k], 3);
        }
    }

    [Fact]
    public async Task InsertingInsideAWord_LaysOutLikeTheEditedMarkup_AndPutsTheCaretAfterIt()
    {
        var (_, document, canvas) = await LoadAsync("<p>Hello world</p>");

        var result = await document.InsertTextAsync(new TextPosition(0, 5), " big", canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>Hello big world</p>");
        Assert.Equal(new TextPosition(0, 9), result.Caret);
        Assert.Equal(1, result.PagesBefore);
        Assert.Equal(1, result.PagesAfter);
        Assert.Equal(0, result.FirstDirtyPage);
        Assert.NotEmpty(result.DirtyRegions);
    }

    [Fact]
    public async Task DeletingWithinAWord_AndReplacing_LayOutLikeTheEditedMarkup()
    {
        var (_, document, canvas) = await LoadAsync("<p>Hello world</p>");

        Assert.True((await document.DeleteTextAsync(new TextPosition(0, 1), new TextPosition(0, 4), canvas)).Applied);
        await AssertLaysOutLike(document, "<p>Ho world</p>");

        var replaced = await document.ReplaceTextAsync(new TextPosition(0, 3), new TextPosition(0, 8), "there", canvas);
        Assert.True(replaced.Applied, replaced.Reason);
        await AssertLaysOutLike(document, "<p>Ho there</p>");
    }

    [Fact]
    public async Task ARangeAcrossInlineElements_IsDeletedBetweenItsEnds()
    {
        var (_, document, canvas) = await LoadAsync("<p>Lunch at <strong>noon</strong> today</p>");
        var run = Assert.Single(document.TextRuns);
        Assert.Equal("Lunch at noon today", run.Text);

        // From between the "a" and the "t" of "at" to between the "no" and the "on" of "noon".
        var result = await document.DeleteTextAsync(new TextPosition(0, 7), new TextPosition(0, 11), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>Lunch a<strong>on</strong> today</p>");
    }

    [Fact]
    public async Task ARangeAcrossParagraphs_LeavesTwoParagraphs_NotJoinedOnes()
    {
        var (_, document, canvas) = await LoadAsync("<p>one two</p><p>three four</p>");

        var result = await document.DeleteTextAsync(new TextPosition(0, 4), new TextPosition(1, 6), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>one </p><p>four</p>");
    }

    [Fact]
    public async Task DeletingAllTheTextOfAnInlineElement_RemovesItsTextBox()
    {
        var (_, document, canvas) = await LoadAsync("<p>a <strong>bold</strong> b</p>");
        var run = Assert.Single(document.TextRuns);
        var at = run.Text.IndexOf("bold", StringComparison.Ordinal);

        var result = await document.DeleteTextAsync(new TextPosition(0, at), new TextPosition(0, at + 4), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>a <strong></strong> b</p>");
        Assert.NotNull(result.Caret);
    }

    [Fact]
    public async Task AnEditThatGrowsAParagraph_MovesWhatFollowsIt_AndReportsTheFirstDirtyPage()
    {
        var (_, document, canvas) = await LoadAsync("<p id=a>short</p><p id=after>After</p>");
        var before = document.GetElementBounds("after")!.Value;

        var words = string.Join(' ', Enumerable.Repeat("lorem ipsum", 60));
        var result = await document.InsertTextAsync(new TextPosition(0, 5), " " + words, canvas);

        Assert.True(result.Applied, result.Reason);
        Assert.True(document.GetElementBounds("after")!.Value.Top > before.Top + 100, "the paragraph after it did not move down");
        await AssertLaysOutLike(document, "<p id=a>short " + words + "</p><p id=after>After</p>");
    }

    [Fact]
    public async Task AnEditOnAnEarlyPage_ReflowsThePagesAfterIt_AndALateOneLeavesTheEarlyOnesAlone()
    {
        var body = string.Concat(Enumerable.Range(0, 120).Select(i => $"<p>Paragraph {i} with some text in it to fill the line.</p>"));
        var (_, document, canvas) = await LoadAsync(body);
        var pages = document.Pages.Count;
        Assert.True(pages >= 3, $"expected several pages, got {pages}");

        var lateRun = document.TextRuns.Select((run, index) => (run, index)).Last(x => x.run.PageIndex == pages - 1).index;
        var result = await document.InsertTextAsync(new TextPosition(lateRun, 3), "XX", canvas);
        Assert.True(result.Applied, result.Reason);
        Assert.True(result.FirstDirtyPage >= pages - 2, $"an edit on the last page dirtied page {result.FirstDirtyPage}");

        var grown = string.Join(' ', Enumerable.Repeat("added words", 120));
        var early = await document.InsertTextAsync(new TextPosition(0, 5), " " + grown, canvas);
        Assert.True(early.Applied, early.Reason);
        Assert.Equal(0, early.FirstDirtyPage);
        Assert.True(early.PagesAfter >= early.PagesBefore);
        Assert.Equal(early.PagesAfter - early.FirstDirtyPage!.Value, early.DirtyRegions.Count);
    }

    [Fact]
    public async Task GivingAnEmptyElementSomeText_LaysOutLikeTheMarkupThatHadIt()
    {
        var (_, document, canvas) = await LoadAsync("<p id=e></p><p>After</p>");
        Assert.Single(document.TextRuns);

        var result = await document.InsertTextAsync(document.GetElement("e")!, "typed here", canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p id=e>typed here</p><p>After</p>");
        Assert.Equal(new TextPosition(0, 10), result.Caret);
    }

    [Fact]
    public async Task TypingOneCharacterAtATime_FollowsTheCaretTheResultReturns()
    {
        var (_, document, canvas) = await LoadAsync("<p>ab</p>");
        var caret = new TextPosition(0, 2);

        foreach (var typed in "cde")
        {
            var result = await document.InsertTextAsync(caret, typed.ToString(), canvas);
            Assert.True(result.Applied, result.Reason);
            caret = result.Caret!.Value;
        }

        await AssertLaysOutLike(document, "<p>abcde</p>");
        Assert.Equal(new TextPosition(0, 5), caret);
        Assert.Equal(caret, document.Selection.Focus);
        Assert.True(document.Selection.IsCollapsed);
    }

    [Fact]
    public async Task TheSpaceBetweenWords_AndALeadingSpace_Collapse()
    {
        var (_, document, canvas) = await LoadAsync("<p>one <em>two</em></p>");

        // A space typed at the start of an inline whose neighbour already ends in one collapses, as when it is loaded.
        var at = document.TextRuns[0].Text.IndexOf("two", StringComparison.Ordinal);
        var result = await document.InsertTextAsync(new TextPosition(0, at), " ", canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>one <em> two</em></p>");
    }

    [Fact]
    public async Task APositionAfterASoftHyphen_MapsPastIt()
    {
        var (_, document, canvas) = await LoadAsync("<p>co&shy;operate</p>");
        Assert.Equal("cooperate", document.TextRuns[0].Text);

        var result = await document.InsertTextAsync(new TextPosition(0, 3), "X", canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>co&shy;oXperate</p>");
    }

    [Fact]
    public async Task RightToLeftText_IsEditedInLogicalOrder()
    {
        const string hebrew = "שלום";
        var (_, document, canvas) = await LoadAsync($"<p dir='rtl'>{hebrew} abc</p>");

        var result = await document.InsertTextAsync(new TextPosition(0, 2), "x", canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, $"<p dir='rtl'>{hebrew[..2]}x{hebrew[2..]} abc</p>");
    }

    [Fact]
    public async Task WhatCannotBeEditedInPlace_IsRefusedAndLeavesTheDocumentAlone()
    {
        // A document whose layout depends on every page: a running element, here.
        var (_, running, runningCanvas) = await LoadAsync("<div id=h>Header</div><p>Hello</p>",
            Head + "<style>#h { position: running(header) } @page { @top-center { content: element(header) } }</style>");
        var refused = await running.InsertTextAsync(new TextPosition(0, 1), "X", runningCanvas);
        Assert.False(refused.Applied);
        Assert.Contains("running", refused.Reason);

        // A page-referring document: target-counter(page).
        var (_, referring, referringCanvas) = await LoadAsync("<p><a id=l href='#t'>see</a></p><p id=t>Target</p>",
            Head + "<style>#l::after { content: ' p.' target-counter(attr(href), page) }</style>");
        var referred = await referring.InsertTextAsync(new TextPosition(0, 1), "X", referringCanvas);
        Assert.False(referred.Applied);
        Assert.Contains("page numbers", referred.Reason);

        // Generated content is not text of the document.
        var (_, generated, generatedCanvas) = await LoadAsync("<p>Hello</p>", Head + "<style>p::before { content: 'Gen ' }</style>");
        Assert.StartsWith("Gen ", generated.TextRuns[0].Text);
        var inGenerated = await generated.InsertTextAsync(new TextPosition(0, 1), "X", generatedCanvas);
        Assert.False(inGenerated.Applied);
        Assert.Contains("generated", inGenerated.Reason);
        Assert.StartsWith("Gen Hello", generated.TextRuns[0].Text);

        // A position that is not in the text.
        var (_, plain, plainCanvas) = await LoadAsync("<p>Hello</p>");
        Assert.False((await plain.InsertTextAsync(new TextPosition(9, 0), "X", plainCanvas)).Applied);
        Assert.False((await plain.InsertTextAsync(new TextPosition(0, 99), "X", plainCanvas)).Applied);
        Assert.Equal("Hello", plain.TextRuns[0].Text);
    }

    [Fact]
    public async Task ATableWithARepeatingHeader_LaysOutLikeAFreshLoadAfterAnEdit()
    {
        // A <thead> repeats on every page through per-page proxies that a second layout over the same tree once mistook for the header.
        string Table(string firstCell) =>
            "<table style='width:100%'><thead><tr><th>Head A</th><th>Head B</th></tr></thead><tbody>"
            + $"<tr><td>{firstCell}</td><td>x</td></tr>"
            + string.Concat(Enumerable.Range(0, 90).Select(i => $"<tr><td>Row {i} cell text</td><td>more {i}</td></tr>"))
            + "</tbody></table>";

        var (_, document, canvas) = await LoadAsync(Table("first"));
        Assert.True(document.Pages.Count >= 2, "the table should span pages");

        var run = document.TextRuns.Select((r, i) => (r, i)).First(x => x.r.Text == "first");
        var result = await document.InsertTextAsync(new TextPosition(run.i, 5), " cell grown into several words of text", canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, Table("first cell grown into several words of text"));

        // And again: a second edit over the already re-laid-out tree.
        var again = document.TextRuns.Select((r, i) => (r, i)).First(x => x.r.Text.StartsWith("first cell", StringComparison.Ordinal));
        var lastOfCell = document.TextRuns.Select((r, i) => (r, i)).Last(x => x.r.BlockId == again.r.BlockId);
        Assert.True((await document.DeleteTextAsync(new TextPosition(again.i, 5), new TextPosition(lastOfCell.i, lastOfCell.r.Text.Length), canvas)).Applied);
        await AssertLaysOutLike(document, Table("first"));
    }

    [Fact]
    public async Task PreformattedText_KeepsItsNewlines_WhenEdited()
    {
        const string head = Head + "<style>pre { white-space: pre-wrap; margin: 0 }</style>";
        var (_, document, canvas) = await LoadAsync("<pre>one\ntwo\nthree</pre>", head);
        Assert.Equal(3, document.TextRuns.Count);

        var result = await document.InsertTextAsync(new TextPosition(1, 3), "\nTWO-AND-A-HALF", canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<pre>one\ntwo\nTWO-AND-A-HALF\nthree</pre>", head);
    }

    [Fact]
    public async Task EditingNeedsTheOptions()
    {
        var options = new HtmlDocumentOptions { RecordTextRuns = true };
        var (_, document, canvas) = await LoadAsync("<p>Hello</p>", Head, options);

        await Assert.ThrowsAsync<InvalidOperationException>(() => document.InsertTextAsync(new TextPosition(0, 1), "X", canvas));
    }
}
