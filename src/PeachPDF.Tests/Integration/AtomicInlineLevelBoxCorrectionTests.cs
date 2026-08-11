using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// DomParser's "block inside inline" DOM-correction pass (CorrectBlockInsideInline) must never
    /// look through an inline-level box that establishes its own independent formatting context
    /// (CSS Display 3 §2.3: inline-block/inline-table/inline-grid/inline-flex all do) when deciding
    /// whether an ANCESTOR box needs splitting - DomParser.IsAtomicInlineLevel is what makes such a
    /// box opaque for that purpose. Before this box's own <c>display: none</c> child was included,
    /// a child whose own resolved display was <c>none</c> (not "inline" per <c>CssBox.IsInline</c>)
    /// made the correction pass misclassify the box's content as containing a "block", incorrectly
    /// splitting the child out of its real parent entirely - reproduced by an inline-block
    /// &lt;select&gt; with a <c>display: none</c> &lt;option&gt; child (see #709's forms work, which
    /// found this while giving &lt;option&gt; a display:none UA rule).
    /// </summary>
    public class AtomicInlineLevelBoxCorrectionTests
    {
        [Fact]
        public async Task DisplayNoneChildOfInlineBlockBox_StaysNestedUnderItsRealParent()
        {
            var html = Wrap(
                "<select id='sel'>" +
                "<option id='opt1' style='display:none'>Red</option>" +
                "<option id='opt2' style='display:none'>Green</option>" +
                "</select>");

            var (root, _) = await BuildAndLayout(html);
            var select = FindById(root, "sel")!;
            var opt1 = FindById(root, "opt1")!;
            var opt2 = FindById(root, "opt2")!;

            Assert.True(IsDescendantOf(opt1, select), "opt1 (display:none) must stay nested under its real <select> parent.");
            Assert.True(IsDescendantOf(opt2, select), "opt2 must stay nested under its real <select> parent.");
        }

        [Fact]
        public async Task DisplayNoneChildOfInlineTableBox_StaysNestedUnderItsRealParent()
        {
            var html = Wrap(
                "<div id='wrap'>" +
                "<div id='it' style='display:inline-table'>" +
                "<div style='display:table-row'><div id='cell' style='display:table-cell'>cell</div></div>" +
                "<div id='hidden' style='display:none'>hidden</div>" +
                "</div>" +
                "</div>");

            var (root, _) = await BuildAndLayout(html);
            var inlineTable = FindById(root, "it")!;
            var hidden = FindById(root, "hidden")!;

            Assert.True(IsDescendantOf(hidden, inlineTable), "a display:none sibling inside an inline-table must stay nested under it.");
        }

        [Fact]
        public async Task DisplayNoneChildOfInlineFlexBox_StillStaysNestedUnderItsRealParent()
        {
            // Regression guard for the pre-existing inline-flex handling (issue #462's own fix) -
            // confirms broadening IsAtomicInlineLevel didn't disturb it.
            var html = Wrap(
                "<div id='fx' style='display:inline-flex'>" +
                "<div id='item'>item</div>" +
                "<div id='hidden' style='display:none'>hidden</div>" +
                "</div>");

            var (root, _) = await BuildAndLayout(html);
            var flex = FindById(root, "fx")!;
            var hidden = FindById(root, "hidden")!;

            Assert.True(IsDescendantOf(hidden, flex), "a display:none sibling inside an inline-flex must stay nested under it.");
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

        private static bool IsDescendantOf(CssBox box, CssBox ancestor)
        {
            var current = box.ParentBox;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor)) return true;
                current = current.ParentBox;
            }
            return false;
        }
    }
}
