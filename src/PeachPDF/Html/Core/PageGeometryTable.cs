using PeachPDF.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// One pagination slot's resolved geometry: its document-space band top and height (internal
    /// pixel space, the same space all layout coordinates use) plus its four resolved page margins and
    /// its resolved physical sheet width/height, all in true PDF points (the space the paint loop's
    /// clip/translate and the margin-box renderer use). <see cref="SheetWidthPt"/>/
    /// <see cref="SheetHeightPt"/> are always populated - the base/configured sheet size for a slot no
    /// <c>@page</c> rule overrides, this slot's own resolved <c>size</c> otherwise.
    /// </summary>
    internal readonly record struct PageBandGeometry(
        int PageIndex,
        double Top,
        double BandHeight,
        double MarginLeftPt,
        double MarginTopPt,
        double MarginRightPt,
        double MarginBottomPt,
        double SheetWidthPt,
        double SheetHeightPt);

    /// <summary>
    /// The per-page geometry table behind CSS Paged Media's page-box model: when per-page
    /// <c>@page</c> rules (<c>:first</c>, <c>:left</c>/<c>:right</c>, named pages) override the top
    /// or bottom margins, each pagination slot gets its own content-band height and cumulative top —
    /// slot k+1 starts where slot k's band ends. Built forward-incrementally and lazily: slot k's
    /// applicable rule needs only the page number (known a priori for <c>:first</c>/<c>:left</c>/
    /// <c>:right</c>) and the named page active at the slot's START — and because a change of page
    /// name always forces a break onto a fresh page (<c>CssBox.PerformLayoutImp</c>), with the
    /// registration snapped to that page's slot top (<c>CssBox.NamedPageRegistrationY</c>) and made
    /// BEFORE the named box's children lay out, that name is fully determined by content laid out
    /// before the slot, so no fixpoint relayout is ever needed. A
    /// named-page registration only invalidates cached slots at/after its own Y
    /// (<see cref="InvalidateFrom"/>); boxes that consumed those entries are inside those slots and
    /// lay out at/after the registering box.
    /// All values scale per the issue-#113 discipline: margins resolve in true points, then scale by
    /// <c>PixelsPerPoint</c> exactly once into layout space.
    /// </summary>
    internal sealed class PageGeometryTable(HtmlContainerInt container)
    {
        private readonly List<PageBandGeometry> _pages = [];
        private bool? _hasVerticalOverrides;
        private bool? _hasHorizontalOverrides;
        private bool? _hasSizeOverrides;

        /// <summary>
        /// Whether any selector-carrying <c>@page</c> rule declares a top or bottom margin — the only
        /// overrides that vary band geometry (left/right overrides shift paint horizontally but never
        /// change pagination). When false, <see cref="HtmlContainerInt"/>'s grid helpers stay on the
        /// closed-form uniform arithmetic and this table is never consulted for geometry.
        /// </summary>
        internal bool HasVerticalMarginOverrides
        {
            get
            {
                _hasVerticalOverrides ??= ComputeHasVerticalOverrides();
                return _hasVerticalOverrides.Value;
            }
        }

        private bool ComputeHasVerticalOverrides()
        {
            foreach (var rule in container.PageRules)
            {
                if (rule.Selector is null) continue;
                if (rule.Style.MarginTop.Length > 0 || rule.Style.MarginBottom.Length > 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether any selector-carrying <c>@page</c> rule declares a left or right margin — the
        /// horizontal analogue of <see cref="HasVerticalMarginOverrides"/>. When true, content laid out
        /// on a page whose left/right margins differ from the base is re-wrapped to that page's own
        /// content-box width (CSS Paged Media 3: "the edges of the page area act as a containing block
        /// for layout that occurs between page breaks"), rather than merely shifted/clipped at paint.
        /// When false, <see cref="HtmlContainerInt"/> keeps the exact historical single-width layout.
        /// </summary>
        internal bool HasHorizontalMarginOverrides
        {
            get
            {
                _hasHorizontalOverrides ??= ComputeHasHorizontalOverrides();
                return _hasHorizontalOverrides.Value;
            }
        }

        private bool ComputeHasHorizontalOverrides()
        {
            foreach (var rule in container.PageRules)
            {
                if (rule.Selector is null) continue;
                if (rule.Style.MarginLeft.Length > 0 || rule.Style.MarginRight.Length > 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether any selector-carrying <c>@page</c> rule declares a physical <c>size</c> - when true,
        /// a pagination slot's sheet width/height can differ from the document's base/configured size
        /// (e.g. a named page in a different orientation), so <see cref="Compute"/> resolves it per
        /// slot via <see cref="PageRuleResolver.ResolvePageSize"/> instead of the cheap global
        /// reconstruction. When false (the overwhelming majority of documents), sheet size stays the
        /// single base value for every slot and no per-slot resolution runs at all.
        /// </summary>
        internal bool HasSizeOverrides
        {
            get
            {
                _hasSizeOverrides ??= ComputeHasSizeOverrides();
                return _hasSizeOverrides.Value;
            }
        }

        private bool ComputeHasSizeOverrides()
        {
            foreach (var rule in container.PageRules)
            {
                if (rule.Selector is null) continue;
                if (rule.Style.Size.Length > 0)
                    return true;
            }

            return false;
        }

        /// <summary>Drops every cached slot and re-evaluates the override scan — called at the start
        /// of every layout pass (and via <c>Clear</c>/<c>SetHtml</c>) so a fresh pass never sees the
        /// previous pass's geometry.</summary>
        internal void Reset()
        {
            _pages.Clear();
            _hasVerticalOverrides = null;
            _hasHorizontalOverrides = null;
            _hasSizeOverrides = null;
        }

        /// <summary>
        /// Truncates cached slots whose band top is at/after <paramref name="y"/> — called when a
        /// named-page element registers (or moves) at that Y, since only those slots' rule selection
        /// could see the new name.
        /// </summary>
        internal void InvalidateFrom(double y)
        {
            for (var k = _pages.Count - 1; k >= 0; k--)
            {
                if (_pages[k].Top >= y - HtmlContainerInt.PageBoundaryEpsilon)
                    _pages.RemoveAt(k);
                else
                    break;
            }
        }

        internal PageBandGeometry GetPage(int pageIndex)
        {
            var index = Math.Max(pageIndex, 0);
            ExtendTo(index);
            return _pages[index];
        }

        /// <summary>The slot whose band contains document Y <paramref name="y"/> (clamped to slot 0
        /// for anything above the first band's top).</summary>
        internal int PageIndexOf(double y)
        {
            ExtendTo(0);

            while (y >= _pages[^1].Top + _pages[^1].BandHeight)
                ExtendTo(_pages.Count);

            var lo = 0;
            var hi = _pages.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (y < _pages[mid].Top + _pages[mid].BandHeight)
                    hi = mid;
                else
                    lo = mid + 1;
            }

            return lo;
        }

        private void ExtendTo(int pageIndex)
        {
            while (_pages.Count <= pageIndex)
            {
                var k = _pages.Count;
                var top = k == 0
                    ? container.MarginTop // document space is anchored at the BASE content origin
                    : _pages[k - 1].Top + _pages[k - 1].BandHeight;
                _pages.Add(Compute(k, top));
            }
        }

        private PageBandGeometry Compute(int pageIndex, double top)
        {
            var ppp = (container.Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
            var baseLPt = container.MarginLeft / ppp;
            var baseTPt = container.MarginTop / ppp;
            var baseRPt = container.MarginRight / ppp;
            var baseBPt = container.MarginBottom / ppp;
            // The raw sheet width/height in layout px, recovered by construction: PdfGenerator.SetContent
            // subtracts both point-space margins from the point-space sheet, and the public wrappers
            // scale PageSize and margins by the same PixelsPerPoint.
            var baseSheetPxWidth = container.PageSize.Width + container.MarginLeft + container.MarginRight;
            var baseSheetPxHeight = container.PageSize.Height + container.MarginTop + container.MarginBottom;

            var activeName = PageRuleResolver.ActiveNameAtSlotStart(container.NamedPageElements, top);
            var rule = PageRuleResolver.SelectPageRule(container.PageRules, pageIndex + 1, activeName);
            var (mL, mT, mR, mB) = PageRuleResolver.ResolvePageMargins(
                rule, baseLPt, baseTPt, baseRPt, baseBPt, container.PageLengthContext);

            // Only resolve a per-slot sheet size when some rule in the document actually declares one -
            // the cheap, overwhelmingly common case (including every slot of a document that HAS a size
            // override elsewhere, but not on the rule that won here) stays the plain base reconstruction,
            // with no PageRuleResolver.ResolvePageSize call/allocation at all.
            double sheetPxHeight, sheetWidthPt, sheetHeightPt;
            if (HasSizeOverrides)
            {
                var baseSizePt = new XSize(baseSheetPxWidth / ppp, baseSheetPxHeight / ppp);
                var resolved = PageRuleResolver.ResolvePageSize(rule, baseSizePt, container.PageLengthContext);
                sheetWidthPt = resolved.Width;
                sheetHeightPt = resolved.Height;
                sheetPxHeight = resolved.Height * ppp;
            }
            else
            {
                sheetWidthPt = baseSheetPxWidth / ppp;
                sheetHeightPt = baseSheetPxHeight / ppp;
                sheetPxHeight = baseSheetPxHeight;
            }

            var bandHeight = sheetPxHeight - (mT + mB) * ppp;
            if (bandHeight < 1.0)
            {
                // Degenerate override (top+bottom margins consume the whole sheet): discard it for
                // band purposes and fall back to the base document margins against the SAME resolved
                // sheet (not just the base one - only the margins are discarded, per the class doc
                // comment), so the pagination walk always advances and paint/clip stay consistent with
                // the band actually used.
                mT = baseTPt;
                mB = baseBPt;
                bandHeight = sheetPxHeight - (mT + mB) * ppp;

                if (bandHeight < 1.0)
                {
                    // Still degenerate - the resolved sheet itself is smaller than even the base
                    // margins allow (only reachable once a per-slot size override can make the sheet
                    // arbitrarily small, e.g. a tiny named page). Discard margins entirely rather than
                    // let pagination stall.
                    mT = 0;
                    mB = 0;
                    bandHeight = sheetPxHeight;
                }
            }

            return new PageBandGeometry(pageIndex, top, bandHeight, mL, mT, mR, mB, sheetWidthPt, sheetHeightPt);
        }
    }
}
