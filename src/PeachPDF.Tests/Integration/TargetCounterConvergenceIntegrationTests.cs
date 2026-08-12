using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Proves <c>target-counter(attr(href), page)</c> resolves the *real* page a target element lands
    /// on, across a forced page break - the test that exercises <c>HtmlContainerInt</c>'s target-page
    /// convergence loop end to end, not just that each piece (grammar, resolution, leader fill) works in
    /// isolation. A hand-authored table-of-contents entry (leader + page number, the issue #703 idiom)
    /// is asserted directly against <see cref="CssBox.Words"/> after a real multi-page layout, rather
    /// than through a generated PDF's content stream - this repo has no established way to decode a
    /// content stream's <c>Tj</c> operand back to literal text (PeachPDF generally paints through a CID
    /// font, so operands are hex glyph ids, not literal characters - see
    /// <c>DirectionalPageBreakIntegrationTests</c>'s own <c>ContentStreams</c> helper and its doc
    /// comment), while the resolved <see cref="CssBox.Words"/> text is exactly what
    /// <c>FragmentPainter.PaintWords</c> goes on to paint - a direct, faithful proxy for what the reader
    /// would see.
    /// </summary>
    public class TargetCounterConvergenceIntegrationTests
    {
        [Fact]
        public async Task TargetCounterWithLeader_ResolvesRealPageNumber_AcrossForcedPageBreak()
        {
            var html = Wrap("""
                <style>
                    #tocnum::after { content: leader(dotted) target-counter(attr(href), page); }
                    #ch2 { break-before: page; }
                </style>
                <div><a id="tocnum" href="#ch2">Chapter Two</a></div>
                <div id="ch1">Chapter One content.</div>
                <div id="ch2">Chapter Two content.</div>
                """);

            var (root, container) = await BuildAndLayout(html);

            // Prove the premise: the forced break really did put ch2 on a later page than the TOC entry,
            // and the document really did materialize more than one page.
            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1);

            var ch2Box = FindById(root, "ch2")!;
            var tocLinkBox = FindById(root, "tocnum")!;
            var ch2Slot = container.SlotStartingAt(ch2Box.Location.Y);
            var tocSlot = container.SlotStartingAt(tocLinkBox.Location.Y);
            Assert.True(ch2Slot > tocSlot);

            var (slotToPage, _) = PageAnchorResolver.BuildSlotToPageMap(container.FragmentTree.Fragmentainers);
            var expectedPage = slotToPage[ch2Slot] + 1; // materialized page index -> 1-based page number

            // The ::after pseudo-element holds the resolved leader + target-counter content list.
            var afterBox = tocLinkBox.Boxes.Single(b => b.IsAfterPseudoElement);
            Assert.Equal(2, afterBox.Words.Count);
            var leader = Assert.IsType<CssRectLeader>(afterBox.Words[0]);
            var pageNumberWord = afterBox.Words[1];

            // The value under test: not the placeholder "1" AppendTargetCounter emits before any page
            // map exists, but the page the convergence loop actually resolved ch2 to land on.
            Assert.Equal(expectedPage.ToString(), pageNumberWord.Text);

            // And the leader really did claim the line's remaining width against that real text, not a
            // stale zero-width placeholder from before the convergence loop's last reflow.
            Assert.True(leader.Width > 0);
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
