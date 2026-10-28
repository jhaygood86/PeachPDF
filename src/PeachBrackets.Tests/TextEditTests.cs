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
    [Fact]
    public async Task SplittingAParagraphInTheMiddleOfAWord_LaysOutLikeTwoParagraphs()
    {
        var (_, document, canvas) = await LoadAsync("<p>Hello world</p><p>After</p>");

        var result = await document.SplitParagraphAsync(new TextPosition(0, 5), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>Hello</p><p> world</p><p>After</p>");
        Assert.Equal(new TextPosition(1, 0), result.Caret);
    }

    [Fact]
    public async Task SplittingInsideAnInlineElement_ClonesItForTheTail()
    {
        var (_, document, canvas) = await LoadAsync("<p>Lunch <strong>at noon</strong> today</p>");

        var result = await document.SplitParagraphAsync(new TextPosition(0, 9), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>Lunch <strong>at </strong></p><p><strong>noon</strong> today</p>");
    }

    [Fact]
    public async Task SplittingAtTheEnd_GivesAnEmptyParagraph_AndAtTheStartAnEmptyFirstOne()
    {
        var (_, document, canvas) = await LoadAsync("<p>one</p><p>two</p>");

        var atEnd = await document.SplitParagraphAsync(new TextPosition(0, 3), canvas);
        Assert.True(atEnd.Applied, atEnd.Reason);
        Assert.NotNull(atEnd.Block);
        await AssertLaysOutLike(document, "<p>one</p><p></p><p>two</p>");

        var filled = await document.InsertTextAsync(atEnd.Block!, "new", canvas);
        Assert.True(filled.Applied, filled.Reason);
        await AssertLaysOutLike(document, "<p>one</p><p>new</p><p>two</p>");
    }

    [Fact]
    public async Task JoiningWithTheNextParagraph_LaysOutLikeOneParagraph()
    {
        var (_, document, canvas) = await LoadAsync("<p>one two</p><p>three <em>four</em></p><p>five</p>");

        var result = await document.JoinWithNextParagraphAsync(new TextPosition(0, 2), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>one twothree <em>four</em></p><p>five</p>");
        Assert.Equal(new TextPosition(0, 7), result.Caret);
    }

    [Fact]
    public async Task SplitThenJoin_RestoresTheDocument_AndPagesReflow()
    {
        var body = string.Concat(Enumerable.Range(0, 60).Select(i => $"<p>Paragraph {i} with some text in it to fill the line.</p>"));
        var (_, document, canvas) = await LoadAsync(body);
        var pages = document.Pages.Count;

        var split = await document.SplitParagraphAsync(new TextPosition(0, 9), canvas);
        Assert.True(split.Applied, split.Reason);
        Assert.Equal(pages, split.PagesBefore);

        var join = await document.JoinWithNextParagraphAsync(new TextPosition(0, 1), canvas);
        Assert.True(join.Applied, join.Reason);
        await AssertLaysOutLike(document, body);
    }

    [Fact]
    public async Task StructureEditsInsideAnythingButAPlainParagraph_AreRefused()
    {
        var (_, document, canvas) = await LoadAsync("<ul><li>one</li><li>two</li></ul><div style='display:flex'><p>fx</p></div><p>last</p>");

        var inBlockOfBlocks = await document.SplitParagraphAsync(new TextPosition(Enumerable.Range(0, document.TextRuns.Count).First(i => document.TextRuns[i].Text == "fx"), 1), canvas);
        Assert.False(inBlockOfBlocks.Applied);
        var lastParagraph = document.TextRuns.Count - 1;
        var noNext = await document.JoinWithNextParagraphAsync(new TextPosition(lastParagraph, 1), canvas);
        Assert.False(noNext.Applied);
        Assert.NotNull(noNext.Reason);
    }
    [Fact]
    public async Task ARangeGivenBackwards_IsTheSameEdit()
    {
        var (_, document, canvas) = await LoadAsync("<p>one two</p><p>three four</p>");

        var result = await document.DeleteTextAsync(new TextPosition(1, 6), new TextPosition(0, 4), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>one </p><p>four</p>");
    }

    [Fact]
    public async Task DeletingWholeParagraphsOfText_RemovesTheirBoxes_AndKeepsACaret()
    {
        var (_, document, canvas) = await LoadAsync("<p>one</p><p>two</p><p>three</p>");

        var result = await document.DeleteTextAsync(new TextPosition(0, 0), new TextPosition(1, 3), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p></p><p></p><p>three</p>");
        Assert.NotNull(result.Caret);
    }

    [Fact]
    public async Task PositionsOutsideTheDocument_AreRefused()
    {
        var (_, document, canvas) = await LoadAsync("<p>one</p>");

        Assert.False((await document.InsertTextAsync(new TextPosition(5, 0), "x", canvas)).Applied);
        Assert.False((await document.InsertTextAsync(new TextPosition(0, 99), "x", canvas)).Applied);
        Assert.False((await document.SplitParagraphAsync(new TextPosition(5, 0), canvas)).Applied);
        Assert.False((await document.JoinWithNextParagraphAsync(new TextPosition(5, 0), canvas)).Applied);
    }

    [Fact]
    public async Task InsertingIntoAnElement_AppendsToItsText_OrRefusesWhatCannotHoldText()
    {
        var (_, document, canvas) = await LoadAsync("<p id=a>one</p><div id=b><p>x</p></div><p id=c></p><img id=d width=10 height=10><p id=e style='display:none'>h</p>");

        var appended = await document.InsertTextAsync(document.GetElement("a")!, " more", canvas);
        Assert.True(appended.Applied, appended.Reason);

        Assert.False((await document.InsertTextAsync(document.GetElement("b")!, "y", canvas)).Applied);
        Assert.False((await document.InsertTextAsync(document.GetElement("d")!, "y", canvas)).Applied);
        Assert.False((await document.InsertTextAsync(document.GetElement("e")!, "y", canvas)).Applied);

        var filled = await document.InsertTextAsync(document.GetElement("c")!, "filled", canvas);
        Assert.True(filled.Applied, filled.Reason);
    }

    [Fact]
    public async Task SplittingAtTheStartOfAParagraph_MovesEverythingToTheNewOne()
    {
        var (_, document, canvas) = await LoadAsync("<p>one <em>two</em></p><p>after</p>");

        var result = await document.SplitParagraphAsync(new TextPosition(0, 0), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p></p><p>one <em>two</em></p><p>after</p>");
    }
    [Fact]
    public async Task DeletingASpaceBeforeAnInlineBoundary_KeepsTheSpaceLoadingHadCollapsedIntoIt()
    {
        var (_, document, canvas) = await LoadAsync("<p>ab <b> cd</b></p>");
        Assert.Equal("ab cd", Assert.Single(document.TextRuns).Text);

        var result = await document.DeleteTextAsync(new TextPosition(0, 2), new TextPosition(0, 3), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>ab<b> cd</b></p>");
    }

    [Fact]
    public async Task SplittingAParagraph_KeepsTheEmptyAnchorsItAlreadyHad()
    {
        var (_, document, canvas) = await LoadAsync("<p>ab <a id=t></a>cd ef</p>");

        var result = await document.SplitParagraphAsync(new TextPosition(0, 5), canvas);

        Assert.True(result.Applied, result.Reason);
        Assert.NotNull(document.GetElement("t"));
    }
    [Fact]
    public async Task ASplitParagraph_KeepsEveryStyleOfTheOriginal_IncludingThoseInheritStyleDoesNotCopy()
    {
        const string head = Head + "<style>p.box { min-height: 80pt; border-left: 4pt solid red; padding-left: 5pt }</style>";
        var (_, document, canvas) = await LoadAsync("<p class=box>Hello world</p><p>After</p>", head);

        var result = await document.SplitParagraphAsync(new TextPosition(0, 5), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p class=box>Hello</p><p class=box> world</p><p>After</p>", head);
    }
    private static string ManyParagraphs(int count, Func<int, string?>? text = null) =>
        string.Concat(Enumerable.Range(0, count).Select(i => $"<p>{text?.Invoke(i) ?? $"Paragraph {i} with some text in it to fill the line."}</p>"));

    [Fact]
    public async Task AnEditOnALatePage_ResumesLayoutThere_AndMatchesAFreshLoad()
    {
        var (_, document, canvas) = await LoadAsync(ManyParagraphs(120));
        Assert.True(document.Pages.Count >= 5);

        // Deep in the document: the run whose text is "Paragraph 100 ...".
        var index = Enumerable.Range(0, document.TextRuns.Count).First(i => document.TextRuns[i].Text.StartsWith("Paragraph 100 ", StringComparison.Ordinal));
        var result = await document.InsertTextAsync(new TextPosition(index, 13), " and then a good deal more words that make this one wrap onto a second line", canvas);

        Assert.True(result.Applied, result.Reason);
        Assert.True(result.ResumedFromPage > 0, $"expected a resumed layout, got {result.ResumedFromPage}");
        await AssertLaysOutLike(document, ManyParagraphs(120, i => i == 100 ? "Paragraph 100 and then a good deal more words that make this one wrap onto a second line with some text in it to fill the line." : null));
    }

    [Fact]
    public async Task SeveralResumedEditsInARow_StayEqualToAFreshLoad()
    {
        var (_, document, canvas) = await LoadAsync(ManyParagraphs(150));
        var expected = Enumerable.Range(0, 150).Select(i => $"Paragraph {i} with some text in it to fill the line.").ToList();

        foreach (var paragraph in new[] { 120, 60, 140, 90 })
        {
            var index = Enumerable.Range(0, document.TextRuns.Count).First(i => document.TextRuns[i].Text.StartsWith($"Paragraph {paragraph} ", StringComparison.Ordinal));
            var result = await document.InsertTextAsync(new TextPosition(index, 4), "XX", canvas);
            Assert.True(result.Applied, result.Reason);
            expected[paragraph] = expected[paragraph].Insert(4, "XX");
        }

        await AssertLaysOutLike(document, ManyParagraphs(150, i => expected[i]));
    }

    [Fact]
    public async Task SplittingJoiningAndDeletingOnLatePages_ResumeAndMatchAFreshLoadAfterEachStep()
    {
        var (_, document, canvas) = await LoadAsync(ManyParagraphs(150));
        var texts = Enumerable.Range(0, 150).Select(i => $"Paragraph {i} with some text in it to fill the line.").ToList();

        int RunOf(string start) => Enumerable.Range(0, document.TextRuns.Count).First(i => document.TextRuns[i].Text.StartsWith(start, StringComparison.Ordinal));
        string Markup() => string.Concat(texts.Select(t => $"<p>{t}</p>"));

        var split = await document.SplitParagraphAsync(new TextPosition(RunOf("Paragraph 110 "), 14), canvas);
        Assert.True(split.Applied, split.Reason);
        Assert.True(split.ResumedFromPage > 0);
        texts.Insert(111, texts[110][14..]);
        texts[110] = texts[110][..14];
        await AssertLaysOutLike(document, Markup());

        var join = await document.JoinWithNextParagraphAsync(new TextPosition(RunOf("Paragraph 70 "), 3), canvas);
        Assert.True(join.Applied, join.Reason);
        texts[70] += texts[71];
        texts.RemoveAt(71);
        await AssertLaysOutLike(document, Markup());

        var at = RunOf("Paragraph 129 ");
        var delete = await document.DeleteTextAsync(new TextPosition(at, 0), new TextPosition(at, 14), canvas);
        Assert.True(delete.Applied, delete.Reason);
        texts[129] = texts[129][14..];
        await AssertLaysOutLike(document, Markup());
    }
    private const string LongLine = "lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore magna aliqua ut enim ad minim veniam";

    // Every edit of a sweep must lay out like a fresh load of the edited markup, for documents where page breaks are decided by widows,
    // orphans, keeps and floats - the places a resumed layout can differ from a whole one.
    [Theory]
    [InlineData("p { margin: 6pt 0 }")]
    [InlineData("p { margin: 6pt 0; orphans: 3; widows: 3 }")]
    [InlineData("p { margin: 6pt 0; break-inside: avoid }")]
    [InlineData("blockquote { margin: 4pt 0; widows: 3 } p { margin: 6pt 0 }")]
    [InlineData("p { margin: 6pt 0 } p:nth-child(6n) { break-after: avoid }")]
    [InlineData("div { border: 2pt solid; padding: 4pt; margin: 3pt 0 } p { margin: 6pt 0 }")]
    [InlineData("p.f { float: right; width: 60pt; margin: 2pt }")]
    public async Task ASweepOfEdits_OverPageBreakHeavyDocuments_AlwaysMatchesAFreshLoad(string css)
    {
        var head = "<style>body { font-size: 14pt } " + css + "</style>";
        var texts = Enumerable.Range(0, 80).Select(i => $"P{i} {LongLine}").ToList();
        string Markup() => string.Concat(texts.Select((t, i) => css.Contains("blockquote") ? $"<blockquote><p>{t}</p></blockquote>"
            : css.StartsWith("div", StringComparison.Ordinal) ? $"<div><p>{t}</p></div>"
            : css.Contains("p.f") ? $"<p{(i % 7 == 6 ? " class=f" : string.Empty)}>{t}</p>"
            : $"<p>{t}</p>"));

        var (_, document, canvas) = await LoadAsync(Markup(), head);

        foreach (var target in new[] { 10, 12, 30, 31, 45, 60, 70, 20 })
        {
            var run = Enumerable.Range(0, document.TextRuns.Count).First(i => document.TextRuns[i].Text.StartsWith($"P{target} ", StringComparison.Ordinal));
            var result = await document.InsertTextAsync(new TextPosition(run, 3), "Q a few more words here that make it longer ", canvas);
            Assert.True(result.Applied, result.Reason);
            texts[target] = texts[target].Insert(3, "Q a few more words here that make it longer ");

            await AssertLaysOutLike(document, Markup(), head);
        }
    }

    [Fact]
    public async Task AFlexDocument_IsLaidOutWhole_SoItsExtentMatchesAFreshLoad()
    {
        var texts = Enumerable.Range(0, 40).Select(i => $"P{i} {LongLine}").ToList();
        string Markup() => string.Concat(texts.Select(t => $"<div style='display:flex'><p style='flex:1'>{t}</p><p style='flex:1'>b</p></div>"));
        var (_, document, canvas) = await LoadAsync(Markup());

        var run = Enumerable.Range(0, document.TextRuns.Count).First(i => document.TextRuns[i].Text.StartsWith("P30 ", StringComparison.Ordinal));
        var result = await document.InsertTextAsync(new TextPosition(run, 3), "ZZ ", canvas);

        Assert.True(result.Applied, result.Reason);
        Assert.Null(result.ResumedFromPage);
        texts[30] = texts[30].Insert(3, "ZZ ");
        await AssertLaysOutLike(document, Markup());
    }
    [Fact]
    public async Task SplittingTheLastParagraph_ReevaluatesStructuralSelectorsForBothHalves()
    {
        const string head = Head + "<style>p:last-child { margin-top: 40pt; color: rgb(200, 0, 0) } p:first-child { margin-bottom: 20pt }</style>";
        var (_, document, canvas) = await LoadAsync("<p>one</p><p>Hello world</p>", head);

        var result = await document.SplitParagraphAsync(new TextPosition(1, 5), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>one</p><p>Hello</p><p> world</p>", head);
    }

    [Fact]
    public async Task JoiningTheLastTwoParagraphs_ReevaluatesTheSurvivorsStructuralSelectors()
    {
        const string head = Head + "<style>p:last-child { margin-top: 40pt }</style>";
        var (_, document, canvas) = await LoadAsync("<p>one</p><p>two</p><p>three</p>", head);

        var result = await document.JoinWithNextParagraphAsync(new TextPosition(1, 1), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>one</p><p>twothree</p>", head);
    }
    [Fact]
    public async Task SplittingAMiddleParagraph_ReevaluatesNthChildForEveryLaterSibling()
    {
        const string head = Head + "<style>p:nth-child(even) { margin-top: 30pt; color: rgb(0, 120, 0) } p:nth-last-child(2) { margin-bottom: 25pt }</style>";
        var (_, document, canvas) = await LoadAsync("<p>one</p><p>two words</p><p>three</p><p>four</p><p>five</p>", head);

        var result = await document.SplitParagraphAsync(new TextPosition(1, 3), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>one</p><p>two</p><p> words</p><p>three</p><p>four</p><p>five</p>", head);
    }

    [Fact]
    public async Task JoiningParagraphs_ReevaluatesNthChildForEveryLaterSibling()
    {
        const string head = Head + "<style>p:nth-child(odd) { margin-top: 30pt; color: rgb(0, 120, 0) } p ~ p + p { padding-left: 12pt }</style>";
        var (_, document, canvas) = await LoadAsync("<p>one</p><p>two</p><p>three</p><p>four</p><p>five</p>", head);

        var result = await document.JoinWithNextParagraphAsync(new TextPosition(1, 1), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>one</p><p>twothree</p><p>four</p><p>five</p>", head);
    }

    [Theory]
    [InlineData("ul")]
    [InlineData("ol")]
    public async Task SplittingAListItem_LaysOutLikeTheMarkupWithTwoItems(string list)
    {
        var (_, document, canvas) = await LoadAsync($"<{list}><li>one</li><li>two words</li><li>three</li></{list}>");

        var run = Enumerable.Range(0, document.TextRuns.Count).First(i => document.TextRuns[i].Text.StartsWith("two", StringComparison.Ordinal));
        var result = await document.SplitParagraphAsync(new TextPosition(run, 3), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, $"<{list}><li>one</li><li>two</li><li> words</li><li>three</li></{list}>");
    }

    [Fact]
    public async Task JoiningListItems_LaysOutLikeTheMarkupWithOneItem()
    {
        var (_, document, canvas) = await LoadAsync("<ol><li>one</li><li>two</li><li>three</li><li>four</li></ol>");

        var run = Enumerable.Range(0, document.TextRuns.Count).First(i => document.TextRuns[i].Text.StartsWith("two", StringComparison.Ordinal));
        var result = await document.JoinWithNextParagraphAsync(new TextPosition(run, 1), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<ol><li>one</li><li>twothree</li><li>four</li></ol>");
    }

    private const string CounterHead = Head + "<style>body { counter-reset: n } p { counter-increment: n } div::before { content: counter(n) '. ' }</style>";

    [Fact]
    public async Task SplittingAParagraph_RenumbersTheCountersAfterIt()
    {
        var (_, document, canvas) = await LoadAsync("<p>one</p><p>two words</p><p>three</p><div>end</div>", CounterHead);

        var run = Enumerable.Range(0, document.TextRuns.Count).First(i => document.TextRuns[i].Text.Contains("two", StringComparison.Ordinal));
        var result = await document.SplitParagraphAsync(new TextPosition(run, 3), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>one</p><p>two</p><p> words</p><p>three</p><div>end</div>", CounterHead);
    }

    [Fact]
    public async Task JoiningParagraphs_RenumbersTheCountersAfterThem()
    {
        var (_, document, canvas) = await LoadAsync("<p>one</p><p>two</p><p>three</p><div>end</div>", CounterHead);

        var run = Enumerable.Range(0, document.TextRuns.Count).First(i => document.TextRuns[i].Text.Contains("two", StringComparison.Ordinal));
        var result = await document.JoinWithNextParagraphAsync(new TextPosition(run, 1), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>one</p><p>twothree</p><div>end</div>", CounterHead);
    }

    [Fact]
    public async Task SplittingInsideAQuote_ShiftsTheQuoteNestingAfterIt()
    {
        const string head = Head + "<style>q::before { content: open-quote } q::after { content: no-close-quote }</style>";
        var (_, document, canvas) = await LoadAsync("<p><q>aa bb</q></p><p><q>cc</q></p><p><q>dd</q></p>", head);

        var run = Enumerable.Range(0, document.TextRuns.Count).First(i => document.TextRuns[i].Text.Contains("aa", StringComparison.Ordinal));
        var result = await document.SplitParagraphAsync(new TextPosition(run, 3), canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p><q>aa</q></p><p><q> bb</q></p><p><q>cc</q></p><p><q>dd</q></p>", head);
    }

    [Fact]
    public async Task WrappingAWordInStrong_LaysOutLikeTheMarkupWithIt_AndPutsTheCaretAfterIt()
    {
        var (_, document, canvas) = await LoadAsync("<p>The quick brown fox</p>");

        var result = await document.WrapRangeAsync(new TextPosition(0, 4), new TextPosition(0, 9), "strong", null, canvas);

        Assert.True(result.Applied, result.Reason);
        Assert.NotNull(result.Block);
        await AssertLaysOutLike(document, "<p>The <strong>quick</strong> brown fox</p>");
        Assert.Equal(new TextPosition(0, 9), result.Caret);
    }

    [Fact]
    public async Task WrappingARange_AtTheStartMiddleAndEndOfATextBox_CutsItExactly()
    {
        var (_, document, canvas) = await LoadAsync("<p>alpha beta gamma</p>");

        Assert.True((await document.WrapRangeAsync(new TextPosition(0, 0), new TextPosition(0, 5), "em", null, canvas)).Applied);
        await AssertLaysOutLike(document, "<p><em>alpha</em> beta gamma</p>");

        Assert.True((await document.WrapRangeAsync(new TextPosition(0, 11), new TextPosition(0, 16), "u", null, canvas)).Applied);
        await AssertLaysOutLike(document, "<p><em>alpha</em> beta <u>gamma</u></p>");
    }

    [Fact]
    public async Task WrappingInALink_KeepsItsAttributes_AndALinkIsFoundOnThePage()
    {
        var (_, document, canvas) = await LoadAsync("<p>See the docs for more</p>");

        var result = await document.WrapRangeAsync(new TextPosition(0, 8), new TextPosition(0, 12), "a",
            new Dictionary<string, string> { ["href"] = "https://example.com/" }, canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>See the <a href=\"https://example.com/\">docs</a> for more</p>");
    }

    [Fact]
    public async Task UnwrappingAnElement_LeavesItsTextWhereItWas_AndJoinsTheTextAroundIt()
    {
        var (_, document, canvas) = await LoadAsync("<p>The <strong id=s>quick</strong> brown</p>");

        var result = await document.UnwrapElementAsync(document.GetElement("s")!, canvas);

        Assert.True(result.Applied, result.Reason);
        await AssertLaysOutLike(document, "<p>The quick brown</p>");
    }

    [Fact]
    public async Task WrapAndUnwrap_RoundTrip_OnALateRepeatedPage()
    {
        var body = ManyParagraphs(120);
        var (_, document, canvas) = await LoadAsync(body);
        var run = Enumerable.Range(0, document.TextRuns.Count).First(i => document.TextRuns[i].Text.StartsWith("Paragraph 100 ", StringComparison.Ordinal));

        var wrapped = await document.WrapRangeAsync(new TextPosition(run, 0), new TextPosition(run, 9), "strong", null, canvas);
        Assert.True(wrapped.Applied, wrapped.Reason);
        Assert.True(wrapped.ResumedFromPage > 0);
        await AssertLaysOutLike(document, ManyParagraphs(120, i => i == 100 ? "<strong>Paragraph</strong> 100 with some text in it to fill the line." : null));

        var unwrapped = await document.UnwrapElementAsync(wrapped.Block!, canvas);
        Assert.True(unwrapped.Applied, unwrapped.Reason);
        await AssertLaysOutLike(document, body);
    }

    [Fact]
    public async Task WrapAndUnwrapAreRefused_ForRangesAndElementsTheyCannotHandle()
    {
        var (_, document, canvas) = await LoadAsync("<p>one <em>two</em> three</p><div id=d><p>x</p></div><p id=p>last</p>");

        // across an element boundary, an empty range, an element that is not phrasing, a script-like attribute
        Assert.False((await document.WrapRangeAsync(new TextPosition(0, 1), new TextPosition(0, 6), "strong", null, canvas)).Applied);
        Assert.False((await document.WrapRangeAsync(new TextPosition(0, 1), new TextPosition(0, 1), "strong", null, canvas)).Applied);
        Assert.False((await document.WrapRangeAsync(new TextPosition(0, 0), new TextPosition(0, 2), "div", null, canvas)).Applied);
        Assert.False((await document.WrapRangeAsync(new TextPosition(0, 0), new TextPosition(0, 2), "span",
            new Dictionary<string, string> { ["onclick"] = "x" }, canvas)).Applied);
        Assert.False((await document.UnwrapElementAsync(document.GetElement("d")!, canvas)).Applied);
        Assert.False((await document.UnwrapElementAsync(document.GetElement("p")!, canvas)).Applied);
    }

    [Fact]
    public async Task ASplitParagraph_DoesNotDuplicateTheId()
    {
        var (_, document, canvas) = await LoadAsync("<p id=a>Hello world</p>");

        var result = await document.SplitParagraphAsync(new TextPosition(0, 5), canvas);

        Assert.True(result.Applied, result.Reason);
        Assert.Equal("a", document.GetElement("a")!.Id);
        Assert.NotEqual(document.GetElement("a"), result.Block);
        Assert.True(string.IsNullOrEmpty(result.Block!.Id), "the new paragraph kept the id");
    }
}
