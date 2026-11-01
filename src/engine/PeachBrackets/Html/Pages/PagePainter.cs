using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Entities;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Paint;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;
using PeachBrackets.Utilities;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Html.Pages
{
    /// <summary>A canvas for one stage of painting a page, and what to do with it afterwards (a PDF page hands out a fresh canvas per stage; a bitmap reuses one).</summary>
    internal readonly struct CanvasLease : IDisposable
    {
        private readonly Canvas _canvas;
        private readonly Action<Canvas>? _release;

        public CanvasLease(Canvas canvas, Action<Canvas>? release = null)
        {
            _canvas = canvas;
            _release = release;
        }

        public static implicit operator Canvas(CanvasLease lease) => lease._canvas;

        public void Dispose() => _release?.Invoke(_canvas);
    }

    /// <summary>
    /// Paints a laid-out document one page at a time onto any <see cref="Canvas"/>: the <c>@page</c> box's background and border, the document
    /// canvas background, the page's content, its footnote areas, and its margin boxes - the layers css-page-3 &#167;3.1 orders. It knows nothing about
    /// what the canvas writes; the caller supplies a canvas per stage and a way to move the content origin.
    /// </summary>
    internal sealed class PagePainter : IDisposable
    {
        private readonly HtmlContainerInt _container;
        private readonly RenderContext _renderContext;
        private readonly CssBox? _canvasBackgroundBox;

        // Margin-box `content: url(...)` images are cached by declaration text across the whole document, since the same margin rule - and so
        // the same image - repeats identically on every page; without this a multi-hundred-page document would re-decode (or re-fetch) the same
        // logo once per page. @page box background image layers and margin-box background image layers share the same shape and rationale.
        private readonly Dictionary<string, CssImage?> marginBoxImageCache = new();
        private readonly Dictionary<string, IReadOnlyList<CssImage?>?> pageBackgroundImageCache = new();
        private readonly Dictionary<string, IReadOnlyList<CssImage?>?> marginBoxBackgroundImageCache = new();

        private Func<CanvasLease> _lease = null!;
        private Func<double, double, IDisposable?> _shift = null!;

        public PagePainter(HtmlContainerInt container, RenderContext renderContext)
        {
            _container = container;
            _renderContext = renderContext;

            // Per CSS2.1 &#167;14.2 the "canvas" (here: every page) is filled with body's background if it declares one, else html's. Resolved
            // during layout, since the fragment tree's own page-materialization rule depends on it.
            _canvasBackgroundBox = container.CanvasBackgroundBox;
        }

        /// <summary>The pages layout produced, in order: one per fragmentainer.</summary>
        public IReadOnlyList<FragmentainerFragment> Fragmentainers => _container.FragmentTree?.Fragmentainers ?? [];

        /// <summary>
        /// Paints one page. <paramref name="lease"/> supplies a canvas for each stage; <paramref name="shift"/> moves everything painted
        /// afterwards by (x, y) points until the returned scope is disposed (<see langword="null"/> when there is nothing to undo).
        /// </summary>
        public async Task PaintPageAsync(
            FragmentainerFragment fragmentainer, int pageNumber, Func<CanvasLease> lease, Func<double, double, IDisposable?> shift)
        {
            _lease = lease;
            _shift = shift;
            var totalPages = Fragmentainers.Count;
            var canvasBackgroundBox = _canvasBackgroundBox;

            // The single source of truth for this slot's margins and band: the same geometry
            // table layout paginated against, carried on the fragmentainer itself (already
            // corrected against this slot's materialized page number, when a content-empty gap
            // skipped earlier made that differ from its raw grid number - see
            // HtmlContainerInt.LayoutMarginBoxes and issue #148), so paint can never disagree with
            // layout about a page's content band. Margins come out in true points (the space the
            // clip/translate below and MarginBoxRenderer use); Top/BandHeight are internal-pixel
            // document space (the space NamedPageElement Ys live in), which also fixes the
            // historical ShrinkToFit drift where pageY mixed a pixel-space slot top with a
            // point-space MarginTop for named-page attribution.
            var geom = fragmentainer.Geometry;
            var pageY = geom.Top;
            var isBlankPage = _container.IsReservedBlankSlot(fragmentainer.SlotIndex);
            var applicableMargins = SelectApplicableMarginRules(
                _container.PageRules,
                pageNumber,
                _container.NamedPageElements,
                pageY,
                geom.BandHeight,
                isBlankPage);
            var applicablePageStyle = SelectApplicablePageStyle(
                _container.PageRules,
                pageNumber,
                _container.NamedPageElements,
                pageY,
                geom.BandHeight,
                isBlankPage);

            var (mL, mT, mR, mB) = (geom.MarginLeftPt, geom.MarginTopPt, geom.MarginRightPt, geom.MarginBottomPt);
            // The page box's own resolved border/padding (issue #1147) - all zero for a page with no
            // @page border/padding declared, which is exactly the pre-#1147 behavior.
            var (bL, bT, bR, bB) = (geom.BorderLeftPt, geom.BorderTopPt, geom.BorderRightPt, geom.BorderBottomPt);
            var (pL, pT, pR, pB) = (geom.PaddingLeftPt, geom.PaddingTopPt, geom.PaddingRightPt, geom.PaddingBottomPt);

            var sheetRect = new Rect(0, 0,
                geom.SheetWidthPt * _renderContext.LayoutUnitsPerPoint, geom.SheetHeightPt * _renderContext.LayoutUnitsPerPoint);

            // The page box's own border-box/padding-box/content-box rects (css-page-3 §3's box
            // model), resolved in true PDF points then converted to px - the same border-box ->
            // padding-box -> content-box shrink chain MarginBoxRenderer.PaintBackgroundAndBorder
            // already uses for a page-MARGIN-box's own background/border, reused here via the same
            // MarginBoxRenderer.Shrink primitive rather than re-deriving the arithmetic (issue
            // #1147). border-box is the sheet minus this page's own margin only - background/border
            // never extend into the margin area (margin never participates in background painting
            // for ANY CSS box), unlike the canvas fill below, which deliberately does.
            var sheetRectPt = new Rect(0, 0, geom.SheetWidthPt, geom.SheetHeightPt);
            var borderBoxRectPt = MarginBoxRenderer.Shrink(sheetRectPt, mL, mT, mR, mB);
            var paddingBoxRectPt = MarginBoxRenderer.Shrink(borderBoxRectPt, bL, bT, bR, bB);
            var contentBoxRectPt = MarginBoxRenderer.Shrink(paddingBoxRectPt, pL, pT, pR, pB);

            var borderBoxRect = PeachBrackets.Utilities.Utils.ToPixels(borderBoxRectPt, _renderContext.LayoutUnitsPerPoint);
            var paddingBoxRect = PeachBrackets.Utilities.Utils.ToPixels(paddingBoxRectPt, _renderContext.LayoutUnitsPerPoint);
            var contentBoxRect = PeachBrackets.Utilities.Utils.ToPixels(contentBoxRectPt, _renderContext.LayoutUnitsPerPoint);

            Rect ResolvePagePositioningRect(string value) => value switch
            {
                Keywords.ContentBox => contentBoxRect,
                Keywords.BorderBox => borderBoxRect,
                // padding-box is the initial value (CSS Backgrounds 3 §3.9/§3.10) and what an
                // unrecognized/empty value falls back to - matching MarginBoxRenderer's own
                // ResolvePositioningRect for a page-margin box's background.
                _ => paddingBoxRect,
            };

            // Same "no real inheritance chain" em-basis convention MarginBoxRenderer.ResolveFontSizePt's
            // own StyleDeclaration overload uses - falls back to DefaultFontResolver.FontSize, never
            // null, so a gradient/border-width with no @page font-size resolves against the same
            // page-box-appropriate basis a margin-box's own would, not the document root's own
            // (possibly quite different) actual font-size. Shared between the page background
            // (gradient em-sizing) and the page border (below) so both agree on the same basis.
            var pageFontSizeStr = applicablePageStyle?.FontSize;
            var pageEmSizePt = string.IsNullOrEmpty(pageFontSizeStr)
                ? DefaultFontResolver.FontSize
                : MarginBoxRenderer.ResolveFontSizePt(pageFontSizeStr);
            var pageRemPt = _container.PageLengthContext?.RemPt ?? DefaultFontResolver.FontSize;

            // css-page-3 §3.1's page layer paint order is (bottommost first): page background,
            // document canvas, page borders, document contents, page-margin boxes. This paints the
            // @page box's own background - the bottommost layer - one step before the canvas fill
            // below, so an opaque canvas background naturally occludes it (and a transparent/absent
            // one lets it show through) with no extra precedence logic needed. background-origin/
            // -clip now (issue #1147) genuinely distinguish border-box/padding-box/content-box via
            // this page's own resolved border/padding, mirroring how a page-margin box's own
            // background already does.
            if (_container.Root is { } rootBoxForPageBackground && applicablePageStyle is not null)
            {
                using var pageBackgroundGraphics = Lease();

                await LayeredBackgroundPainter.PaintAsync(
                    pageBackgroundGraphics, applicablePageStyle, _renderContext, _container,
                    rootBoxForPageBackground, pageEmSizePt,
                    resolvePositioningRect: ResolvePagePositioningRect,
                    viewportRect: sheetRect,
                    pageBackgroundImageCache,
                    currentColor: MarginBoxRenderer.ResolveColor(applicablePageStyle, null, _container, _renderContext));
            }

            if (canvasBackgroundBox != null)
            {
                // Must paint before the content clip below is applied (page.304's IntersectClip),
                // so the fill reaches the true full page bleed (including the margin-box area), not
                // just the content rect. The canvas fill is a distinct paint step from the page box's
                // own background above - CSS2.1 §14.2's canvas propagation always fills the whole
                // sheet regardless of the page box's own border/padding, exactly as before #1147.
                using var canvasGraphics = Lease();
                FragmentPainter.PaintCanvasBackground(canvasGraphics, canvasBackgroundBox, sheetRect);
            }

            // css-page-3 §3.1's third paint layer: the page box's OWN border (issue #1147) - a
            // distinct layer from a page-MARGIN-box's own border (MarginBoxRenderer.PaintBorder,
            // painted later alongside the rest of that margin box's own content), sitting between
            // the document canvas and the document's own content. Painted at borderBoxRect (the
            // sheet minus this page's own margin only) using the same non-mitred four-independent-
            // edge approach a margin box's border already uses (PaintBorder itself) - a physical page
            // never fragments/shares a corner with a neighboring page either.
            if (applicablePageStyle is not null && (bL > 0 || bT > 0 || bR > 0 || bB > 0))
            {
                using var pageBorderGraphics = Lease();
                MarginBoxRenderer.PaintBorder(
                    pageBorderGraphics, borderBoxRect, applicablePageStyle,
                    pageEmSizePt, pageRemPt, _renderContext.LayoutUnitsPerPoint, _renderContext,
                    MarginBoxRenderer.ResolveColor(applicablePageStyle, null, _container, _renderContext));
            }

            // Save state so the content transform can be undone for margin box rendering

            // No content-area clip pushed here (an earlier version of this method intersected one
            // directly on the raw PdfGraphics, ahead of FragmentPainter.Paint's own push below) - a raw
            // PdfGraphics.IntersectClip call made here is invisible to the Canvas abstraction's own
            // clip-stack bookkeeping, which is what Canvas.SuspendClipping walks to let a
            // position: fixed box's own paint reach back out to the full page box (margins included,
            // its actual containing block per CSS2.1 §10.1). Baking the content clip in here put it
            // permanently out of that call's reach, silently discarding every fixed box's own geometry
            // that legitimately fell inside the page's margins. FragmentPainter.Paint pushes the
            // equivalent content-area clip itself now (HtmlContainerInt.PageClipOverride, set below) -
            // PaintFootnoteArea, which paints outside that wrapper, pushes its own.

            // Content is anchored at the base MarginLeft/MarginTop in layout space; this delta maps
            // that anchor onto THIS page's own physical content-box origin - margin, and (since
            // issue #1147) the page box's own border/padding too, all three of which sit between the
            // sheet edge and the content that pagination itself already reserved this room for
            // (geom.BandWidth/BandHeight).
            var deltaX = mL + bL + pL - (_container.MarginLeft / _renderContext.LayoutUnitsPerPoint);
            var deltaY = mT + bT + pT - (_container.MarginTop / _renderContext.LayoutUnitsPerPoint);
            var contentShift = deltaX != 0 || deltaY != 0 ? _shift(deltaX, deltaY) : null;

            // Same-units (internal pixel space) generalization of PageBoxRect's default paint
            // window: x/y adapt to this page's margin override via the delta translate above;
            // the width pins the window's right edge to the physical paper edge (identical to
            // PageBoxRect's PageSize.Width + MarginRight whenever mL equals the base left
            // margin, so non-overridden pages are unchanged). The height is this slot's own
            // content band from the geometry table — pagination itself ran on the same variable
            // bands, so a margin-0 page's window reclaims the full sheet height without ever
            // exposing a neighboring slot's content.
            _container.PageClipOverride = new Rect(
                _container.MarginLeft,
                _container.MarginTop,
                (geom.SheetWidthPt - mL) * _renderContext.LayoutUnitsPerPoint,
                geom.BandHeight);

            using (var contentCanvas = Lease())
            {
                _container.PerformPaint(contentCanvas, fragmentainer);
            }

            // The footnote area's own geometry (AttachFootnoteAreas) is built the same
            // fragmentainer-local way every other content fragment's is - content stays anchored at
            // the base MarginLeft/MarginTop in layout space, with this page's own deltaX/deltaY
            // translate (above) the only thing that maps it onto a page whose own @page margins
            // differ from the base. It must therefore paint here, inside that transform, alongside
            // ordinary content - not after the restore below, which is what a true page-absolute rect
            // (a plain string/counter/element() margin box's own rect, computed directly from this
            // page's own margins) needs instead.
            // A page can carry more than one: its own note area, plus one per column any
            // float-reference: column call landed in. Each already knows its own rect, so nothing
            // here needs to distinguish them.
            foreach (var footnoteArea in fragmentainer.FootnoteAreas ?? [])
            {
                PaintFootnoteArea(Lease(), _renderContext, _container, footnoteArea);
            }

            // Restore to pre-content state so margin boxes render in absolute page coordinates
            contentShift?.Dispose();

            if (applicableMargins.Count > 0)
            {
                using var marginBoxCanvas = Lease();
                await MarginBoxRenderer.Render(
                    marginBoxCanvas,
                    new Size(geom.SheetWidthPt, geom.SheetHeightPt),
                    mL,
                    mT,
                    mR,
                    mB,
                    applicableMargins,
                    pageNumber + _container.PageNumberOffset,
                    _container.TotalPagesOverride ?? totalPages,
                    pageY,
                    _container.NamedStrings,
                    _renderContext,
                    applicablePageStyle,
                    _container,
                    marginBoxImageCache,
                    marginBoxBackgroundImageCache);
            }

            // A rule can be content:element(...) with no matching position:running(...) anywhere (yet)
            // - HtmlContainerInt.LayoutMarginBoxes then creates no MarginBoxFragment for it, but its
            // own background/border must still paint (PaintElementMarginBoxes' own second loop), so
            // this can't gate on fragmentainer.MarginBoxes alone.
            var hasElementMarginBoxes = fragmentainer.MarginBoxes.Count > 0 || applicableMargins.Any(m =>
                !string.IsNullOrEmpty(m.Style.Content) && MarginBoxRenderer.TryParseElementFunction(m.Style.Content, out _, out _));
            if (hasElementMarginBoxes)
            {
                await PaintElementMarginBoxes(Lease(), _renderContext, _container, fragmentainer.MarginBoxes,
                    new Size(geom.SheetWidthPt, geom.SheetHeightPt), mL, mT, mR, mB, applicableMargins, applicablePageStyle,
                    marginBoxBackgroundImageCache);
            }

            // The page's content is complete: write it now rather than holding every page's drawing state until the document is saved.

        }

        private CanvasLease Lease() => _lease();

        private static void PaintFootnoteArea(CanvasLease lease, RenderContext renderContext, HtmlContainerInt htmlContainer, FootnoteAreaFragment footnoteArea)
        {
            using var pdfLease = lease;
            PaintFootnoteArea((Canvas)pdfLease, renderContext, htmlContainer, footnoteArea);
        }

        public void Dispose()
        {
            foreach (var cachedImage in pageBackgroundImageCache.Values)
            {
                if (cachedImage is null) continue;
                foreach (var image in cachedImage) image?.Dispose();
            }

            foreach (var cachedImage in marginBoxBackgroundImageCache.Values)
            {
                if (cachedImage is null) continue;
                foreach (var image in cachedImage) image?.Dispose();
            }

            foreach (var cachedImage in marginBoxImageCache.Values)
                cachedImage?.Dispose();
        }

        internal static PageRule? SelectPageRule(
            IReadOnlyList<PageRule> rules,
            int pageNumber,
            IReadOnlyList<NamedPageElement> namedPageElements,
            double pageY,
            double pageHeight,
            bool isBlank = false)
            => PageRuleResolver.SelectPageRule(rules, pageNumber,
                PageRuleResolver.ActiveNameAtPageEnd(namedPageElements, pageY, pageHeight), isBlank);

        internal static IReadOnlyList<MarginStyleRule> SelectApplicableMarginRules(
            IReadOnlyList<PageRule> rules,
            int pageNumber,
            IReadOnlyList<NamedPageElement> namedPageElements,
            double pageY,
            double pageHeight,
            bool isBlank = false)
            => PageRuleResolver.SelectApplicableMarginRules(rules, pageNumber,
                PageRuleResolver.ActiveNameAtPageEnd(namedPageElements, pageY, pageHeight), isBlank);

        internal static StyleDeclaration? SelectApplicablePageStyle(
            IReadOnlyList<PageRule> rules,
            int pageNumber,
            IReadOnlyList<NamedPageElement> namedPageElements,
            double pageY,
            double pageHeight,
            bool isBlank = false)
            => PageRuleResolver.SelectApplicablePageStyle(rules, pageNumber,
                PageRuleResolver.ActiveNameAtPageEnd(namedPageElements, pageY, pageHeight), isBlank);

        internal static (double L, double T, double R, double B) ResolvePageMargins(
            PageRule? rule, double baseL, double baseT, double baseR, double baseB)
            => PageRuleResolver.ResolvePageMargins(rule, baseL, baseT, baseR, baseB);

        /// <summary>
        /// Paints one page's css-gcpm-3 <c>content: element()</c> margin-box content - real, laid-out
        /// <see cref="CssBox"/> subtrees (<see cref="FragmentainerFragment.MarginBoxes"/>), unlike the
        /// plain string/counter/image content <see cref="MarginBoxRenderer.Render"/> still draws directly.
        /// Reuses <see cref="FragmentPainter.PaintFragment"/>'s paint through <see cref="FragmentPainter.PaintDetached"/>
        /// (the tree is not part of the page's own, so a decoration needs it as the root to find where its
        /// line was truncated) - the same paint every other <see cref="BoxFragment"/> in the document goes through - so real formatting/descendant
        /// elements (backgrounds, borders, nested inline styling) paint correctly for free, and (per
        /// <c>FragmentPainter.PaintFragment</c>'s own doc comment, "the single choke point all tagging
        /// flows through") so tagged-PDF structure attaches automatically when enabled, keyed to whichever
        /// page's <c>structureTagBuilder.BeginPage</c> is currently open - the precondition this call site,
        /// immediately after the same page's main-content paint, satisfies.
        /// </summary>
        /// <remarks>
        /// Mirrors <see cref="MarginBoxRenderer"/>'s own <c>PaintImage</c> precedent for going from a
        /// point-space margin-box rect to a fresh, scoped <c>PdfCanvas</c> - <see cref="MarginBoxFragment.Content"/>'s
        /// coordinates are already in that same pixel space (<see cref="RunningElementLayout.LayoutRunningElementFor"/>
        /// laid it out directly against the pixel-converted rect), so no further conversion happens here.
        /// <para>
        /// The running element's own background/border (a real, laid-out <see cref="CssBox"/>) paints for
        /// free through <c>PaintFragment</c> above - what doesn't is the margin-box RULE's own
        /// background/border (e.g. <c>@top-center { content: element(chapter-title); background-color:
        /// #ccc; }</c>), painted here via <see cref="MarginBoxRenderer.PaintBackgroundAndBorder"/> at the
        /// same rect <c>HtmlContainerInt.LayoutMarginBoxes</c> derived for layout, recomputed
        /// (<see cref="MarginBoxRenderer.FindMargin"/> by <see cref="MarginBoxFragment.BoxName"/>) since
        /// it isn't retained on <see cref="MarginBoxFragment"/> itself - the same recomputation-not-
        /// caching convention <see cref="MarginBoxRenderer.Render"/>'s own text path already follows.
        /// </para>
        /// <para>
        /// A second pass then covers every <c>element(...)</c>-content rule that has NO
        /// <see cref="MarginBoxFragment"/> at all - a page where the named running element hasn't been
        /// reached yet (or never exists, e.g. a misspelled name) - since <c>LayoutMarginBoxes</c> only
        /// creates a fragment once its running-element lookup actually resolves. That rule's own
        /// background/border must still paint independent of content, the same as an ordinary
        /// <c>content: none</c>/empty text-path box already does in <c>MarginBoxRenderer.Render</c> -
        /// otherwise a margin box's background/border would silently depend on whether its content
        /// happens to have resolved yet, which is not what "paints unconditionally" is supposed to mean.
        /// </para>
        /// </remarks>
        private static async Task PaintElementMarginBoxes(
            CanvasLease lease, RenderContext renderContext, HtmlContainerInt htmlContainer, IReadOnlyList<MarginBoxFragment> marginBoxes,
            Size pageSize, double marginLeft, double marginTop, double marginRight, double marginBottom,
            IReadOnlyList<MarginStyleRule> margins, StyleDeclaration? pageStyle,
            Dictionary<string, IReadOnlyList<CssImage?>?> backgroundImageCache)
        {
            var pixelsPerPoint = renderContext.LayoutUnitsPerPoint;
            using var pdfLease = lease;
            Canvas pdfCanvas = pdfLease;
            var painter = new FragmentPainter(htmlContainer);
            var remPt = htmlContainer.PageLengthContext?.RemPt ?? DefaultFontResolver.FontSize;
            var resolvedBoxNames = marginBoxes.Count > 0
                ? new HashSet<string>(marginBoxes.Select(m => m.BoxName), StringComparer.OrdinalIgnoreCase)
                : [];

            async Task PaintRuleOwnBackgroundAndBorder(string boxName, MarginStyleRule rule)
            {
                var outerRect = MarginBoxRenderer.GetMarginBoxRect(boxName, pageSize,
                    marginLeft, marginTop, marginRight, marginBottom, margins, pageStyle, remPt);
                var cbWidth = MarginBoxRenderer.MarginAreaWidth(boxName, pageSize, marginLeft, marginRight);
                var cbHeight = MarginBoxRenderer.MarginAreaHeight(boxName, pageSize, marginTop, marginBottom);

                await MarginBoxRenderer.PaintBackgroundAndBorder(pdfCanvas, outerRect, pageSize, rule, pageStyle, remPt,
                    cbWidth, cbHeight, renderContext, htmlContainer, backgroundImageCache);
            }

            foreach (var marginBox in marginBoxes)
            {
                if (MarginBoxRenderer.FindMargin(margins, marginBox.BoxName) is { } rule)
                    await PaintRuleOwnBackgroundAndBorder(marginBox.BoxName, rule);

                painter.PaintDetached(pdfCanvas, marginBox.Content);
            }

            foreach (var rule in margins)
            {
                var boxName = rule.Selector?.Text?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(boxName) || resolvedBoxNames.Contains(boxName))
                    continue;

                var contentValue = rule.Style.Content;
                if (string.IsNullOrEmpty(contentValue) || !MarginBoxRenderer.TryParseElementFunction(contentValue, out _, out _))
                    continue;

                await PaintRuleOwnBackgroundAndBorder(boxName, rule);
            }
        }


        /// <summary>
        /// The <see cref="Canvas"/>-level half of <c>PaintFootnoteArea</c>,
        /// split out so a test can drive it with a recording <see cref="Canvas"/> and assert the actual
        /// draw call sequence (which calls, in what order) rather than only that a full PDF pipeline
        /// completes without error or that a fragment holds the right data - see this repo's own testing
        /// conventions on why a token/page-count check alone is not proof a paint path isn't a no-op.
        /// </summary>
        internal static void PaintFootnoteArea(Canvas pdfCanvas, RenderContext renderContext, HtmlContainerInt htmlContainer, FootnoteAreaFragment footnoteArea)
        {
            // Bypasses FragmentPainter.Paint's own clip push (this is called directly, not through that
            // wrapper), so it needs its own content-area bound - previously provided for free by a raw
            // PdfGraphics.IntersectClip AddPdfPages applied ahead of this call, removed because it also
            // (wrongly) bounded position: fixed content the same way; see AddPdfPages's own remarks.
            pdfCanvas.PushClip(htmlContainer.PageClipOverride ?? htmlContainer.PageBoxRect);

            var rect = footnoteArea.DividerRect;
            if (rect.Height > 0)
            {
                var dividerColor = ResolveFootnoteDividerColor(footnoteArea.DividerColor, renderContext);
                pdfCanvas.DrawRectangle(pdfCanvas.GetSolidBrush(dividerColor), rect.X, rect.Y, rect.Width, rect.Height);
            }

            var painter = new FragmentPainter(htmlContainer);
            foreach (var body in footnoteArea.Bodies)
            {
                painter.PaintDetached(pdfCanvas, body);
            }

            pdfCanvas.PopClip();
        }

        /// <summary>
        /// The <c>@footnote</c> divider's resolved paint color - black when <paramref name="declaredColor"/>
        /// is null (no <c>border-top</c> declared, see <see cref="HtmlContainerInt.ResolveFootnoteAreaRule"/>)
        /// or resolves to no real color of its own (<c>currentcolor</c>/<c>initial</c> - the divider is a
        /// UA-drawn separator with no element of its own to inherit a text color from), otherwise the
        /// declared color parsed the same way any other CSS color value is.
        /// </summary>
        internal static PaintColor ResolveFootnoteDividerColor(string? declaredColor, RenderContext renderContext) =>
            string.IsNullOrWhiteSpace(declaredColor) ||
            declaredColor.Equals(Keywords.CurrentColor, StringComparison.OrdinalIgnoreCase) ||
            declaredColor.Equals(Keywords.Initial, StringComparison.OrdinalIgnoreCase)
                ? PaintColor.Black
                : new CssValueParser(renderContext).GetActualColor(declaredColor);
    }
}
