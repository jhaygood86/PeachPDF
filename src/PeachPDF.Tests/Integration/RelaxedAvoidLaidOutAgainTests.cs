using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A <c>break-inside: avoid</c> block taller than a page that straddles a page foot is relaxed by moving it to the next page
/// (css-break-3 §4.3). Moved by translation it kept its unbroken extent, so the lines it carried past the foot of the new
/// page were clipped rather than continuing on the page after (§4.4). It is laid out again there instead, and fragments
/// from its new top like any other box.
/// </summary>
public class RelaxedAvoidLaidOutAgainTests
{
    private const double PageHeight = 250;
    private const double Margin = 20;

    private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment) =>
        new[] { fragment }.Concat(fragment.Children.SelectMany(Flatten));

    private static string Words(int from, int count) => string.Join(" ", Enumerable.Range(from, count).Select(n => $"r{n}"));

    private static async Task<List<(string Word, int Page, double Bottom)>> LayOutAsync(string body, char startsWith = 'r')
    {
        var (_, container) = await LayoutHarness.LayoutAsync(
            "<html><body style='margin:0;font:10pt/12pt \"Relaxed Fixture\"'>" + body + "</body></html>",
            pageWidth: 300, pageHeight: PageHeight, margin: Margin,
            configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.LiberationSans, "Relaxed Fixture"));

        var tree = container.FragmentTree!;
        var words = new List<(string, int, double)>();

        for (var page = 0; page < tree.Fragmentainers.Count; page++)
        {
            foreach (var fragment in Flatten(tree.Fragmentainers[page].Root).SelectMany(f => f.Words))
            {
                var text = (fragment.Word.Text ?? "").Trim();
                if (text.StartsWith(startsWith)) words.Add((text, page, fragment.Rect.Bottom));
            }
        }

        return words;
    }

    // Starts on page one, straddles its foot, and is taller than the 210pt band it would move to.
    private static string Document() =>
        "<div style='height:100pt'>filler</div>"
        + "<div style='margin-top:20pt;break-inside:avoid;display:flow-root'>"
        + $"<p style='margin:10pt 0'>{Words(0, 40)}</p><p style='margin:10pt 0'>{Words(40, 40)}</p>"
        + string.Concat(Enumerable.Range(2, 4).Select(i => $"<p style='margin:10pt 0'>{Words(i * 40, 40)}</p>")) + "</div>";

    [Fact]
    public async Task ABlockTallerThanAPage_RelaxedByMovingIt_StillDrawsEveryWordOnce()
    {
        var words = await LayOutAsync(Document());

        Assert.Equal(240, words.Count);
        Assert.Equal(240, words.Select(w => w.Word).Distinct().Count());
    }

    [Fact]
    public async Task ABlockTallerThanAPage_RelaxedByMovingIt_DrawsNoWordPastThePageFoot()
    {
        var words = await LayOutAsync(Document());

        Assert.All(words, w => Assert.True(w.Bottom <= PageHeight - Margin + 0.5,
            $"{w.Word} on page {w.Page} ends at {w.Bottom}, past the {PageHeight - Margin}pt foot"));
    }

    // The reduction of a document that lost its last line: the avoid block (flow-root, inside nested padded blocks) is 216pt
    // tall against the 210pt band.
    [Fact]
    public async Task AFlowRootAvoidBlockJustTallerThanAPage_KeepsItsLastLine()
    {
        var words = await LayOutAsync(
            "<div style='column-count:2'><ul>w470 lorem w471 w472 w473 w474 w475</ul></div> w1097 "
            + "<div style='margin-top:20pt;margin-bottom:20pt;padding:12pt'><div><p></p><div style='margin-top:4pt;padding:2pt'>"
            + "<div style='margin-top:20pt;break-inside:avoid;display:flow-root'>"
            + "<h3 style='font-weight:normal'>w1117 lorem w1118 lorem w1119 w1120 w1121 lorem w1122 lorem</h3>w1123 w1124 w1125 lorem w1126 lorem "
            + "<p>w1127 w1128 w1129 w1130 w1131 lorem w1132 w1133 w1134 w1135 w1136 w1137 lorem</p>"
            + "w1138 lorem w1139 w1140 lorem w1141 lorem w1142 lorem w1143 w1144 lorem</div></div></div></div>",
            startsWith: 'w');

        Assert.Single(words, w => w.Word == "w1144");
        Assert.All(words, w => Assert.True(w.Bottom <= PageHeight - Margin + 0.5, $"{w.Word} ends at {w.Bottom}"));
    }}
