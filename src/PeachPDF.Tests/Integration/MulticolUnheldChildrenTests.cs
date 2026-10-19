using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachDrawing.PDF;
using PeachDrawing.PDF.Engine.Drawing;
using System.Text;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// Each page of a multi-column container used to walk, live, every child its own columns do not hold - all the rest of the
/// document - and recurse into each, so a long container cost the whole document once per page. A child that begins past
/// the top of the next page shows nothing on this one and is no longer visited. The children that do have to be visited
/// anyway are the interesting ones: a paragraph split across two pages reports where its remainder resumes, and a heading
/// chained to the paragraph that starts the next column by <c>break-after: avoid</c> travels with it rather than being
/// left at the foot of the column that was left.
/// </summary>
public class MulticolUnheldChildrenTests
{
    // Paragraphs of 40-110 words, a heading every 8 (break-after: avoid), two columns, default orphans/widows.
    private static string Document(int paragraphs)
    {
        var words = "lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore magna aliqua"
            .Split(' ');
        var sb = new StringBuilder("<!DOCTYPE html><html><head><style>body { font-size: 16pt } .cols { column-count: 2; column-gap: 24pt } p { margin: 0 0 7pt 0 } h2 { break-after: avoid; margin: 14pt 0 6pt 0 }</style></head><body><div class=cols>");
        var seed = 4242u;
        for (var i = 0; i < paragraphs; i++)
        {
            if (i % 8 == 0) sb.Append($"<h2>Section {i / 8 + 1}</h2>");

            seed = seed * 1664525u + 1013904223u;
            var count = 40 + (int)(seed >> 16) % 71;
            sb.Append("<p>");
            for (var w = 0; w < count; w++)
            {
                seed = seed * 1664525u + 1013904223u;
                sb.Append(words[(int)(seed >> 16) % words.Length]).Append(' ');
            }

            sb.Append("</p>");
        }

        return sb.Append("</div></body></html>").ToString();
    }

    private static async Task<HtmlContainerInt> Layout(string html)
    {
        var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
        var container = new HtmlContainerInt(renderContext);
        await container.SetHtml(html, null);

        var size = new Size(595, 842);
        container.PageSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);
        container.MaxSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);

        using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
        await container.PerformLayout(graphics);

        Assert.NotNull(container.Root);
        return container;
    }

    private static IEnumerable<CssBox> Descendants(CssBox box)
    {
        yield return box;
        foreach (var child in box.Boxes)
        {
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    [Fact]
    public async Task EveryWordOfALongTwoColumnDocument_IsClaimedByAFragment()
    {
        var container = await Layout(Document(240));

        var authored = Descendants(container.Root!).SelectMany(b => b.Words).Count();
        // Pages, not words, are what the test needs; the default font differs per platform, so the size is set above and the
        // bound is well under what any of them produce.
        Assert.True(container.FragmentTree!.Fragmentainers.Count >= 10, "expected a long document");

        var claims = new Dictionary<CssRect, int>();
        void Count(BoxFragment fragment)
        {
            foreach (var word in fragment.Words) claims[word.Word] = claims.GetValueOrDefault(word.Word) + 1;
            foreach (var child in fragment.Children) Count(child);
        }

        foreach (var fragmentainer in container.FragmentTree.Fragmentainers) Count(fragmentainer.Root);

        // No word may be lost, and none may be claimed twice: a heading chained to a paragraph by break-after: avoid
        // used to be translated to the next page while its column kept it, so two fragmentainers claimed it (issue #1719).
        Assert.Equal(authored, claims.Count);
        Assert.Empty(claims.Where(c => c.Value != 1).Select(c => c.Key.Text));
    }

    [Fact]
    public async Task ChildrenPastThePage_AreNotVisitedOnEveryPage()
    {
        // 240 paragraphs is ~30 pages of two columns. Every page used to visit the ~230 children its columns do not
        // hold; the ones past it are now skipped, which is about half of all such visits.
        var container = await Layout(Document(240));
        var pages = container.FragmentTree!.Fragmentainers.Count;

        Assert.True(container.UnheldChildrenSkipped > pages * 40,
            $"{container.UnheldChildrenSkipped} children skipped over {pages} pages");
    }

    [Fact]
    public async Task TheBoxVisitsAPageMakes_AreFewerThanTheDocumentHas()
    {
        // Visiting the whole document on each of ~30 pages is ~30 x boxes; skipping what lies past the page takes it
        // well under that.
        var container = await Layout(Document(240));
        var boxes = Descendants(container.Root!).Count();
        var pages = container.FragmentTree!.Fragmentainers.Count;

        Assert.True(container.BuildDraftCalls < (long)boxes * pages * 6 / 10,
            $"{container.BuildDraftCalls} box visits for {boxes} boxes over {pages} pages");
    }
}
