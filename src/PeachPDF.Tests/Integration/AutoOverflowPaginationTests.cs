using PeachPDF.Adapters;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

public class AutoOverflowPaginationTests
{
    [Fact]
    public async Task AutoHeightCodeListing_StartsBelowItsHeadingAndContinuesAcrossPages()
    {
        var labels = Enumerable.Range(1, 34).Select(i => $"Line{i}").ToArray();
        var html = $$"""
            <html><head><style>
            @page { size:105mm 148mm; margin:12mm 10mm; }
            body { margin:0; font:8.5pt/1.35 sans-serif; }
            h2 { font-size:10pt; margin:10pt 0 4pt; }
            pre { overflow:auto; border:0.75pt solid #999; padding:6pt; font-family:sans-serif;
                  font-size:7.5pt; line-height:1.3; margin:0; }
            </style></head><body>
            <div style="height:70pt"></div>
            <h2 id="heading">A code listing with overflow: auto</h2>
            <pre id="listing">{{string.Join("\n", labels)}}</pre>
            </body></html>
            """;
        var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
            html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);
        var heading = LayoutHarness.FindById(root, "heading")!;
        var listing = LayoutHarness.FindById(root, "listing")!;

        Assert.Equal(0, container.PageIndexOf(listing.Location.Y));
        Assert.InRange(listing.Location.Y - heading.ActualBottom, 0, 5);
        Assert.True(container.FragmentTree!.Fragmentainers.Count > 1);

        List<string> painted = [];
        for (var page = 0; page < container.FragmentTree.Fragmentainers.Count; page++)
        {
            using var graphics = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(container, graphics, page);
            painted.AddRange(graphics.Log.Where(op => op.Kind == PaintOpKind.DrawString
                && op.Text!.StartsWith("Line", StringComparison.Ordinal)).Select(op => op.Text!));
            if (page == 0)
            {
                Assert.Contains("Line1", painted);
                Assert.InRange(painted.Count, 1, labels.Length - 1);
            }
        }
        Assert.Equal(labels, painted);
        Assert.Empty(container.ClipReport.Words);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("min-height:50pt", false)]
    [InlineData("height:50%", false)]
    [InlineData("max-height:50%", false)]
    [InlineData("height:50pt", true)]
    [InlineData("max-height:50pt", false)]
    [InlineData("height:50pt;max-height:60pt", true)]
    [InlineData("writing-mode:vertical-rl;width:50pt", true)]
    [InlineData("writing-mode:vertical-rl;max-width:50pt", true)]
    [InlineData("writing-mode:vertical-rl;height:50pt", false)]
    public async Task AutoOverflow_IsMonolithicOnlyWithADefiniteLogicalHeight(string sizing, bool expected)
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
            $"<div><div id='box' style='overflow:auto;{sizing}'>Text</div></div>"));

        Assert.Equal(expected, MonolithicContent.IsMonolithic(LayoutHarness.FindById(root, "box")!));
    }

    [Fact]
    public async Task AutoOverflow_WithADefinitePercentageMaximum_BreaksUnderItsCap()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
            "<div style='height:100pt'><div id='box' style='overflow:auto;max-height:50%'>Text</div></div>"));

        Assert.False(MonolithicContent.IsMonolithic(LayoutHarness.FindById(root, "box")!));
    }

    [Theory]
    [InlineData("<table><tr><td>Text</td></tr></table>")]
    [InlineData("<div style='display:flex'>Text</div>")]
    [InlineData("<div><div style='display:grid'>Text</div></div>")]
    public async Task CappedAutoOverflow_HoldingAnEngineOfItsOwn_StaysMonolithic(string content)
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
            $"<div><div id='box' style='overflow:auto;max-height:50pt'>{content}</div></div>"));

        Assert.True(MonolithicContent.IsMonolithic(LayoutHarness.FindById(root, "box")!));
    }
}
