using System.Globalization;
using PeachPDF.Adapters;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A card is an auto-height scroll container whose first child has a top margin, the shape of a titled panel.
/// When the card starts within that margin of the page foot, its first page holds nothing and layout goes back
/// to the slot the pass has just filled.
/// </summary>
/// <remarks>
/// That pass then ended with the card's own boxes still on its break chain, and concluding from it that
/// <c>html</c> and <c>body</c> had "emitted nothing from here on" pruned every page after it: the whole card
/// was drawn on no page. The fixtures paint through a recording <c>Canvas</c>, because the words were present in
/// layout and gone from the pages.
/// </remarks>
public class CardAtThePageFootIntegrationTests
{
    [Theory]
    [InlineData("hidden", 131)]
    [InlineData("hidden", 138.5)]
    [InlineData("hidden", 146)]
    [InlineData("hidden", 154.5)]
    [InlineData("hidden", 159)]
    [InlineData("auto", 131)]
    [InlineData("auto", 138.5)]
    [InlineData("auto", 146)]
    [InlineData("auto", 154.5)]
    [InlineData("auto", 159)]
    public async Task CardWhoseFirstChildsMarginReachesPastThePageFoot_DrawsEveryWordOnce(string overflow, double spacerHeight)
    {
        var lines = Enumerable.Range(1, 20).Select(i => $"Line{i}").ToArray();
        var html = $$"""
            <html><head><style>
            @page { size: 300pt 200pt; margin: 20pt; }
            body { margin: 0; font: 10pt/12pt sans-serif; }
            p { margin: 0 0 4pt; }
            </style></head><body>
            <div style="height:{{spacerHeight.ToString(CultureInfo.InvariantCulture)}}pt">Before</div>
            <div style="overflow:{{overflow}}"><p style="margin-top:30pt">Title</p>{{string.Concat(lines.Select(t => $"<p>{t}</p>"))}}</div>
            <p>After</p>
            </body></html>
            """;

        var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
            html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);

        List<string> painted = [];
        for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
        {
            using var graphics = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(container, graphics, page);
            painted.AddRange(graphics.Log.Where(op => op.Kind == PaintOpKind.DrawString).Select(op => op.Text!));
        }

        Assert.Equal(["Before", "Title", .. lines, "After"], painted);
    }
}
