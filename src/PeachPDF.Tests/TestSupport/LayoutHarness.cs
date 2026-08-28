using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <paramref name="pageHeight"/> points. <paramref name="pixelsPerPoint"/> defaults to 1.0 to
        /// match production's default <c>PixelsPerInch = 72</c>, so asserted coordinates read as points;
        /// pass a non-default value (e.g. 2.0, simulating <c>PixelsPerInch = 144</c>) for a test whose
        /// subject is <c>PixelsPerPoint</c>-scaling behavior itself (issue #812 and its siblings).
        /// </summary>
        /// <param name="prepare">
        /// Optional: run against the parsed box tree's root after <c>SetHtml</c> and before layout, for a
        /// test that has to put something in the tree the parser cannot produce — a box whose layout
        /// behaviour is the subject, rather than a box some markup happens to produce.
        /// </param>
        /// <param name="after">
        /// Optional: run once layout has finished, with the same <see cref="RGraphics"/> layout itself
        /// used, for a test whose subject is a layout call the document cannot make on its own — running
        /// one engine again over a box it already laid out, say. It runs before the graphics context is
        /// disposed, which is why it belongs here rather than after the call returns.
        /// </param>
        internal static async Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync(
            string html,
            double pageWidth = 595,
            double pageHeight = 842,
            double margin = 20,
            Action<CssBox>? prepare = null,
            Func<CssBox, HtmlContainerInt, RGraphics, Task>? after = null,
            double pixelsPerPoint = 1.0)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = pixelsPerPoint };
            var container = new HtmlContainerInt(adapter)
            {
                MarginTop = margin,
                MarginBottom = margin,
                MarginLeft = margin,
                MarginRight = margin
            };

            var sheet = new XSize(pageWidth, pageHeight);
            container.PageSize = Utilities.Utils.Convert(sheet, pixelsPerPoint);

            await container.SetHtml(html, null);

            if (prepare is not null)
            {
                Assert.NotNull(container.Root);
                prepare(container.Root!);
            }

            // Mirror PdfGenerator.SetContent: PageSize is the *content band* (the sheet less its
            // margins) and the document origin sits at the top-left of that band. Getting this wrong
            // puts the root box above the first page's band and makes pagination nonsense.
            var band = new XSize(pageWidth - container.MarginLeft - container.MarginRight,
                                 pageHeight - container.MarginTop - container.MarginBottom);
            container.PageSize = Utilities.Utils.Convert(band, pixelsPerPoint);
            container.MaxSize = Utilities.Utils.Convert(band, pixelsPerPoint);
            container.Location = Utilities.Utils.Convert(new XPoint(container.MarginLeft, container.MarginTop), pixelsPerPoint);

            var measure = XGraphics.CreateMeasureContext(sheet, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, pixelsPerPoint);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);

            if (after is not null)
            {
                await after(container.Root!, container, graphics);
            }

            return (container.Root!, container);
        }

        /// <summary>
        /// Lays <paramref name="html"/> out <paramref name="passes"/> times over the <i>same</i> box tree,
        /// taking <paramref name="snapshot"/> after each pass.
        /// </summary>
        /// <remarks>
        /// Whether laying the same subtree out again reproduces the first result is a real question about
        /// a layout engine, not a theoretical one: a repeating table <c>&lt;thead&gt;</c> is detached and
        /// replaced by per-page proxies that nothing removes, so a second run over the same table measures
        /// taller. Any engine that is to be re-entered by a resumed fragmentainer pass has to be checked
        /// the same way, which is what this exists for.
        /// </remarks>
        internal static async Task<IReadOnlyList<T>> LayoutRepeatedlyAsync<T>(
            string html,
            int passes,
            Func<CssBox, HtmlContainerInt, T> snapshot,
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

            var band = new XSize(pageWidth - container.MarginLeft - container.MarginRight,
                                 pageHeight - container.MarginTop - container.MarginBottom);
            container.PageSize = Utilities.Utils.Convert(band, 1.0);
            container.MaxSize = Utilities.Utils.Convert(band, 1.0);
            container.Location = Utilities.Utils.Convert(new XPoint(container.MarginLeft, container.MarginTop), 1.0);

            var measure = XGraphics.CreateMeasureContext(sheet, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);

            var results = new List<T>(passes);

            for (var pass = 0; pass < passes; pass++)
            {
                await container.PerformLayout(graphics);
                Assert.NotNull(container.Root);
                results.Add(snapshot(container.Root!, container));
            }

            return results;
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
        /// Depth-first search that also descends into a repeating <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>'s
        /// detached source subtree via each <see cref="CssProxyBox"/> it finds, since that content isn't
        /// reachable through the ordinary <see cref="FindById"/> walk once
        /// <c>CssLayoutEngineTable.RemoveHeaderFooterFromTree</c> has detached it from the live tree.
        /// </summary>
        internal static CssBox? FindByIdIncludingHeaderFooterProxies(CssBox box, string id)
        {
            if (FindById(box, id) is { } found) return found;

            foreach (var proxy in Descendants(box).OfType<CssProxyBox>())
            {
                if (FindById(proxy.SourceBox, id) is { } inSource) return inSource;
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
