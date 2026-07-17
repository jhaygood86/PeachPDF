using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies <c>::first-letter</c> - previously parsed (see <c>PseudoElementSelectorFactory</c>)
    /// but never matched anything (<c>CssData.DoesSelectorMatch(PseudoElementSelector, ...)</c> only
    /// handled <c>before</c>/<c>after</c>/<c>marker</c>), and had no box-splitting logic at all. Uses
    /// real layout via <see cref="HtmlContainerInt"/>/<see cref="PdfSharpAdapter"/> and asserts on the
    /// resulting box tree - see <c>DomParser.ApplyFirstLetterPseudoElements</c> for the implementation.
    /// </summary>
    public class FirstLetterPseudoElementIntegrationTests
    {
        [Fact]
        public async Task BasicSplit_CreatesFirstLetterBoxWithRemainderSibling()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>p::first-letter { font-size: 200%; color: rgb(255,0,0) }</style>" +
                "<p id='p'>Hello world</p>"));
            var p = FindById(root, "p")!;

            var firstLetterBox = p.Boxes.Single(b => b.IsFirstLetterPseudoElement);
            Assert.Equal("H", firstLetterBox.Text);
            Assert.Equal("rgb(255, 0, 0)", firstLetterBox.Color);
            Assert.True(firstLetterBox.ActualFont.Size > p.ActualFont.Size);

            var remainder = p.Boxes.Single(b => !b.IsFirstLetterPseudoElement && b.Text != null);
            Assert.Equal("ello world", remainder.Text);
        }

        [Fact]
        public async Task NestedInline_SplitsInsideNestedElement_InheritsItsStyleAndMatchesOuterRule()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>p::first-letter { color: rgb(255,0,0) }</style>" +
                "<p id='p'><b>Hello</b> world</p>"));
            var p = FindById(root, "p")!;
            var b = FindByTag(root, "b")!;

            var firstLetterBox = b.Boxes.Single(x => x.IsFirstLetterPseudoElement);
            Assert.Equal("H", firstLetterBox.Text);

            // Inherits bold weight from its real structural parent <b> ...
            Assert.Equal(b.FontWeight, firstLetterBox.FontWeight);
            // ... while still picking up the color from the rule matched against <p>, not <b>.
            Assert.Equal("rgb(255, 0, 0)", firstLetterBox.Color);

            var remainder = b.Boxes.Single(x => !x.IsFirstLetterPseudoElement && x.Text != null);
            Assert.Equal("ello", remainder.Text);
        }

        [Fact]
        public async Task LeadingPunctuation_IncludedInFirstLetterUnit()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>p::first-letter { color: rgb(255,0,0) }</style>" +
                "<p id='p'>“Hello” world</p>"));
            var p = FindById(root, "p")!;

            var firstLetterBox = p.Boxes.Single(x => x.IsFirstLetterPseudoElement);
            Assert.Equal("“H", firstLetterBox.Text);
        }

        [Fact]
        public async Task Idempotent_DoesNotDoubleSplitOnReprocessing()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>p::first-letter { color: rgb(255,0,0) } p::first-letter { font-weight: bold }</style>" +
                "<p id='p'>Hello world</p>"));
            var p = FindById(root, "p")!;

            var firstLetterBoxes = p.Boxes.Where(x => x.IsFirstLetterPseudoElement).ToList();
            Assert.Single(firstLetterBoxes);

            // Both rules (from two separate cascade phases matching the same box) actually applied to
            // the one synthesized box, rather than only the first ever taking effect.
            Assert.Equal("rgb(255, 0, 0)", firstLetterBoxes[0].Color);
            Assert.Equal(CssConstants.Bold, firstLetterBoxes[0].FontWeight);
        }

        [Fact]
        public async Task BlockBoundary_DoesNotSplitIntoNestedBlockDescendant()
        {
            // #d::first-letter (not the more general "div::first-letter", which would also
            // legitimately match the nested #inner div in its own right) isolates the behavior under
            // test: does the outer div's own search skip over a nested block-level descendant rather
            // than descending into it.
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>#d::first-letter { color: rgb(255,0,0) }</style>" +
                "<div id='d'><div id='inner'>Nested</div>text</div>"));
            var d = FindById(root, "d")!;
            var inner = FindById(root, "inner")!;

            // The outer div's own first-letter target is its own direct inline text ("text"), not
            // anything inside the nested block <div id="inner">. A later, unrelated correction pass
            // (anonymous block-box wrapping for the block-in-inline-context normalization "text" needs
            // as a sibling of the block-level #inner) may nest the split result a level deeper than
            // "direct child of d" by the time layout finishes, so search the whole subtree rather than
            // just direct children.
            Assert.DoesNotContain(AllDescendants(inner), x => x.IsFirstLetterPseudoElement);

            var firstLetterBox = AllDescendants(d).SingleOrDefault(x => x.IsFirstLetterPseudoElement);
            Assert.NotNull(firstLetterBox);
            Assert.Equal("t", firstLetterBox!.Text);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

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

        private static System.Collections.Generic.IEnumerable<CssBox> AllDescendants(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                yield return child;
                foreach (var grandchild in AllDescendants(child))
                    yield return grandchild;
            }
        }

        private static CssBox? FindByTag(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name.Equals(tag, System.StringComparison.OrdinalIgnoreCase) == true)
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByTag(child, tag);
                if (found != null) return found;
            }
            return null;
        }
    }
}
