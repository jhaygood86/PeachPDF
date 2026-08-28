using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    public class MarkerPseudoElementIntegrationTests
    {
        [Fact]
        public async Task ListItem_GetsSynthesizedMarkerBox_WithLblTagType()
        {
            var (root, container) = await BuildAndLayout(Wrap("<ul><li id='li'>text</li></ul>"));
            var li = FindById(root, "li")!;

            var marker = li.Boxes.SingleOrDefault(b => b.IsMarkerPseudoElement);
            Assert.NotNull(marker);
            Assert.Equal("Lbl", marker!.PdfTagType, ignoreCase: true);
        }

        [Fact]
        public async Task MarkerBox_IsExcludedFromGenericPaintWalk()
        {
            // The marker is always painted via one explicit Paint(g)/PaintImpCore(g) call from
            // CssBox.PaintImpCore/PaintListItem (see CssBoxMarker), never discovered generically here -
            // both so the tagged-PDF path can wrap it in its own "/Lbl" element separately from the
            // rest of the list item's "/LBody" content, and so an "outside" marker never gets treated
            // as if it were normal in-flow content of the stacking context it belongs to.
            var (root, container) = await BuildAndLayout(Wrap("<ul><li id='li'>text</li></ul>"));
            var li = FindById(root, "li")!;

            var flattened = PeachPDF.Html.Core.Paint.StackingOrder.Flatten(FragmentPaintHarness.FragmentOf(container, li)).ToList();
            Assert.DoesNotContain(flattened, p => p.Box.IsMarkerPseudoElement);
        }

        [Fact]
        public async Task MarkerBox_DisplayResolvesToInline()
        {
            // Unlike the old cascade-only stub (display: none), a real ::marker box's Display is the
            // ordinary CSS initial value ("inline") - it's a genuine, laid-out box now.
            var (root, container) = await BuildAndLayout(Wrap("<ul><li id='li'>text</li></ul>"));
            var li = FindById(root, "li")!;

            var marker = li.Boxes.Single(b => b.IsMarkerPseudoElement);
            Assert.Equal(DisplayMode.Inline, marker.Display.Value);
        }

        [Fact]
        public async Task MarkerBox_IsRealBox_WithLocationAndContentAfterLayout()
        {
            // The default (outside) marker for the first <li> in an <ol> shows "1." - proving
            // CssBoxMarker.ResolveDefaultContent() actually ran and the box laid itself out with a
            // real, non-zero Location/size (CssBoxMarker.PerformLayoutImp), not a display:none stub.
            var (root, container) = await BuildAndLayout(Wrap("<ol><li id='li'>text</li></ol>"));
            var li = FindById(root, "li")!;

            var marker = (CssBoxMarker)li.Boxes.Single(b => b.IsMarkerPseudoElement);
            Assert.Equal("1.", marker.Text);
            Assert.True(marker.Location.X < li.ClientLeft, "outside marker should sit to the left of the list item's content edge");
            Assert.True(marker.ActualRight - marker.Location.X > 0, "marker should have measured a non-zero width");
        }

        [Fact]
        public async Task MarkerTagType_CanBeSuppressedByAuthor()
        {
            var html = Wrap("<style>li::marker { -peachpdf-pdf-tag-type: none }</style><ul><li id='li'>text</li></ul>");
            var (root, container) = await BuildAndLayout(html);
            var li = FindById(root, "li")!;

            var marker = li.Boxes.Single(b => b.IsMarkerPseudoElement);
            Assert.Equal("none", marker.PdfTagType, ignoreCase: true);
        }

        [Fact]
        public async Task NonListItemElement_DoesNotGetMarkerBox()
        {
            var (root, container) = await BuildAndLayout(Wrap("<p id='p'>text</p>"));
            var p = FindById(root, "p")!;

            Assert.DoesNotContain(p.Boxes, b => b.IsMarkerPseudoElement);
        }

        [Fact]
        public async Task ListItemLayout_IsUnaffectedByMarkerBoxSynthesis()
        {
            // The marker box must be a pure cascade target with zero visual/layout footprint -
            // list-item content should measure identically to a document with no ::marker rule at all.
            var withRule = await BuildAndLayout(
                Wrap("<style>li::marker { -peachpdf-pdf-tag-type: Lbl }</style><ul><li id='li'>text</li></ul>"));
            var withoutRule = await BuildAndLayout(Wrap("<ul><li id='li'>text</li></ul>"));

            var liWith = FindById(withRule.root, "li")!;
            var liWithout = FindById(withoutRule.root, "li")!;

            Assert.Equal(liWithout.ActualBottom, liWith.ActualBottom);
            Assert.Equal(liWithout.ActualRight, liWith.ActualRight);
        }

        [Fact]
        public async Task OutsideMarker_AccountsForOwnersBorderTopWidth()
        {
            // CssBoxMarker.PerformLayoutImp's top calculation omitted ActualBorderTopWidth (unlike
            // ClientTop's own definition), so a bordered <li>/<ul>/<ol> sat its marker too high by
            // exactly the border width. Uses a decimal (<ol>) marker to skip the MarkerShape centering
            // branch entirely, isolating just this one term.
            var withBorder = await BuildAndLayout(Wrap("<ol><li id='li' style='border-top:4pt solid black'>text</li></ol>"));
            var withoutBorder = await BuildAndLayout(Wrap("<ol><li id='li'>text</li></ol>"));

            var markerWith = li(withBorder.root);
            var markerWithout = li(withoutBorder.root);

            Assert.Equal(4, markerWith.Location.Y - markerWithout.Location.Y, 1);

            static CssBoxMarker li(CssBox root) =>
                (CssBoxMarker)FindById(root, "li")!.Boxes.Single(b => b.IsMarkerPseudoElement);
        }

        [Fact]
        public async Task InsideMarkerShape_IsCenteredWithinTheLine_NotBaselineAligned()
        {
            // An outside marker's shape is explicitly centered within the owner's line box
            // (PerformLayoutImp's own MarkerShape branch). An "inside" marker never reaches that code -
            // it flows as an ordinary inline box, positioned by CssLayoutEngine's line-box vertical-align
            // pass, which defaults every box to baseline - sitting a small vector shape noticeably high
            // relative to the adjacent text. CssBoxMarker.ResolveDefaultContent now sets VerticalAlign:
            // middle for a shape marker, and CssLayoutEngine's ancestor walk stops at a marker box
            // instead of reading its owning <li>'s (unset) vertical-align.
            var (root, container) = await BuildAndLayout(Wrap(
                "<ul style='list-style-position:inside'><li id='li'>text</li></ul>"));
            var li = FindById(root, "li")!;

            var marker = (CssBoxMarker)li.Boxes.Single(b => b.IsMarkerPseudoElement);
            Assert.Equal(VerticalAlignment.Middle, marker.VerticalAlign.Value!.Keyword);

            var markerWord = marker.Words.Single();
            var textWord = AllWords(li).First(w => w != markerWord);

            // The shape's vertical center should fall within the adjacent text word's own vertical
            // extent - "roughly level with the text", not sitting entirely above or below it the way
            // baseline alignment (bottom of a 0.35x-font-height shape pinned to the text baseline) does.
            var markerCenter = markerWord.Top + markerWord.Height / 2;
            Assert.InRange(markerCenter, textWord.Top, textWord.Top + textWord.Height);
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
            container.MaxSize  = PeachPDF.Utilities.Utils.Convert(size, 1.0);

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

        private static System.Collections.Generic.IEnumerable<CssRect> AllWords(CssBox box)
        {
            foreach (var word in box.Words) yield return word;
            foreach (var child in box.Boxes)
                foreach (var word in AllWords(child))
                    yield return word;
        }
    }
}
