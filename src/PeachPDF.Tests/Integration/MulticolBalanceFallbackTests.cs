using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A balanced multi-column container whose retries ran out before one fitted used to keep the last, failing
/// fill and spill its tail onto the next page, although a fill at the page's whole budget had already
/// finished the flow (css-multicol-1 balances only within what fits). Inside a <c>break-inside: avoid</c>
/// block the relocation that followed then left the columns recorded for the page it left behind, so every
/// word was drawn on both pages.
/// </summary>
public class MulticolBalanceFallbackTests
{
    private static string Html(int headingWords, string outerStyle = "") =>
        "<div style='border:1px solid #666;margin:0 0 4pt;break-inside:avoid;" + outerStyle + "'>"
        + "<div style='columns:2;column-gap:6pt'>"
        + string.Join(" ", Enumerable.Range(147, 9).Select(n => $"z530_{n}")) + " "
        + "<h3 style='font-size:13pt;font-weight:normal;margin:6pt 0 4pt;'>"
        + string.Join(" ", Enumerable.Range(164, headingWords).Select(n => $"z530_{n}"))
        + "</h3></div></div>";

    private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment) =>
        new[] { fragment }.Concat(fragment.Children.SelectMany(Flatten));

    private static async Task<List<(string Word, int Page)>> WordsAsync(string body)
    {
        var (_, container) = await LayoutHarness.LayoutAsync(
            "<html><body style='margin:0;font:10pt/10pt \"Balance Fixture\"'>" + body + "</body></html>",
            pageWidth: 240, pageHeight: 150.5, margin: 20,
            configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.LiberationSans, "Balance Fixture"));

        var tree = container.FragmentTree!;
        var words = new List<(string, int)>();

        for (var page = 0; page < tree.Fragmentainers.Count; page++)
        {
            foreach (var word in Flatten(tree.Fragmentainers[page].Root).SelectMany(f => f.Words))
            {
                var text = (word.Word.Text ?? "").Trim();
                if (text.StartsWith('z')) words.Add((text, page));
            }
        }

        return words;
    }

    [Fact]
    public async Task EveryWordInsideAnAvoidBlock_IsDrawnExactlyOnce_AtAnyHeadingLength()
    {
        var failures = new List<string>();

        for (var n = 1; n <= 13; n++)
        {
            var words = await WordsAsync(Html(n));
            var expected = 9 + n;

            if (words.Count != expected || words.Select(w => w.Word).Distinct().Count() != expected)
            {
                failures.Add($"{n}: {words.Count} drawn of {expected}: {string.Join(",", words.Select(w => w.Word[6..] + "@" + w.Page))}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}