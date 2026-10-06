using PeachPDF.Adapters;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

public class HiddenOverflowPaginationTests
{
    [Theory]
    [InlineData(155, false)]
    [InlineData(160, false)]
    [InlineData(165, false)]
    [InlineData(155, true)]
    [InlineData(160, true)]
    [InlineData(165, true)]
    public async Task AutoHeightHiddenOverflow_PaginatesWholeLinesInsideThePage(double pageHeight, bool inlineLines)
    {
        var labels = Enumerable.Range(0, 18).Select(i => $"Line{i}").ToArray();
        var paragraphs = inlineLines
            ? "<p>" + string.Join("<br>", labels) + "</p>"
            : string.Concat(labels.Select(t => $"<p>{t}</p>"));
        var html = $$"""
            <html><head><style>
            @page { size: 300pt {{pageHeight}}pt; margin: 20pt; }
            body { margin: 0; font: 10pt/17pt sans-serif; }
            p { margin: 0; orphans: 1; widows: 1; }
            </style></head><body>
            <div id="container" style="overflow:hidden;position:relative;padding:7pt">
            {{paragraphs}}
            </div></body></html>
            """;
        var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
            html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);

        Assert.True(container.FragmentTree!.Fragmentainers.Count > 1);
        foreach (var p in LayoutHarness.Descendants(root).Where(b => b.HtmlTag?.Name == "p"))
        {
            foreach (var line in p.LineBoxes)
            {
                var word = Assert.Single(line.Words, w => !w.IsLineBreak && !w.IsSpaces);
                var page = container.PageIndexOf(word.Top);
                Assert.True(word.Bottom <= container.PageBottomOf(page) + 0.001,
                    $"{word.Text} straddles the bottom of page {page}");
            }
        }

        List<string> painted = [];
        for (var page = 0; page < container.FragmentTree.Fragmentainers.Count; page++)
        {
            using var graphics = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(container, graphics, page);
            painted.AddRange(graphics.Log.Where(op => op.Kind == PaintOpKind.DrawString).Select(op => op.Text!));
        }
        Assert.Equal(labels, painted);
        Assert.Empty(container.ClipReport.Words);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("height:auto", false)]
    [InlineData("min-height:50pt", false)]
    [InlineData("height:50%", false)]
    [InlineData("height:50pt", true)]
    [InlineData("height:50pt;max-height:60pt", true)]
    [InlineData("max-height:60pt", false)]
    [InlineData("writing-mode:vertical-rl;height:50pt", false)]
    [InlineData("writing-mode:vertical-rl;width:50pt", true)]
    [InlineData("writing-mode:vertical-rl;width:50pt;max-width:60pt", true)]
    public async Task HiddenOverflow_UsesLogicalHeightForFragmentation(string sizing, bool monolithic)
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
            $"<div><div id='box' style='overflow:hidden;{sizing}'>Text</div></div>"));
        var box = LayoutHarness.FindById(root, "box")!;

        Assert.True(MonolithicContent.IsScrollContainer(box));
        Assert.Equal(monolithic, MonolithicContent.IsMonolithic(box));
    }

    [Fact]
    public async Task HiddenOverflow_WithADefinitePercentageHeight_StaysMonolithic()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
            "<div style='height:100pt'><div id='box' style='overflow:hidden;height:50%'>Text</div></div>"));

        Assert.True(MonolithicContent.IsMonolithic(LayoutHarness.FindById(root, "box")!));
    }

    [Fact]
    public async Task AutoHeightHiddenOverflow_HonorsAForcedBreakInsideIt()
    {
        var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync("""
            <html><head><style>
            @page { size: 300pt 160pt; margin: 20pt; }
            body { margin: 0; font: 10pt/17pt sans-serif; }
            p { margin: 0; }
            </style></head><body><div style="overflow:hidden">
            <p>Before</p><p id="after" style="break-before:page">After</p>
            </div></body></html>
            """, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);

        Assert.Equal(2, container.FragmentTree!.Fragmentainers.Count);
        Assert.Equal(container.PageTopOf(1), LayoutHarness.FindById(root, "after")!.Location.Y, 6);
    }
}
