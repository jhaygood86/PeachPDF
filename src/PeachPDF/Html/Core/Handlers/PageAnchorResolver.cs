using PeachPDF.Html.Core.Fragments;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Shared rect-to-page/Y resolution used by both <c>PdfGenerator.HandleLinks</c>'s anchor-link
    /// destinations and <see cref="BookmarkOutlineBuilder"/>'s PDF outline destinations, so the two
    /// don't each re-derive the fragmentainer-slot -&gt; <c>document.Pages</c> mapping and per-slot Y
    /// math independently.
    /// </summary>
    internal static class PageAnchorResolver
    {
        /// <summary>
        /// Materialized pages are the fragmentainers, which may skip content-empty grid slots entirely
        /// - so a document-space slot index and a <c>document.Pages</c> index are NOT interchangeable.
        /// Builds that mapping once for a whole generation pass.
        /// </summary>
        public static (Dictionary<int, int> SlotToPage, int MaxMappedSlot) BuildSlotToPageMap(
            IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            var slotToPage = new Dictionary<int, int>(fragmentainers.Count);
            for (var p = 0; p < fragmentainers.Count; p++)
                slotToPage[fragmentainers[p].SlotIndex] = p;

            var maxMappedSlot = slotToPage.Count > 0 ? slotToPage.Keys.Max() : -1;
            return (slotToPage, maxMappedSlot);
        }

        /// <summary>
        /// Resolves a true-point rect (see <see cref="HtmlContainer.GetElementRectangle"/>, or a live
        /// <see cref="Dom.CssBox"/> rect converted via <see cref="Utilities.Utils.Convert(Html.Adapters.Entities.RRect, double)"/>) to the PDF
        /// page it lands on and its true-point Y offset on that page - <paramref name="ppp"/> converts
        /// it back to the internal pixel space <paramref name="inner"/>'s slot geometry operates in,
        /// mirroring how the rest of this pass round-trips point-space rects for slot attribution. A
        /// rect inside a skipped (content-empty) slot attributes to the next materialized page - the
        /// nearest place a reader can actually land - falling back to the last mapped page if nothing
        /// was mapped past it.
        /// </summary>
        public static (int PageIndex, double TopPt) ResolveRectToPage(
            HtmlContainerInt inner, double ppp, IReadOnlyDictionary<int, int> slotToPage, int maxMappedSlot,
            int fallbackPageCount, XRect rect)
        {
            var pixelY = rect.Top * ppp;
            var page = ResolvePixelYToPage(inner, slotToPage, maxMappedSlot, fallbackPageCount, pixelY);

            var slot = inner.SlotStartingAt(pixelY);
            var topPt = inner.PageGeometry.GetPage(slot).MarginTopPt
                + (pixelY - inner.PageTopOf(slot)) / ppp;

            return (page, topPt);
        }

        /// <summary>
        /// The page-index half of <see cref="ResolveRectToPage"/>, taking a Y already in
        /// <paramref name="inner"/>'s own internal pixel space rather than true points - for callers
        /// (<c>CssContentEngine.AppendTargetCounter</c>'s <c>target-counter(_, page)</c> resolution) that
        /// run during layout itself, before a <c>PixelsPerPoint</c>/<c>PdfDocument</c> exists to convert
        /// through, and only need the page number (not <see cref="ResolveRectToPage"/>'s point-space
        /// <c>TopPt</c>, meaningless without one).
        /// </summary>
        public static int ResolvePixelYToPage(
            HtmlContainerInt inner, IReadOnlyDictionary<int, int> slotToPage, int maxMappedSlot,
            int fallbackPageCount, double pixelY)
        {
            var slot = inner.SlotStartingAt(pixelY);
            var page = -1;
            for (var s = Math.Max(slot, 0); page < 0 && s <= maxMappedSlot; s++)
                if (slotToPage.TryGetValue(s, out var found))
                    page = found;

            return page < 0 ? fallbackPageCount - 1 : page;
        }
    }
}
