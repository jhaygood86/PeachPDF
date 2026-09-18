using PeachPDF.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
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
    /// <c>@page</c> rule overrides, this slot's own resolved <c>size</c> otherwise. <see cref="BandWidth"/>
    /// is the horizontal analogue of <see cref="BandHeight"/> - this slot's own content-box width, in
    /// layout px - so <see cref="Dom.CssLayoutEngine.GetBoxHeight"/>'s existing pin of the initial
    /// containing block's height to <c>GetPage(0).BandHeight</c> has a width counterpart to pin to (issue
    /// #201) without duplicating <see cref="HtmlContainerInt.PageContentRightOf"/>'s degenerate-override
    /// fallback a second time. <see cref="ActiveName"/> is the named page active at this slot's own
    /// start (<c>null</c> for the un-named default) - exposed so <see cref="HtmlContainerInt.PageAssignmentSignature"/>
    /// can tell two slots with the same numeric index but different active geometry apart (issue #202).
    /// <see cref="BorderLeftPt"/>/<see cref="BorderTopPt"/>/<see cref="BorderRightPt"/>/
    /// <see cref="BorderBottomPt"/> and <see cref="PaddingLeftPt"/>/<see cref="PaddingTopPt"/>/
    /// <see cref="PaddingRightPt"/>/<see cref="PaddingBottomPt"/> are the page box's own resolved
    /// border/padding (css-page-3 §3's box model - closing issue #1147), in true PDF points, all
    /// zero for a page with no <c>@page</c> border/padding declared (the pre-#1147 default). They sit
    /// strictly INSIDE <see cref="MarginLeftPt"/>/etc - a page-margin box's own containing block
    /// (<see cref="Dom.MarginBoxRenderer.MarginAreaWidth"/>/<see cref="Dom.MarginBoxRenderer.MarginAreaHeight"/>)
    /// spans the margin-to-margin "available width"/height per css-page-3's own margin-box definition,
    /// which already equals the full border-box regardless of how it subdivides into border/padding/
    /// content - so margin-box geometry needs no change for this feature. <see cref="BandWidth"/>/
    /// <see cref="BandHeight"/> already have border+padding subtracted (in addition to margin) -
    /// everywhere that reads them for the actual content area needs no separate border/padding term of
    /// its own; a caller that needs the CONTENT BOX's own offset from the sheet edge (not just its
    /// size) uses <see cref="ContentLeftPt"/>/<see cref="ContentTopPt"/> below rather than re-summing
    /// margin + border + padding at each call site.
    /// </summary>
    internal readonly record struct PageBandGeometry(
        int PageIndex,
        double Top,
        double BandHeight,
        double BandWidth,
        double MarginLeftPt,
        double MarginTopPt,
        double MarginRightPt,
        double MarginBottomPt,
        double BorderLeftPt,
        double BorderTopPt,
        double BorderRightPt,
        double BorderBottomPt,
        double PaddingLeftPt,
        double PaddingTopPt,
        double PaddingRightPt,
        double PaddingBottomPt,
        double SheetWidthPt,
        double SheetHeightPt,
        string? ActiveName)
    {
        /// <summary>The content box's own left edge, as an offset in true PDF points from the sheet's
        /// own left edge - margin + border + padding (issue #1147). The single home for this sum, used
        /// everywhere a caller needs the content box's POSITION rather than just its size (which
        /// <see cref="BandWidth"/> already gives directly).</summary>
        internal double ContentLeftPt => MarginLeftPt + BorderLeftPt + PaddingLeftPt;

        /// <summary>The vertical analogue of <see cref="ContentLeftPt"/>.</summary>
        internal double ContentTopPt => MarginTopPt + BorderTopPt + PaddingTopPt;
    }

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
    /// before the slot for a SINGLE pass. A
    /// named-page registration only invalidates cached slots at/after its own Y
    /// (<see cref="InvalidateFrom"/>); boxes that consumed those entries are inside those slots and
    /// lay out at/after the registering box. What this does NOT rule out on its own: a named page whose
    /// L/R margin or <c>size</c> override spans several physical pages has a genuine
    /// width→height→page-name feedback (content width affects box height, which affects which page a
    /// later boundary falls on, which can affect which name is active there) that
    /// <see cref="HtmlContainerInt.PerformLayout"/>'s own bounded reflow loop, not this table, is
    /// responsible for driving to a fixpoint across MULTIPLE passes - see
    /// <see cref="HtmlContainerInt.PageAssignmentSignature"/> and
    /// <c>.claude/accepted-gaps/named-page-run-convergence-loop-is-bounded-not-guaranteed.md</c> (issue
    /// #202).
    /// All values scale per the issue-#113 discipline: margins resolve in true points, then scale by
    /// <c>PixelsPerPoint</c> exactly once into layout space.
    /// </summary>
    internal sealed class PageGeometryTable(HtmlContainerInt container)
    {
        private readonly List<PageBandGeometry> _pages = [];
        private bool? _hasVerticalOverrides;
        private bool? _hasHorizontalOverrides;
        private bool? _hasSizeOverrides;
        private bool? _hasVerticalBorderPaddingOverrides;
        private bool? _hasHorizontalBorderPaddingOverrides;

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
        /// (e.g. a named page in a different orientation), so <see cref="Compute(int, double)"/> resolves it per
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

        /// <summary>
        /// Whether ANY <c>@page</c> rule - including the selector-less base rule, unlike
        /// <see cref="HasVerticalMarginOverrides"/>/<see cref="HasHorizontalMarginOverrides"/> - declares
        /// a top or bottom border/padding (issue #1147). The base rule counts here because, unlike
        /// margin, there is no separate "baked into <see cref="HtmlContainerInt.MarginTop"/>" closed-form
        /// fast path for border/padding: a document with border/padding on ONLY the base rule still needs
        /// every slot resolved through <see cref="Compute(int, double)"/> to get a shrunk band, even
        /// though that resolution is uniform across every slot. <see cref="HtmlContainerInt.UseVariablePageGeometry"/>
        /// consults this alongside the existing vertical-margin/size flags.
        /// </summary>
        internal bool HasVerticalBorderPaddingOverrides
        {
            get
            {
                _hasVerticalBorderPaddingOverrides ??= ComputeHasBorderPaddingOverrides(vertical: true);
                return _hasVerticalBorderPaddingOverrides.Value;
            }
        }

        /// <summary>The horizontal analogue of <see cref="HasVerticalBorderPaddingOverrides"/>, consulted
        /// by <see cref="HtmlContainerInt.UseVariableInlineMeasure"/> alongside the existing
        /// horizontal-margin/size flags.</summary>
        internal bool HasHorizontalBorderPaddingOverrides
        {
            get
            {
                _hasHorizontalBorderPaddingOverrides ??= ComputeHasBorderPaddingOverrides(vertical: false);
                return _hasHorizontalBorderPaddingOverrides.Value;
            }
        }

        private bool ComputeHasBorderPaddingOverrides(bool vertical)
        {
            foreach (var rule in container.PageRules)
            {
                var s = rule.Style;
                var declared = vertical
                    ? s.BorderTopWidth.Length > 0 || s.BorderTopStyle.Length > 0 ||
                      s.BorderBottomWidth.Length > 0 || s.BorderBottomStyle.Length > 0 ||
                      s.PaddingTop.Length > 0 || s.PaddingBottom.Length > 0
                    : s.BorderLeftWidth.Length > 0 || s.BorderLeftStyle.Length > 0 ||
                      s.BorderRightWidth.Length > 0 || s.BorderRightStyle.Length > 0 ||
                      s.PaddingLeft.Length > 0 || s.PaddingRight.Length > 0;

                if (declared) return true;
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
            _hasVerticalBorderPaddingOverrides = null;
            _hasHorizontalBorderPaddingOverrides = null;
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

        /// <summary>
        /// Recomputes slot <paramref name="slotIndex"/>'s geometry as if its own <c>:first</c>/
        /// <c>:left</c>/<c>:right</c>-facing page number were <paramref name="materializedPageNumber"/>
        /// — the position this slot's fragmentainer actually lands at in the final, content-empty-
        /// skipped page sequence (issue #148) — instead of its raw grid slot number
        /// (<c>slotIndex + 1</c>, what <see cref="Compute(int, double)"/> used during layout). Returns
        /// <c>null</c> — meaning "nothing to correct, the caller's own already-known geometry is
        /// fine" — both when the two numbers agree (the overwhelmingly common case: no content-empty
        /// slot has been skipped before this one) and when no <c>@page</c> rule in the document could
        /// possibly vary by page number at all (<see cref="HasVerticalMarginOverrides"/>/
        /// <see cref="HasHorizontalMarginOverrides"/>/<see cref="HasSizeOverrides"/> all false — a
        /// cheap check that avoids running <see cref="Compute(int, double, int)"/> a second time only
        /// to reproduce what the caller already has). Also returns <c>null</c> when the numbers
        /// disagree AND substituting would change this slot's own
        /// <see cref="PageBandGeometry.BandWidth"/>/<see cref="PageBandGeometry.BandHeight"/>: content
        /// already laid out into this slot was wrapped/fragmented against the GRID-numbered dimensions
        /// (<see cref="HtmlContainerInt.PageContentRightOf"/>/<see cref="HtmlContainerInt.PageBandHeightOf"/>
        /// both key off <see cref="GetPage"/>(<paramref name="slotIndex"/>) during layout, before any
        /// materialized number is knowable), so a dimensionally-different substitution would offset or
        /// size the printed page differently from what its content actually is — unsafe. The
        /// overwhelmingly common case where a genuine substitution IS safe is a mirrored
        /// binding-gutter <c>:left</c>/<c>:right</c> pair (equal total left+right margin, only the
        /// split differs) — see <see cref="HtmlContainerInt.MeasureIsSharedBetween"/>'s own remarks.
        /// Called once per page, from <see cref="HtmlContainerInt.LayoutMarginBoxes"/>, which bakes a
        /// non-null result back into the fragment tree — everything downstream (paint, links,
        /// bookmarks, form fields) reads <see cref="Fragments.FragmentainerFragment.Geometry"/>
        /// directly rather than calling this again, keeping the fragment tree the one place this is
        /// decided. Only call this once the final page sequence is known — never during layout itself,
        /// which doesn't know it yet.
        /// </summary>
        internal PageBandGeometry? ResolveForMaterializedPage(int slotIndex, int materializedPageNumber)
        {
            if (materializedPageNumber == slotIndex + 1) return null;
            if (!HasVerticalMarginOverrides && !HasHorizontalMarginOverrides && !HasSizeOverrides &&
                !HasVerticalBorderPaddingOverrides && !HasHorizontalBorderPaddingOverrides) return null;

            var slotGeometry = GetPage(slotIndex);
            var candidate = Compute(slotIndex, slotGeometry.Top, materializedPageNumber);
            return Math.Abs(candidate.BandWidth - slotGeometry.BandWidth) < 0.01
                && Math.Abs(candidate.BandHeight - slotGeometry.BandHeight) < 0.01
                ? candidate
                : null;
        }

        /// <summary>
        /// Issue #1041's narrow residual on top of <see cref="ResolveForMaterializedPage"/>: probes
        /// whether slot <paramref name="slotIndex"/> is eligible for a relayout-gated correction in the
        /// one case that method itself always declines - substituting the materialized-number geometry
        /// would change <see cref="PageBandGeometry.BandWidth"/> OR <see cref="PageBandGeometry.BandHeight"/>
        /// (not both). Both changing at once stays out of scope entirely (returns <c>null</c>): nothing
        /// here distinguishes "a relayout would fix this" from "a relayout would fix this along some
        /// dimension but not verifiably both", and the caller (<see cref="HtmlContainerInt.TryApplyDimensionChangingPageCorrection"/>)
        /// has no narrower way to validate a two-dimension change than it already does for one. Also
        /// declines whenever this slot (grid-numbered or materialized-numbered) sits under an active
        /// named page (<see cref="PageBandGeometry.ActiveName"/> non-null on either side) - a named-page
        /// run already drives its own bounded-not-guaranteed convergence loop
        /// (<see cref="HtmlContainerInt.PageAssignmentSignature"/>,
        /// <c>.claude/accepted-gaps/named-page-run-convergence-loop-is-bounded-not-guaranteed.md</c>);
        /// stacking a second, independent relayout mechanism on top of that one is materially riskier
        /// than either alone and outside this issue's own repro shape (which is about <c>:first</c>/
        /// <c>:left</c>/<c>:right</c>, not named pages). There is no separate "flagged fragile named-page
        /// run" registry to consult beyond that <c>ActiveName</c> check - this IS how a named-page run is
        /// told apart from the base grid today.
        /// Pure/speculative like <see cref="ResolveForMaterializedPage"/> - does not touch <see cref="_pages"/>
        /// and is safe to call before deciding whether to actually run the extra layout pass.
        /// </summary>
        internal PageBandGeometry? ProbeDimensionChangingCorrection(int slotIndex, int materializedPageNumber)
        {
            if (materializedPageNumber == slotIndex + 1) return null;
            if (!HasVerticalMarginOverrides && !HasHorizontalMarginOverrides && !HasSizeOverrides &&
                !HasVerticalBorderPaddingOverrides && !HasHorizontalBorderPaddingOverrides) return null;

            var slotGeometry = GetPage(slotIndex);
            if (slotGeometry.ActiveName is not null) return null;

            var candidate = Compute(slotIndex, slotGeometry.Top, materializedPageNumber);
            if (candidate.ActiveName is not null) return null;

            var widthChanged = Math.Abs(candidate.BandWidth - slotGeometry.BandWidth) >= 0.01;
            var heightChanged = Math.Abs(candidate.BandHeight - slotGeometry.BandHeight) >= 0.01;

            // Exactly one, not zero (ResolveForMaterializedPage already handles that) and not both
            // (still out of scope - see this method's own remarks).
            return widthChanged ^ heightChanged ? candidate : null;
        }

        /// <summary>
        /// Slot-index -> materialized-page-number overrides driving <see cref="HtmlContainerInt.TryApplyDimensionChangingPageCorrection"/>'s
        /// one extra, speculative layout pass (issue #1041): while set, <see cref="Compute(int, double)"/>
        /// resolves a slot present as a key here against that materialized number rather than its own
        /// raw grid number (<c>pageIndex + 1</c>), so content laid out during that one pass actually
        /// wraps/fragments against the corrected dimensions instead of merely having them baked in
        /// after the fact. Deliberately NOT cleared by <see cref="Reset"/> - <c>Reset</c> runs at the
        /// START of the very pass this exists to steer (<see cref="HtmlContainerInt"/>'s
        /// <c>LayoutDocument</c>), so clearing it there would defeat the whole mechanism. The caller sets
        /// this immediately before that one pass and clears it immediately after, in a <c>finally</c>,
        /// regardless of outcome - every other layout pass in the document (including the fallback
        /// restore pass <c>TryApplyDimensionChangingPageCorrection</c> runs when the corrected pass
        /// doesn't validate) must see this <c>null</c> and resolve every slot against its plain grid
        /// number exactly as before this feature existed.
        /// </summary>
        internal Dictionary<int, int>? MaterializedNumberOverrides { get; set; }

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

        private PageBandGeometry Compute(int pageIndex, double top) => Compute(
            pageIndex, top,
            MaterializedNumberOverrides is { } overrides && overrides.TryGetValue(pageIndex, out var materialized)
                ? materialized
                : pageIndex + 1);

        /// <summary>
        /// <paramref name="ruleSelectionPageNumber"/> is the number fed to
        /// <see cref="PageRuleResolver.SelectPageRule"/> for <c>:first</c>/<c>:left</c>/<c>:right</c>
        /// selection — ordinarily <paramref name="pageIndex"/> + 1 (the raw grid slot number, via the
        /// single-argument overload above), but overridden by <see cref="ResolveForMaterializedPage"/>
        /// to recompute this same slot's geometry against a different (materialized) page number
        /// without touching <see cref="_pages"/>. Pure function of its arguments - safe to call
        /// speculatively.
        /// </summary>
        private PageBandGeometry Compute(int pageIndex, double top, int ruleSelectionPageNumber)
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
            var rule = PageRuleResolver.SelectPageRule(container.PageRules, ruleSelectionPageNumber, activeName);
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

            // The page box's own border/padding (issue #1147), resolved against the per-declaration-
            // merged page style (not SelectPageRule's single-winner `rule` above - see
            // PageRuleResolver.ResolvePageBorderAndPadding's own remarks on why border/padding, as new
            // properties, don't inherit margin/size's older, less spec-accurate single-rule selection),
            // and against THIS slot's own resolved sheet size (sheetWidthPt/sheetHeightPt just above) -
            // a named page whose own `size` differs must resolve its own padding percentages against its
            // own sheet, not the document's base one. Skipped entirely (staying the all-zero default -
            // exactly the pre-#1147 "no border/padding" behavior) when neither override flag is set, so
            // a border/padding-free document never pays for SelectApplicablePageStyle's own rule scan.
            var borderPadding = HasVerticalBorderPaddingOverrides || HasHorizontalBorderPaddingOverrides
                ? PageRuleResolver.ResolvePageBorderAndPadding(
                    PageRuleResolver.SelectApplicablePageStyle(container.PageRules, ruleSelectionPageNumber, activeName),
                    sheetWidthPt, sheetHeightPt,
                    container.PageLengthContext?.RemPt ?? DefaultFontResolver.FontSize)
                : default;

            var (bL, bT, bR, bB) = (borderPadding.BorderLeftPt, borderPadding.BorderTopPt, borderPadding.BorderRightPt, borderPadding.BorderBottomPt);
            var (pL, pT, pR, pB) = (borderPadding.PaddingLeftPt, borderPadding.PaddingTopPt, borderPadding.PaddingRightPt, borderPadding.PaddingBottomPt);

            var bandHeight = sheetPxHeight - (mT + mB + bT + bB + pT + pB) * ppp;
            if (bandHeight < 1.0)
            {
                // Degenerate override (top+bottom margin/border/padding together consume the whole
                // sheet): discard the whole vertical contribution - margin AND border/padding alike -
                // and fall back to the base document margins with no border/padding, against the SAME
                // resolved sheet (not just the base one), so the pagination walk always advances and
                // paint/clip stay consistent with the band actually used. Border/padding are discarded
                // here (not just margin, as before #1147) because painting a border/reserving padding
                // this slot's own band no longer has room for would draw over content instead of beside
                // it.
                mT = baseTPt;
                mB = baseBPt;
                bT = bB = pT = pB = 0;
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

            // The horizontal mirror of the bandHeight computation/fallback above - kept in sync with
            // HtmlContainerInt.PageContentRightOf's own (pre-existing) inline version of this same
            // arithmetic, which still owns the MarginLeft-relative "content-right edge" framing that
            // callers outside this table need; this field exists so a caller that only wants the slot's
            // own WIDTH (not an edge relative to the base left origin) doesn't have to re-derive it.
            // Uses local fallback margins rather than reassigning mL/mR - a degenerate left/right
            // override still reports its OWN resolved MarginLeftPt/MarginRightPt (e.g. for margin-box
            // painting), exactly as before this field existed; only the derived band width falls back.
            // Border/padding, unlike margin, ARE reset to zero on the degenerate fallback below (see the
            // vertical fallback's own remarks on why) - MarginLeftPt/MarginRightPt keep this asymmetry
            // with BorderLeftPt/etc for the same reason they always have with mL/mR themselves.
            var sheetPxWidth = sheetWidthPt * ppp;
            var bandWidth = sheetPxWidth - (mL + mR + bL + bR + pL + pR) * ppp;
            if (bandWidth < 1.0)
            {
                bandWidth = sheetPxWidth - (baseLPt + baseRPt) * ppp;
                bL = bR = pL = pR = 0;

                if (bandWidth < 1.0)
                    bandWidth = sheetPxWidth;
            }

            return new PageBandGeometry(
                pageIndex, top, bandHeight, bandWidth,
                mL, mT, mR, mB,
                bL, bT, bR, bB,
                pL, pT, pR, pB,
                sheetWidthPt, sheetHeightPt, activeName);
        }
    }
}
