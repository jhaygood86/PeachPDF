using PeachPDF.Tests.TestSupport;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A float taller than the page continues on the next page, and the text that flows beside it carries on
/// beside the continuation instead of being dropped after the first page.
/// </summary>
/// <remarks>
/// 200pt pages with 20pt margins leave a 160pt band, which holds eight of the 20pt lines below - fixed line
/// heights, one short word per line, so nothing here depends on a font's metrics.
/// </remarks>
public class FloatBesideTextPaginationTests
{
    private const double PageWidth = 300;
    private const double PageHeight = 200;
    private const double Margin = 20;

    private static string Lines(string prefix, int count) =>
        string.Concat(Enumerable.Range(1, count).Select(i => $"<div class='l'>{prefix}{i}</div>"));

    private static string Document(string floatStyle, int floatLines, int textLines, int afterLines = 0) =>
        Wrap("<style>body { margin:0; font:10pt/20pt sans-serif } .l { height:20pt }</style>" +
             $"<div style='{floatStyle}'>{Lines("F", floatLines)}</div>" +
             $"<div>{Lines("T", textLines)}</div>" +
             (afterLines > 0 ? $"<div style='clear:both'>{Lines("A", afterLines)}</div>" : ""));

    private static async Task<(List<(int Page, string Text, double X, double Y)> Words, int Pages)> PaintAsync(string html, double pageHeight = PageHeight)
    {
        var (_, container) = await LayoutAsync(html, PageWidth, pageHeight, Margin,
            configureAdapter: BundledFonts.PinSansSerifAsync);
        var pages = container.FragmentTree!.Fragmentainers.Count;
        List<(int, string, double, double)> words = [];

        for (var page = 0; page < pages; page++)
        {
            var graphics = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, graphics, page);
            words.AddRange(graphics.DrawStringCalls.Select(w => (page, w.Text.Trim(), w.PaintPoint.X, w.PaintPoint.Y)));
        }

