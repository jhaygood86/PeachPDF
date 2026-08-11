using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Builds a PDF outline (bookmark sidebar) from every box the document's <c>bookmark-level</c>
    /// cascade resolved to something other than <c>none</c> (CSS Generated Content Module Level 3 §2).
    /// Unlike <see cref="StructureTagBuilder"/>, this never hooks into <see cref="Paint.FragmentPainter"/>'s
    /// paint walk - a PDF outline (<see cref="PdfOutline"/>/<see cref="PdfOutlineCollection"/>) is a
    /// free-standing dictionary tree independent of the content stream, so it only needs
    /// <c>bookmarkBoxes</c> in document order (already collected alongside link boxes by
    /// <see cref="Utils.DomUtils.GetAllLinkAndBookmarkBoxes"/> - see the call site in <c>PdfGenerator</c>)
    /// and the finished page objects to resolve destinations against. Unconditional - no
    /// <see cref="PdfGenerateConfig"/> gate, since the UA default stylesheet's own
    /// <c>bookmark-level: none</c> default is itself the "no bookmarks" state for a document with no
    /// headings.
    /// </summary>
    internal static class BookmarkOutlineBuilder
    {
        public static void Build(PdfDocument document, HtmlContainer container,
            IReadOnlyList<FragmentainerFragment> fragmentainers, IReadOnlyList<CssBox> bookmarkBoxes)
        {
            if (bookmarkBoxes.Count == 0) return;

            var (slotToPage, maxMappedSlot) = PageAnchorResolver.BuildSlotToPageMap(fragmentainers);

            // Bookmarks do not need to create a strict hierarchy of levels (css-content-3 §2, note) -
            // no algorithm is mandated. This is the conventional heading-outline shape (also Prince's
            // and every browser print-to-PDF's own behavior): a level-N bookmark nests under the
            // nearest preceding bookmark with a shallower level, or the root if none exists.
            var stack = new Stack<(int Level, PdfOutline Outline)>();

            foreach (var box in bookmarkBoxes)
            {
                var level = ResolveLevel(box);
                if (level is not { } n) continue;

                while (stack.Count > 0 && stack.Peek().Level >= n)
                    stack.Pop();

                var outline = new PdfOutline
                {
                    Title = CssContentEngine.ResolveBookmarkLabel(box),
                    Opened = ResolveState(box) != BookmarkState.Closed
                };

                ApplyDestination(document, outline, box, container, slotToPage, maxMappedSlot, fragmentainers.Count);

                if (stack.Count > 0)
                    stack.Peek().Outline.Outlines.Add(outline);
                else
                    document.Outlines.Add(outline);

                stack.Push((n, outline));
            }
        }

        /// <summary>
        /// Re-parses the raw <c>none | &lt;integer&gt;</c> string (mirroring <see cref="StructureTagMapper.Classify"/>'s
        /// own re-parse of <c>PdfTagType</c>) - the CSS-OM <c>BookmarkLevelProperty</c> converter
        /// (<see cref="Converters.BookmarkLevelConverter"/>) already validated the grammar at
        /// declaration time, so an invalid/unparseable string here can only be a global-keyword
        /// spelling (e.g. <c>inherit</c> resolved to its cascade-computed text) or genuinely absent.
        /// </summary>
        private static int? ResolveLevel(CssBox box)
        {
            var raw = box.BookmarkLevel;
            return !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw.Trim(), out var n) && n >= 1 ? n : null;
        }

        private static BookmarkState ResolveState(CssBox box) =>
            !string.IsNullOrWhiteSpace(box.BookmarkState) && Map.BookmarkStates.TryGetValue(box.BookmarkState.Trim(), out var state)
                ? state
                : BookmarkState.Open;

        /// <summary>
        /// Resolves <c>-peachpdf-bookmark-target</c> (self by default) into either a page destination
        /// (self, or an in-document <c>url(#id)</c>/<c>attr()</c> fragment) or an external-URL <c>/A</c>
        /// action (see <see cref="PdfOutline.Url"/>). A destination that can't be resolved (e.g. a
        /// <c>display: none</c> heading) leaves the outline with neither - a supported, harmless state
        /// (proven by <c>PdfOutlineSaveTests.Save_WithOutlineWithoutDestinationPage_Succeeds</c>) chosen
        /// over dropping the entry, since dropping it would perturb sibling/descendant nesting.
        /// </summary>
        private static void ApplyDestination(PdfDocument document, PdfOutline outline, CssBox box,
            HtmlContainer container, IReadOnlyDictionary<int, int> slotToPage, int maxMappedSlot, int fallbackPageCount)
        {
            var inner = container.HtmlContainerInt;
            var ppp = container.PixelsPerPoint;

            // Resolved (not the raw declared value) so a relative external target - e.g.
            // -peachpdf-bookmark-target: attr(href) reading a plain "other.html" - is followed the same
            // way an equivalent <a href> link would be, rather than being written raw and unresolvable
            // by a reader for anything but an already-absolute URL. ResolveHref leaves a #-prefixed
            // fragment untouched, so the classification below still sees it.
            var target = ResolveTargetValue(box) is { } raw ? container.ResolveHref(raw) : null;

            XRect? rect;
            if (target is null)
            {
                // A display:none (or otherwise unrendered) box was never actually painted -
                // CommonUtils.GetFirstValueOrDefault would silently fall back to Bounds (typically
                // all-zero/default), producing a nonsense page-0/Y-0 destination instead of the
                // documented "leave the outline with neither" behavior. Same ActualDisplay check
                // FragmentPainter itself uses to skip painting a box entirely.
                if (box.DerivedStyle.ActualDisplay == Keywords.None) return;

                var ownRect = CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds);
                rect = PeachPDF.Utilities.Utils.Convert(ownRect, ppp);
            }
            else if (target.Length > 1 && target[0] == '#')
            {
                rect = container.GetElementRectangle(target[1..]);
            }
            else if (target.Length > 0)
            {
                outline.Url = target;
                return;
            }
            else
            {
                return;
            }

            if (rect is not { } r) return;

            var (pageIndex, topPt) = PageAnchorResolver.ResolveRectToPage(inner, ppp, slotToPage, maxMappedSlot, fallbackPageCount, r);
            if (pageIndex < 0 || pageIndex >= document.Pages.Count) return;

            outline.DestinationPage = document.Pages[pageIndex];
            // /FitH top fits page width and positions vertically at top - the outline-entry analog of
            // the anchor-link destination fix in PdfGenerator.HandleLinks, not /FitV's horizontal left.
            // topPt is top-down (0 at the page's own top margin, increasing downward, matching every
            // other rect PageAnchorResolver works with), but /FitH's own top parameter is PDF default
            // user space, which is bottom-up (0 at the page's bottom edge) - flip via the destination
            // page's own height before writing it, exactly like PdfGenerator.HandleLinks' matching fix.
            outline.PageDestinationType = PdfPageDestinationType.FitH;
            outline.Top = outline.DestinationPage.Height - topPt;
        }

        /// <summary>
        /// Evaluates <c>-peachpdf-bookmark-target</c>'s <c>self | url() | attr()</c> grammar (Prince's
        /// own extension, not a real css-content-3 property - see the property's css-properties.json
        /// entry) to a raw target string, or null for <c>self</c>. The caller classifies the result by
        /// <c>#</c>-prefix, mirroring <see cref="Entities.LinkElementData{T}.IsAnchor"/>'s identical
        /// check for <c>&lt;a href="#id"&gt;</c>.
        /// </summary>
        private static string? ResolveTargetValue(CssBox box)
        {
            var raw = box.BookmarkTarget;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var tokens = CssValueParser.GetCssTokens(raw);
            if (tokens.Count == 0) return null;

            return tokens[0] switch
            {
                KeywordToken { Data: Keywords.Self } => null,
                UrlToken urlToken => string.IsNullOrEmpty(urlToken.Data) ? null : urlToken.Data,
                FunctionToken { Data: "attr" } attrToken => ResolveAttrTarget(box, attrToken),
                _ => null
            };
        }

        private static string? ResolveAttrTarget(CssBox box, FunctionToken attrToken)
        {
            if (attrToken.ArgumentTokens.FirstOrDefault() is not KeywordToken nameToken) return null;

            // Same pseudo-element -> parent redirect CssContentEngine's own attr() handling uses - a
            // pseudo-element's own attributes don't exist, so it must read the real source element's.
            var sourceBox = box.IsPseudoElement && box.ParentBox != null ? box.ParentBox : box;
            var value = sourceBox.GetAttribute(nameToken.Data, "");
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
