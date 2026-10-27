using PeachBrackets.Text;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class TextGeometryTests
{
    private const string Lunch = "<p>Lunch at <strong>12</strong>? See <a href=x>the menu</a> \U0001F600</p>";
    private const string LunchText = "Lunch at 12? See the menu \U0001F600";

    private static HtmlDocumentOptions Narrow(bool record = true)
    {
        var options = new HtmlDocumentOptions { RecordTextRuns = record, PageWidthPoints = 130 };
        options.SetMargins(10);
        return options;
    }

    private static async Task<HtmlDocument> LoadAsync(string body, HtmlDocumentOptions? options = null) =>
        (await TestDocument.LoadAsync(TestDocument.Wrap(body, "<style>body { font-size: 14pt }</style>"), options ?? Narrow())).Document;

    private static TextPosition Start(HtmlDocument document) => new(0, 0);

    private static TextPosition End(HtmlDocument document) => new(document.TextRuns.Count - 1, document.TextRuns[^1].Text.Length);

    [Fact]
    public async Task AWrappedParagraph_IsOneRunPerLine_InOneBlock()
    {
        var document = await LoadAsync(Lunch);

        Assert.True(document.TextRuns.Count >= 2, "the paragraph should have wrapped");
        Assert.Single(document.TextRuns.Select(run => run.BlockId).Distinct());
        Assert.Equal(document.TextRuns.Count, document.GetTextRuns(0).Count);
        Assert.All(document.TextRuns, run => Assert.Equal(0, run.PageIndex));
        Assert.Equal(LunchText, document.GetText(Start(document), End(document)));
    }

    [Fact]
    public async Task CharacterOffsets_CoverEveryIndex_AndNoBoundaryFallsInsideASurrogatePair()
    {
        var document = await LoadAsync(Lunch);

        foreach (var run in document.TextRuns)
        {
            Assert.Equal(run.Text.Length + 1, run.CharacterOffsets.Count);
            Assert.Equal(0, run.CharacterBoundaries[0]);
            Assert.Equal(run.Text.Length, run.CharacterBoundaries[^1]);

            for (var i = 1; i < run.CharacterOffsets.Count; i++)
                Assert.True(run.CharacterOffsets[i] >= run.CharacterOffsets[i - 1] - 1e-6, $"offsets go backwards at {i} in '{run.Text}'");

            foreach (var boundary in run.CharacterBoundaries.Where(b => b < run.Text.Length))
                Assert.False(char.IsLowSurrogate(run.Text[boundary]), "a boundary inside a surrogate pair");
        }

        var emoji = document.TextRuns.Single(run => run.Text.Contains("\U0001F600"));
        var at = emoji.Text.IndexOf("\U0001F600", StringComparison.Ordinal);
        Assert.Equal(emoji.CharacterOffsets[at], emoji.CharacterOffsets[at + 1]);
    }

    [Fact]
    public async Task HitTest_AtEachCharacterCentre_ReturnsThatCharacter()
    {
        var document = await LoadAsync(Lunch);

        for (var runIndex = 0; runIndex < document.TextRuns.Count; runIndex++)
        {
            var run = document.TextRuns[runIndex];
            var y = run.Baseline - (run.Ascent / 2);

            for (var k = 0; k + 1 < run.CharacterBoundaries.Count; k++)
            {
                var start = run.CharacterBoundaries[k];
                var centre = (run.CharacterOffsets[start] + run.CharacterOffsets[run.CharacterBoundaries[k + 1]]) / 2;
                if (run.CharacterOffsets[run.CharacterBoundaries[k + 1]] - run.CharacterOffsets[start] < 0.01)
                    continue;

                Assert.Equal(new TextPosition(runIndex, start), document.HitTest(0, centre, y));
            }
        }
    }

    [Fact]
    public async Task HitTest_InAGapOrMargin_ClampsToTheNearestLine()
    {
        var document = await LoadAsync(Lunch);
        var first = document.TextRuns[0];

        Assert.Equal(0, document.HitTest(0, 0, 0)!.Value.RunIndex);
        Assert.Equal(0, document.HitTest(0, -50, first.Baseline)!.Value.CharIndex);
        Assert.Equal(document.TextRuns[^1].Baseline, document.TextRuns[document.HitTest(0, 100, 800)!.Value.RunIndex].Baseline);

        // Beyond the end of a line the cursor sits after its last character.
        var last = document.GetCaretPosition(0, 5000, first.Baseline)!.Value;
        Assert.Equal(first.Text.Length, last.CharIndex);
    }

    [Fact]
    public async Task ARunInsideALink_CarriesItsAddress()
    {
        var document = await LoadAsync(Lunch);

        var linked = document.TextRuns.Where(run => run.LinkUri is not null).ToArray();
        Assert.NotEmpty(linked);
        Assert.All(linked, run => Assert.EndsWith("x", run.LinkUri));
        Assert.Equal("the menu", string.Concat(linked.Select(run => run.Text.Trim())).Replace("themenu", "the menu"));
        Assert.Contains(document.TextRuns, run => run.LinkUri is null);
    }

    [Fact]
    public async Task SelectionRects_CoverEachLine_AsTallAsItsFont()
    {
        var document = await LoadAsync(Lunch);
        var rects = document.GetSelectionRects(Start(document), End(document));

        Assert.Equal(document.TextRuns.Select(run => run.Baseline).Distinct().Count(), rects.Count);

        foreach (var run in document.TextRuns)
        {
            var covering = rects.Single(rect => run.Baseline > rect.Top && run.Baseline < rect.Bottom);
            Assert.True(covering.Left <= run.Bounds.Left + 0.5 && covering.Right >= run.Bounds.Right - 0.5, "the rectangle does not span the line");
            Assert.True(covering.Height >= run.Ascent + run.Descent - 0.01);
        }

        // Part of a line: the rectangle runs from one character edge to another.
        var firstRun = document.TextRuns[0];
        var partial = Assert.Single(document.GetSelectionRects(new TextPosition(0, 2), new TextPosition(0, 5)));
        Assert.Equal(firstRun.CharacterOffsets[2], partial.Left, 3);
        Assert.Equal(firstRun.CharacterOffsets[5], partial.Right, 3);

        Assert.Empty(document.GetSelectionRects(new TextPosition(0, 3), new TextPosition(0, 3)));
    }

    [Fact]
    public async Task GetText_RoundTripsAcrossBlockquotePreAndParagraphs()
    {
        var document = await LoadAsync(
            "<blockquote>Quoted words</blockquote><pre style='white-space:pre-wrap'>line one\nline two</pre><p>After</p><ul><li>First</li><li>Second</li></ul>",
            new HtmlDocumentOptions { RecordTextRuns = true, PageWidthPoints = 400 });

        Assert.Equal(6, document.TextRuns.Count);

        Assert.Equal("Quoted words\nline one\nline two\nAfter\nFirst\nSecond", document.GetText(Start(document), End(document)));

        // A selection that starts and ends partway through keeps the same rules.
        var from = document.HitTest(0, document.TextRuns[1].CharacterOffsets[5], document.TextRuns[1].Baseline)!.Value;
        var to = document.GetCaretPosition(0, document.TextRuns[3].CharacterOffsets[2], document.TextRuns[3].Baseline)!.Value;
        Assert.Equal("one\nline two\nAf", document.GetText(from, to));
        Assert.Equal(document.GetText(from, to), document.GetText(to, from));
    }

    [Fact]
    public async Task APreLine_IsABlockMemberAfterAForcedBreak_NotAWrap()
    {
        var document = await LoadAsync("<p>one<br>two</p>", Narrow());

        Assert.Equal("one\ntwo", document.GetText(Start(document), End(document)));
        Assert.Single(document.TextRuns.Select(run => run.BlockId).Distinct());
    }

    [Fact]
    public async Task TextRuns_NeedTheOption()
    {
        var document = await LoadAsync(Lunch, Narrow(record: false));

        Assert.Throws<InvalidOperationException>(() => document.TextRuns);
        Assert.Throws<InvalidOperationException>(() => document.HitTest(0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LoadAsync(Lunch).Result.GetTextRuns(5));
    }

    [Fact]
    public async Task RecordingTextRuns_DoesNotChangeTheLayout()
    {
        var (_, off) = await TestDocument.LoadAsync(TestDocument.Wrap(Lunch + Lunch + "<p id=after>Z</p>"), Narrow(record: false));
        var (_, on) = await TestDocument.LoadAsync(TestDocument.Wrap(Lunch + Lunch + "<p id=after>Z</p>"), Narrow());
        _ = on.TextRuns;

        Assert.Equal(off.Pages.Count, on.Pages.Count);
        Assert.Equal(off.ContentExtent, on.ContentExtent);
        Assert.Equal(off.GetElementBounds("after"), on.GetElementBounds("after"));
    }

    [Fact]
    public async Task ARightToLeftRun_ReportsItsDirection_AndItsOffsetsRunBackwards()
    {
        const string hebrew = "שלום עולם";
        var document = await LoadAsync($"<p dir='rtl'>{hebrew}</p>");

        var run = Assert.Single(document.TextRuns);
        Assert.True(run.IsRtl);
        Assert.Equal(hebrew, run.Text);
        Assert.True(run.CharacterOffsets[0] > run.CharacterOffsets[^1], string.Join(", ", run.CharacterOffsets));

        // The first character of the logical text is at the right.
        var y = run.Baseline - (run.Ascent / 2);
        Assert.Equal(0, document.HitTest(0, run.CharacterOffsets[0] - 0.5, y)!.Value.CharIndex);
        Assert.Equal(hebrew.Length - 1, document.HitTest(0, run.CharacterOffsets[^1] + 0.5, y)!.Value.CharIndex);
        Assert.Equal(hebrew.Length, document.GetCaretPosition(0, run.CharacterOffsets[^1] - 5, y)!.Value.CharIndex);
    }

    [Fact]
    public async Task LatinText_InARightToLeftParagraph_StaysInOrder_AtTheRightEdge()
    {
        var document = await LoadAsync("<p dir='rtl'>abc def</p>");

        var run = Assert.Single(document.TextRuns);
        Assert.True(run.IsRtl);
        Assert.Equal("abc def", run.Text);
        Assert.Equal(120, run.Bounds.Right, 1);
        Assert.True(run.CharacterOffsets[0] < run.CharacterOffsets[^1]);
    }

    [Fact]
    public async Task FindText_WordAndBlock_AreRanges_OfTheSameText()
    {
        var document = await LoadAsync(Lunch + "<p>Lunch again</p>");

        var matches = document.FindText("lunch", ignoreCase: true);
        Assert.Equal(2, matches.Count);
        Assert.All(matches, match => Assert.Equal("Lunch", document.GetText(match.Start, match.End)));
        Assert.Empty(document.FindText("lunch"));
        Assert.Empty(document.FindText(""));

        // A match that wraps across two lines reads as it would be copied.
        var wrapped = Assert.Single(document.FindText("12? See"));
        Assert.Equal("12? See", document.GetText(wrapped.Start, wrapped.End));

        var inside = new TextPosition(0, 1);
        var word = document.GetWordAt(inside)!.Value;
        Assert.Equal("Lunch", document.GetText(word.Start, word.End));

        var block = document.GetBlockAt(inside)!.Value;
        Assert.Equal(LunchText, document.GetText(block.Start, block.End));
        Assert.Null(document.GetBlockAt(new TextPosition(99, 0)));
        Assert.Null(document.GetWordAt(new TextPosition(-1, 0)));
    }

    [Fact]
    public async Task ASecondPage_ReportsItsOwnRuns_InPageCoordinates()
    {
        var spacer = string.Concat(Enumerable.Repeat("<div style='height:300pt; background:#ccc'></div>", 4));
        var document = await LoadAsync(spacer + "<p>Late text</p>");

        Assert.True(document.Pages.Count >= 2);
        var runs = document.GetTextRuns(document.Pages.Count - 1);
        var run = Assert.Single(runs);
        Assert.Equal("Late text", run.Text);
        Assert.InRange(run.Baseline, 0, document.Pages[^1].HeightPoints);
        Assert.Equal(new TextPosition(document.TextRuns.Count - 1, 0), document.HitTest(document.Pages.Count - 1, run.Bounds.Left + 1, run.Baseline));
    }
}