        return (words, pages);
    }

    private static IEnumerable<string> Expected(string prefix, int count) =>
        Enumerable.Range(1, count).Select(i => $"{prefix}{i}");

    [Theory]
    [InlineData("float:left;width:100pt")]
    [InlineData("float:right;width:100pt")]
    [InlineData("float:left;width:100pt;margin:6pt")]
    public async Task TextBesideAFloatTallerThanThePage_IsKeptOnEveryPage(string floatStyle)
    {
        var (words, pages) = await PaintAsync(Document(floatStyle, floatLines: 20, textLines: 12));

        Assert.Equal(3, pages);
        Assert.Equal(Expected("F", 20), words.Where(w => w.Text.StartsWith('F')).Select(w => w.Text));
        var text = words.Where(w => w.Text.StartsWith('T')).ToList();
        Assert.Equal(Expected("T", 12), text.Select(w => w.Text));
        Assert.True(text.Select(w => w.Page).Distinct().Count() > 1, "the text was expected to span pages");
    }

    // The text starts to the right of the float on every page the float reaches.
    [Fact]
    public async Task TextBesideALeftFloat_NeverStartsUnderIt()
    {
        var (words, _) = await PaintAsync(Document("float:left;width:100pt", floatLines: 20, textLines: 12));

        var floatRight = Margin + 100;
        foreach (var word in words.Where(w => w.Text.StartsWith('T')))
        {
            Assert.True(word.X >= floatRight - 0.5, $"{word.Text} on page {word.Page} starts at {word.X}, under the float");
        }
    }

    // A block that clears a float which ran across pages starts below the float's last line, at the left edge:
    // the first line of a cleared block used to be drawn beside the float on the float's last page instead. The
    // float is 9, 12 and 20 lines so that it ends with room on its last page, with little, and on a third page.
    [Theory]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(20)]
    public async Task ContentClearedPastATallFloat_FollowsItsLastLine(int floatLines)
    {
        var (words, _) = await PaintAsync(Document("float:left;width:100pt", floatLines, textLines: 0, afterLines: 3));

        var lastFloatLine = words.Single(w => w.Text == $"F{floatLines}");
        var cleared = Expected("A", 3).Select(a => words.Single(w => w.Text == a)).ToList();

        Assert.All(cleared, w => Assert.Equal(Margin, w.X, 0.5));
        Assert.Equal(lastFloatLine.Page, cleared[0].Page);
        Assert.Equal(lastFloatLine.Y + 20, cleared[0].Y, 0.5);
        Assert.Equal(cleared[0].Y + 20, cleared[1].Y, 0.5);
        Assert.Equal(cleared[1].Y + 20, cleared[2].Y, 0.5);
    }

    [Fact]
    public async Task InlineContentClearedPastATallFloat_FollowsItsLastLine()
    {
        var (words, _) = await PaintAsync(Wrap("<style>body { margin:0; font:10pt/20pt sans-serif } .l { height:20pt }</style>" +
                                               $"<div style='float:left;width:100pt'>{Lines("F", 20)}</div><div style='clear:left'>a1 a2 a3</div>"));

        var lastFloatLine = words.Single(w => w.Text == "F20");
        var cleared = new[] { "a1", "a2", "a3" }.Select(a => words.Single(w => w.Text == a)).ToList();

        Assert.All(cleared, w => Assert.Equal(lastFloatLine.Page, w.Page));
        Assert.All(cleared, w => Assert.Equal(lastFloatLine.Y + 20, w.Y, 0.5));
        Assert.Equal(Margin, cleared[0].X, 0.5);
    }

    // A break-inside: avoid box holding a float that runs over several pages cannot be moved whole to the next
    // page: what its float placed on the earlier pages would be lost. The avoid is relaxed and every word stays.
    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public async Task AvoidBoxHoldingATallFloat_KeepsEveryWord(int linesBefore)
    {
        var (words, _) = await PaintAsync(Wrap("<style>body { margin:0; font:10pt/20pt sans-serif } .l { height:20pt }</style>" +
                                               $"<div>{Lines("B", linesBefore)}</div>" +
                                               "<div style='break-inside:avoid'>" +
                                               $"<div style='float:left;width:100pt'>{Lines("F", 20)}</div>" +
                                               $"<div>{Lines("T", 6)}</div></div>"));

        Assert.Equal(Expected("F", 20), words.Where(w => w.Text.StartsWith('F')).Select(w => w.Text));
        Assert.Equal(Expected("T", 6), words.Where(w => w.Text.StartsWith('T')).Select(w => w.Text));
        Assert.Equal(Expected("B", linesBefore), words.Where(w => w.Text.StartsWith('B')).Select(w => w.Text));
    }

    // The common shape: a sidebar of text floated left and an article flowing beside it, both longer than a page.
    [Fact]
    public async Task FloatedTextSidebarBesideAnArticle_KeepsEveryWord()
    {
        static IEnumerable<string> Tokens(string prefix, int paragraphs, int perParagraph) =>
            Enumerable.Range(1, paragraphs).SelectMany(p => Enumerable.Range(1, perParagraph).Select(i => $"{prefix}{p}_{i}"));

        static string Paragraphs(string prefix, int paragraphs, int perParagraph) =>
            string.Concat(Enumerable.Range(1, paragraphs).Select(p =>
                $"<p>{string.Join(' ', Enumerable.Range(1, perParagraph).Select(i => $"{prefix}{p}_{i}"))}</p>"));

        var (words, pages) = await PaintAsync(Wrap("<style>body { margin:0; font:10pt/12pt sans-serif } p { margin:0 0 6pt }</style>" +
                                                   $"<div style='float:left;width:110pt;margin-right:10pt;padding:4pt'>{Paragraphs("s", 8, 14)}</div>" +
                                                   Paragraphs("a", 5, 70)));

        var expected = Tokens("s", 8, 14).Concat(Tokens("a", 5, 70)).Order().ToList();

        Assert.True(pages > 2, "the sidebar was expected to run over several pages");
        Assert.Equal(expected, words.Select(w => w.Text).Order().ToList());
    }

    // The shape that reached the avoid mover in generated documents: lines of text ahead of a break-inside: avoid
    // box that holds a float and a paragraph, neither fitting the room left on the first page. Moving the box whole
    // to the next page dropped what its float had already placed on the earlier pages.
    [Fact]
    public async Task AvoidBoxHoldingAFloatAndAParagraph_KeepsEveryWord()
    {
        static string Run(string prefix, int count) => string.Join(' ', Enumerable.Range(1, count).Select(i => $"{prefix}{i}"));

        var (words, _) = await PaintAsync(
            Wrap("<style>body { margin:0; font:10pt/12pt sans-serif } p { margin:0 0 4pt }</style>" +
                 "<div>b1</div><div>b2</div>" +
                 "<div style='break-inside:avoid'>" +
                 $"<div style='float:left;width:130pt;margin:4pt'><p>{Run("f", 80)}</p></div>" +
                 $"<p>{Run("p", 60)}</p></div>"),
            pageHeight: 160);

        var expected = new[] { "b1", "b2" }
            .Concat(Enumerable.Range(1, 80).Select(i => $"f{i}"))
            .Concat(Enumerable.Range(1, 60).Select(i => $"p{i}"));
        Assert.Equal(expected.Order().ToList(), words.Select(w => w.Text).Order().ToList());
    }

    // Floats stacked down both sides, one inside a list item, on a page 190pt high. A sibling that follows a
    // float that ran over pages is first placed beside the float's top, on a page already emitted; without
    // reopening that page the last line at the foot of a later page is missing.
    [Fact]
    public async Task FloatsOnBothSidesAroundAList_KeepEveryWord()
    {
        static string Run(string prefix, int count) => string.Join(' ', Enumerable.Range(1, count).Select(i => $"{prefix}{i}"));

        var (words, _) = await PaintAsync(
            Wrap("<style>body { margin:0; font:10pt/12pt sans-serif } p { margin:0 0 4pt }</style>" +
                 "<div style='float:left;width:94pt'><div style='height:164pt;background:#ddd'></div></div>" +
                 $"<div style='float:right;width:61pt;margin:8pt'><p>{Run("a", 12)}</p></div>" +
                 $"<p>{Run("b", 38)}</p>" +
                 $"<p style='clear:both'>{Run("c", 15)}</p>" +
                 $"<ul><li><div style='float:right;width:69pt;margin:8pt'><p>{Run("d", 37)}</p></div><p>{Run("e", 15)}</p></li>" +
                 $"<li><p>{Run("f", 5)}</p></li></ul>" +
                 $"<div style='float:left;width:94pt;margin:8pt'><p>{Run("g", 11)}</p></div><p>{Run("h", 29)}</p>"),
            pageHeight: 190);

        var expected = new (string Prefix, int Count)[] { ("a", 12), ("b", 38), ("c", 15), ("d", 37), ("e", 15), ("f", 5), ("g", 11), ("h", 29) }
            .SelectMany(t => Enumerable.Range(1, t.Count).Select(i => $"{t.Prefix}{i}"));
        Assert.Equal(expected.Order().ToList(), words.Select(w => w.Text).Order().ToList());
    }

    [Fact]
    public async Task FloatThatFitsThePage_IsUnaffected()
    {
        var (words, pages) = await PaintAsync(Document("float:left;width:100pt", floatLines: 3, textLines: 12));

        Assert.Equal(2, pages);
        Assert.Equal(Expected("F", 3), words.Where(w => w.Text.StartsWith('F')).Select(w => w.Text));
        Assert.Equal(Expected("T", 12), words.Where(w => w.Text.StartsWith('T')).Select(w => w.Text));
    }
}
