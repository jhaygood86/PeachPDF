using System.Text.RegularExpressions;
using PeachPDF.Adapters;
using PeachPDF.Html.Core.Fragmentation;

namespace PeachPDF.Tests.TestSupport;

/// <summary>
/// Lays a document out and collects the unique-word tokens (<c>w12_34</c>) every page actually paints, so a test can
/// assert that no word was lost (drawn on no page) or doubled. Counting the words in the markup against the words
/// painted is the check a fuzz-found content-loss bug needs: a passing content-stream substring test proves nothing
/// about a word that no page draws.
/// </summary>
internal static partial class PaintedWords
{
    [GeneratedRegex(@"w\d+_\d+")]
    private static partial Regex WordPattern();

    public static IReadOnlyList<string> InMarkup(string markup) =>
        WordPattern().Matches(markup).Select(m => m.Value).Order().ToList();

    public static async Task<(IReadOnlyList<string> Painted, int Pages)> LayOutAndPaintAsync(string html)
    {
        var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
            html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);

        List<string> painted = [];
        var pages = container.FragmentTree!.Fragmentainers.Count;

        for (var page = 0; page < pages; page++)
        {
            using var graphics = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(container, graphics, page);
            painted.AddRange(graphics.Log.Where(op => op.Kind == PaintOpKind.DrawString).Select(op => op.Text!));
        }

        return (painted.Where(w => WordPattern().IsMatch(w) && w.Length == WordPattern().Match(w).Length)
            .Order().ToList(), pages);
    }

    /// <summary>The words in <paramref name="markup"/> that no page painted, or that were painted more than once.</summary>
    public static (List<string> Lost, List<string> Doubled) Diff(string markup, IReadOnlyList<string> painted)
    {
        var expected = InMarkup(markup);
        var lost = expected.Except(painted).ToList();
        var doubled = painted.GroupBy(w => w).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        return (lost, doubled);
    }
}
