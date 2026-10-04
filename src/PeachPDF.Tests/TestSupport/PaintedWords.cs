using System.Text.RegularExpressions;
using PeachDrawing.Core;
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

    /// <summary>
    /// The words that end up fully inside their page's content band, which is what a reader of the PDF can see. A word
    /// the fragment tree places across the page foot (or past its right edge) is painted but clipped away, so a check
    /// on the paint log alone calls it drawn: that is how the #1531 and #1532 reductions passed a recording-only test.
    /// </summary>
    public static async Task<(IReadOnlyList<string> Visible, int Pages)> LayOutAndCollectVisibleAsync(
        string html, Func<PdfSharpAdapter, Task>? extraFonts = null)
    {
        var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
            html, new PdfGenerateConfig(), async adapter =>
            {
                await BundledFonts.PinSansSerifAsync(adapter);
                if (extraFonts is not null) await extraFonts(adapter);
            });

        List<string> visible = [];
        var fragmentainers = container.FragmentTree!.Fragmentainers;

        foreach (var fragmentainer in fragmentainers)
        {
            Collect(fragmentainer.Root, fragmentainer.Rect, visible);
        }

        return (visible.Where(w => WordPattern().IsMatch(w) && w.Length == WordPattern().Match(w).Length)
            .Order().ToList(), fragmentainers.Count);

        static void Collect(global::PeachPDF.Html.Core.Fragments.BoxFragment fragment, Rect band, List<string> into)
        {
            const double tolerance = 0.5;

            foreach (var word in fragment.Words)
            {
                var r = word.Rect;
                if (r.Bottom <= band.Bottom + tolerance && r.Right <= band.Right + tolerance
                    && r.Top >= band.Top - tolerance && r.Left >= band.Left - tolerance)
                {
                    into.Add(word.Word.Text ?? string.Empty);
                }
            }

            foreach (var child in fragment.Children)
            {
                Collect(child, band, into);
            }
        }
    }

    /// <summary>
    /// Where each word of the document sits: its page index and its fragmentainer-local X and Y, in points. The first
    /// fragment of a word wins, so a word drawn twice reports where it first appears.
    /// </summary>
    public static async Task<Dictionary<string, (int Page, double X, double Y)>> PositionsAsync(string html)
    {
        var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
            html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);

        Dictionary<string, (int Page, double X, double Y)> positions = [];
        var fragmentainers = container.FragmentTree!.Fragmentainers;

        for (var page = 0; page < fragmentainers.Count; page++)
        {
            Collect(fragmentainers[page].Root, page, positions);
        }

        return positions;

        static void Collect(global::PeachPDF.Html.Core.Fragments.BoxFragment fragment, int page,
            Dictionary<string, (int Page, double X, double Y)> into)
        {
            foreach (var word in fragment.Words)
            {
                into.TryAdd(word.Word.Text ?? string.Empty, (page, word.Rect.X, word.Rect.Y));
            }

            foreach (var child in fragment.Children)
            {
                Collect(child, page, into);
            }
        }
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
