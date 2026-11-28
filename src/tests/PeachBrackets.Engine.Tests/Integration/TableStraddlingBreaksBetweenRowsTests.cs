using PeachPDF;
using PeachPDF.Rendering;
using PeachDrawing.PDF;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A table that starts partway down a page and does not fit breaks between its rows
/// (css-break-3 §4.1/§4.3, css-tables-3 §6.1); it is not moved whole to the next page.
/// </summary>
public class TableStraddlingBreaksBetweenRowsTests(ITestOutputHelper output)
{
    internal static string Html(int spacerPx) => $$"""
        <!DOCTYPE html><html><head><style>
          @page { size: A4; margin: 20mm; }
          th, td { border: 1px solid #4f81bd; padding: 6px; vertical-align: top; font: 13px Arial, sans-serif; text-align: left; }
          th { width: 30%; }
        </style></head><body>
          <div style="height: {{spacerPx}}px; background: #eee;">SPACER</div>
          <table style="width: 100%; border-collapse: collapse;">
            <tr><th>ROW-1</th><td>Short value</td></tr>
            <tr><th>ROW-2</th><td>Short value</td></tr>
            <tr><th>ROW-3</th><td>Short value</td></tr>
            <tr><th>ROW-4</th><td>First line<br>Second line<p>A paragraph inside the cell that wraps over several lines, so this row is taller than the others in the table.</p></td></tr>
            <tr><th>ROW-5</th><td>Line 1<br>Line 2<br>Line 3<br>Line 4<br>Line 5<br>Line 6</td></tr>
            <tr><th>ROW-6</th><td>Short value</td></tr>
            <tr><th>ROW-7 with a label that wraps over two lines</th><td>Short value</td></tr>
            <tr><th>ROW-8</th><td><p>Line 1<br>Line 2<br>Line 3<br>Line 4<br>Line 5<br>Line 6</p></td></tr>
          </table>
          <p>AFTER-TABLE</p>
        </body></html>
        """;

    /// <summary>For each page, the words painted on it.</summary>
    internal static async Task<List<List<string>>> WordsPerPage(string html)
    {
        var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
            html, new PdfGenerateConfig { PageSize = PageSize.A4 }, BundledFonts.PinSansSerifAsync);

        var pages = new List<List<string>>();
        for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
        {
            using var graphics = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintPage(container, graphics, page);
            pages.Add([.. graphics.Log.Where(op => op.Kind == PaintOpKind.DrawString).Select(op => op.Text!)]);
        }

        return pages;
    }

    [Theory]
    [InlineData(500)]
    [InlineData(550)]
    [InlineData(600)]
    [InlineData(650)]
    [InlineData(700)]
    [InlineData(750)]
    public async Task ATableThatDoesNotFit_StartsOnTheFirstPageAndContinuesOnTheNext(int spacer)
    {
        var pages = await WordsPerPage(Html(spacer));

        foreach (var (words, index) in pages.Select((w, i) => (w, i)))
        {
            output.WriteLine($"spacer {spacer} page {index + 1}: {string.Join(" ", words)}");
        }

        Assert.True(pages.Count >= 2, "the table does not fit the first page, so it has to continue on a second");
        Assert.Contains("ROW-1", pages[0]);
        Assert.DoesNotContain("ROW-1", pages[1]);
        Assert.Contains("AFTER-", pages[1]);
    }
}
