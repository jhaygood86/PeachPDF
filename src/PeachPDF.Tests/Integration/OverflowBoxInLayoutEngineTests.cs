using PeachPDF.Adapters;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// An auto-height scroll container in ordinary block flow breaks between its lines. One that is a flex or grid
/// item, or that sits inside a multi-column container, is laid out by an engine that sizes or clips it per
/// container or column, and letting it break there dropped whole lines at the fragmentainer edge, so it keeps
/// the unbreakable treatment. So does one that holds an absolutely positioned box, which was drawn off the
/// page once its container could break.
/// </summary>
public class OverflowBoxInLayoutEngineTests
{
    private const string Page = "@page { size:300pt 200pt; margin:20pt } body { margin:0; font:10pt/12pt sans-serif } p { margin:0 0 4pt }";

    private static string Words(int from, int count) =>
        string.Join(' ', Enumerable.Range(from, count).Select(i => $"w{i:000}"));

    private static string Paragraphs(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(i => $"<p>{Words(1 + 8 * i, 8)}</p>"));

    private static string Document(string body) =>
        $"<!DOCTYPE html><html><head><style>{Page}</style></head><body>{body}</body></html>";

    // Words the page clip cuts away are still drawn, so the clip report is part of "every word is visible".
    private static async Task<(List<string> Painted, int Clipped)> PaintedWordsAsync(string html)
    {
        var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
            html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);
        List<string> painted = [];
        for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
        {
            using var graphics = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(container, graphics, page);
            painted.AddRange(graphics.DrawnStrings.Select(s => s.Text));
        }
        return (painted, container.ClipReport.Words.Count);
    }

    // A lead paragraph that leaves the item straddling the page edge: every word has to be drawn.
    [Theory]
    [InlineData("flex", "hidden")]
    [InlineData("flex", "auto")]
    [InlineData("flex", "scroll")]
    [InlineData("grid", "hidden")]
    [InlineData("grid", "auto")]
    [InlineData("grid", "scroll")]
    public async Task AutoHeightScrollItemStraddlingAPageEdge_DrawsEveryWord(string display, string overflow)
    {
        var html = Document(
            $"<p>{Words(900, 88)}</p><div style='display:{display}'><div style='overflow:{overflow}'>{Paragraphs(4)}</div></div>");

        var (painted, clipped) = await PaintedWordsAsync(html);

        Assert.Equal(0, clipped);
        Assert.Equal(Enumerable.Range(900, 88).Concat(Enumerable.Range(1, 32)).Select(i => $"w{i:000}").Order(),
            painted.Order());
    }

    // Sized to fit a column: a box taller than the page is sliced whichever way it is classified.
    [Theory]
    [InlineData("<div style='columns:2;column-gap:10pt'><div style='overflow:hidden'>{0}</div></div>", 3)]
    [InlineData("<div style='columns:2;column-gap:10pt'><div style='overflow:hidden'>{0}</div></div>", 5)]
    [InlineData("<div style='columns:2;column-gap:10pt'><div><div style='overflow:hidden'>{0}</div></div></div>", 5)]
    public async Task AutoHeightHiddenBoxInsideColumns_DrawsEveryWord(string template, int paragraphs)
    {
        var (painted, clipped) = await PaintedWordsAsync(Document(string.Format(template, Paragraphs(paragraphs))));

        Assert.Equal(0, clipped);
        Assert.Equal(Enumerable.Range(1, 8 * paragraphs).Select(i => $"w{i:000}").Order(), painted.Order());
    }

    // The absolute box sits below the first lines of its container, which straddles the page edge.
    [Fact]
    public async Task PositionedScrollContainerHoldingAnAbsoluteBox_DrawsEveryWord()
    {
        var html = Document(
            $"<p>{Words(900, 60)}</p><div style='position:relative;overflow:hidden'>{Paragraphs(6)}" +
            $"<div style='position:absolute;top:30pt;right:0;width:80pt'>{Words(700, 6)}</div></div>");

        var (painted, clipped) = await PaintedWordsAsync(html);

        Assert.Equal(0, clipped);
        Assert.Equal(
            Enumerable.Range(900, 60).Concat(Enumerable.Range(1, 48)).Concat(Enumerable.Range(700, 6))
                .Select(i => $"w{i:000}").Order(),
            painted.Order());
    }

    // The wrapper has no position of its own: the absolute box's containing block is above it.
    [Fact]
    public async Task StaticScrollContainerHoldingAnAbsoluteBox_DrawsEveryWord()
    {
        var html = Document(
            $"<p>{Words(900, 60)}</p><div style='overflow:hidden'>{Paragraphs(6)}" +
            $"<div style='position:absolute;top:30pt;right:0;width:80pt'>{Words(700, 6)}</div></div>");

        var (painted, clipped) = await PaintedWordsAsync(html);

        Assert.Equal(0, clipped);
        Assert.Equal(
            Enumerable.Range(900, 60).Concat(Enumerable.Range(1, 48)).Concat(Enumerable.Range(700, 6))
                .Select(i => $"w{i:000}").Order(),
            painted.Order());
    }

    // A positioned wrapper with nothing absolute inside is the ordinary case and has to break between lines
    // rather than slice one at the page edge.
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    public async Task PositionedScrollContainerWithoutAnAbsoluteBox_BreaksBetweenLines(int leadWords)
    {
        var html = Document(
            $"<p>{Words(900, leadWords)}</p><div style='position:relative;overflow:hidden'>{Paragraphs(6)}</div>");

        var (painted, clipped) = await PaintedWordsAsync(html);

        Assert.Equal(0, clipped);
        Assert.Equal(
            Enumerable.Range(900, leadWords).Concat(Enumerable.Range(1, 48)).Select(i => $"w{i:000}").Order(),
            painted.Order());
    }

    [Theory]
    [InlineData("<div style='display:flex'><div id='box' style='overflow:hidden'>text</div></div>", true)]
    [InlineData("<div style='display:grid'><div id='box' style='overflow:auto'>text</div></div>", true)]
    [InlineData("<div style='display:inline-flex'><div id='box' style='overflow:scroll'>text</div></div>", true)]
    [InlineData("<div style='columns:2'><div id='box' style='overflow:hidden'>text</div></div>", true)]
    [InlineData("<div style='column-width:100pt'><div><div id='box' style='overflow:hidden'>text</div></div></div>", true)]
    [InlineData("<div id='box' style='position:relative;overflow:hidden'><div style='position:absolute'>x</div></div>", true)]
    [InlineData("<div id='box' style='position:relative;overflow:hidden'><div><div style='position:absolute'>x</div></div></div>", true)]
    [InlineData("<div id='box' style='position:relative;overflow:hidden'>text</div>", false)]
    [InlineData("<div id='box' style='overflow:hidden'><div style='position:absolute'>x</div></div>", true)]
    [InlineData("<div id='box' style='position:sticky;overflow:hidden'><div style='position:absolute'>x</div></div>", true)]
    [InlineData("<div id='box' style='position:relative;overflow:hidden'><div style='position:absolute;display:none'>x</div>text</div>", false)]
    [InlineData("<div id='box' style='overflow:hidden'><div style='position:relative'>text</div></div>", false)]
    [InlineData("<div><div id='box' style='overflow:hidden'>text</div></div>", false)]
    [InlineData("<div style='display:flex'><div><div id='box' style='overflow:hidden'>text</div></div></div>", false)]
    public async Task ScrollContainerInAnEngine_IsMonolithicOnlyThere(string body, bool monolithic)
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));

        Assert.Equal(monolithic, MonolithicContent.IsMonolithic(LayoutHarness.FindById(root, "box")!));
    }
}
