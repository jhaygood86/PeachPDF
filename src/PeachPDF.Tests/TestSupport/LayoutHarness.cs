using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// The shared lightweight layout harness: builds an <see cref="HtmlContainerInt"/> over a real
    /// <see cref="PdfSharpAdapter"/>, runs layout, and hands back the laid-out box tree plus the
    /// container (whose <c>FragmentTree</c> is layout's real output). Prefer this over hand-rolling
    /// another per-file <c>BuildAndLayout</c> copy.
    /// </summary>
    internal static class LayoutHarness
    {
        /// <summary>
        /// Lays <paramref name="html"/> out on a page of <paramref name="pageWidth"/> ×
        /// <paramref name="pageHeight"/> points. <c>PixelsPerPoint</c> is pinned to 1.0 to match
        /// production's default <c>PixelsPerInch = 72</c>, so asserted coordinates read as points.
        /// </summary>
        internal static async Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync(
            string html,
            double pageWidth = 595,
            double pageHeight = 842,
            double margin = 20)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter)
            {
                MarginTop = margin,
                MarginBottom = margin,
                MarginLeft = margin,
                MarginRight = margin
            };

            var sheet = new XSize(pageWidth, pageHeight);
            container.PageSize = Utilities.Utils.Convert(sheet, 1.0);

            await container.SetHtml(html, null);

            // Mirror PdfGenerator.SetContent: PageSize is the *content band* (the sheet less its
            // margins) and the document origin sits at the top-left of that band. Getting this wrong
            // puts the root box above the first page's band and makes pagination nonsense.
            var band = new XSize(pageWidth - container.MarginLeft - container.MarginRight,
                                 pageHeight - container.MarginTop - container.MarginBottom);
            container.PageSize = Utilities.Utils.Convert(band, 1.0);
            container.MaxSize = Utilities.Utils.Convert(band, 1.0);
            container.Location = Utilities.Utils.Convert(new XPoint(container.MarginLeft, container.MarginTop), 1.0);

            var measure = XGraphics.CreateMeasureContext(sheet, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        /// <summary>
        /// Wraps a body fragment in a minimal document, so a test can state only the markup it cares about.
        /// </summary>
        internal static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body style='margin:0'>{body}</body></html>";

        /// <summary>
        /// Depth-first search for the box carrying <c>id="<paramref name="id"/>"</c>.
        /// </summary>
        internal static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.TryGetAttribute("id") == id)
                return box;

            foreach (var childBox in box.Boxes)
            {
                var found = FindById(childBox, id);
                if (found is not null) return found;
            }

            return null;
        }

        /// <summary>
        /// Every box in the tree, in document order.
        /// </summary>
        internal static IEnumerable<CssBox> Descendants(CssBox box)
        {
            yield return box;

            foreach (var childBox in box.Boxes)
            {
                foreach (var descendant in Descendants(childBox))
                {
                    yield return descendant;
                }
            }
        }
    }
}
