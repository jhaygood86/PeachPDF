using PeachDrawing.Core;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A table continued inside a balanced multi-column fragment keeps every word whatever the resolved font's natural
/// height is against the used <c>line-height</c> (the fonts here differ materially: Liberation Sans is 1.15em tall,
/// Source Sans 3 a different height), with bundled fonts so no host fallback is involved.
/// </summary>
public class MulticolTableFontMetricTests
{
    private static Task RegisterSourceSans(PeachPDF.Adapters.PdfSharpAdapter adapter) =>
        BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, "Metric Fixture");

    [Theory]
    [InlineData("PinnedSans", "12pt", 0)]
    [InlineData("PinnedSans", "7pt", 13)]
    [InlineData("PinnedSans", "20pt", 27)]
    [InlineData("Metric Fixture", "12pt", 0)]
    [InlineData("Metric Fixture", "7pt", 13)]
    [InlineData("Metric Fixture", "20pt", 27)]
    [InlineData("Metric Fixture", "9pt", 41)]
    public async Task TableInBalancedColumns_KeepsEveryWordOnce(string family, string lineHeight, int offset)
    {
        var words = string.Join(" ", Enumerable.Range(0, 120).Select(i => $"w1_{i}"));
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                   $"body {{ margin:0; font: 10pt/{lineHeight} '{family}' }} td {{ vertical-align: top }}</style></head><body>" +
                   $"<div style='height:{offset}pt'></div><div style='columns:2;column-gap:8pt'>" +
                   $"<table border='1'><tr><td>{words}</td><td>w2_1 w2_2</td></tr></table></div></body></html>";

        var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
            html, new PdfGenerateConfig(), async adapter =>
            {
                await BundledFonts.PinSansSerifAsync(adapter);
                await RegisterSourceSans(adapter);
            });

        // Ownership, not the word rectangle: with a line-height below the font's natural height the rectangle of
        // a line's first word pokes past the band edge by half the negative leading, and is still that line's.
        List<string> emitted = [];
        List<string> outside = [];

        foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
        {
            Collect(fragmentainer.Root, fragmentainer.Rect);
        }

        void Collect(BoxFragment fragment, Rect band)
        {
            foreach (var word in fragment.Words)
            {
                var text = word.Word.Text ?? string.Empty;
                if (!text.StartsWith('w')) continue;
                emitted.Add(text);
                var centre = (word.Rect.Top + word.Rect.Bottom) / 2;
                if (centre < band.Top || centre > band.Bottom) outside.Add(text);
            }

            foreach (var child in fragment.Children) Collect(child, band);
        }

        var (lost, doubled) = PaintedWords.Diff(html, emitted.Order().ToList());

        Assert.Empty(lost);
        Assert.Empty(doubled);
        Assert.Empty(outside);
    }
}
