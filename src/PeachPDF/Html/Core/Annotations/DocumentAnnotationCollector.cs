using PeachDrawing.Core;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Annotations
{
    /// <summary>
    /// Works out what a reader of the laid-out pages can click or navigate (<see cref="DocumentAnnotations"/>) from the
    /// document and its fragment tree, in terms of pages and points. It knows nothing about the format the pages are written to.
    /// </summary>
    internal static class DocumentAnnotationCollector
    {
        /// <summary>
        /// Collects the hyperlinks and the outline of a document that has been laid out.
        /// </summary>
        /// <param name="container">the laid-out document</param>
        /// <param name="fragmentainers">the pages, in order</param>
        public static DocumentAnnotations Collect(HtmlContainerInt container, IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            var ppp = container.RenderContext.LayoutUnitsPerPoint;
            var (slotToPage, maxMappedSlot) = PageAnchorResolver.BuildSlotToPageMap(fragmentainers);

            // An anchor inside a skipped (content-empty) slot attributes to the next materialized page - the nearest place a reader
            // can actually land. Shared by both link sources below (the main document tree and, per css-gcpm-3, running-element
            // content) so they can never disagree about which page an anchor lands on.
            PagePosition? ResolveAnchorTarget(string anchorId)
            {
                // href="#" (an empty fragment, commonly used as a JS-driven no-op link) reaches here as an empty id - there is no
                // element to target, so treat it like an anchor whose target simply wasn't found.
                if (anchorId.Length == 0) return null;

                return ResolveElementRectangle(container, ppp, anchorId) is { } anchorRect
                    ? ToPosition(PageAnchorResolver.ResolveRectToPage(container, ppp, fragmentainers, slotToPage, maxMappedSlot, fragmentainers.Count, anchorRect))
                    : null;
            }

            var bookmarkBoxes = new List<CssBox>();
            var links = new List<PageLink>();

            foreach (var link in container.GetLinks(bookmarkBoxes))
            {
                var href = ResolveHref(container, link.Href);
                var rectangle = Utilities.Utils.ToPoints(link.Rectangle, ppp);
                var isAnchor = href.Length > 0 && href[0] == '#';
                var anchorId = isAnchor && href.Length > 1 ? href[1..] : string.Empty;

                // Link rects are true points; slot attribution runs on the internal-pixel shifted grid (content starts at MarginTop,
                // not 0).
                var firstSlot = Math.Max(container.PageIndexOf(rectangle.Top * ppp), 0);
                for (var slot = firstSlot; container.PageTopOf(slot) < rectangle.Bottom * ppp; slot++)
                {
                    if (!slotToPage.TryGetValue(slot, out var pageIndex))
                        continue;

                    // Page-local geometry in true points, matching the painted content's own per-page margins - read off the
                    // fragmentainer itself (already corrected against its materialized page number) rather than re-derived from the
                    // geometry table, so this can never disagree with what was actually painted. ContentTopPt/ContentLeftPt include
                    // the page box's own border and padding, where the content it overlays is painted.
                    var geometry = fragmentainers[pageIndex].Geometry;
                    var topPt = geometry.ContentTopPt + (rectangle.Top * ppp - container.PageTopOf(slot)) / ppp;
                    var leftPt = geometry.ContentLeftPt + (rectangle.Left * ppp - container.MarginLeft) / ppp;
                    var bounds = new Rect(leftPt, topPt, rectangle.Width, rectangle.Height);

                    // A link over an element a transform moved goes where it was drawn (css-transforms-1 "Transform Rendering"), which
                    // only the painter knows: the bounding box of the drawn shape, and the shape itself when it is not that box.
                    PaintPoint[]? quad = null;
                    if (link.GeometryBox is { } geometryBox && (geometryBox.HtmlContainer ?? container).Placements.TryGet(geometryBox, slot, out var placement))
                    {
                        var frame = new PageFrame(geometry.ContentLeftPt, geometry.ContentTopPt, container.MarginLeft, container.MarginTop, ppp, geometry.SheetHeightPt);
                        var local = new Rect(rectangle.Left * ppp, rectangle.Top * ppp - fragmentainers[pageIndex].LocalOriginY, rectangle.Width * ppp, rectangle.Height * ppp);

                        // Not on the page at all (moved off it, or wholly behind the viewer): nothing drawn there to click.
                        if (!AnnotationPlacement.TryPlaceLink(placement, local, frame, geometry.SheetWidthPt, geometry.SheetHeightPt, out bounds, out quad))
                            continue;
                    }

                    if (isAnchor)
                    {
                        if (ResolveAnchorTarget(anchorId) is not { } destination) continue;

                        links.Add(new PageLink { PageIndex = pageIndex, Bounds = bounds, Quad = quad, Anchor = anchorId, Destination = destination, Source = link.SourceBox });
                    }
                    else
                    {
                        links.Add(new PageLink { PageIndex = pageIndex, Bounds = bounds, Quad = quad, Uri = href, Source = link.SourceBox });
                    }
                }
            }

            CollectRunningElementLinks(container, ppp, fragmentainers, ResolveAnchorTarget, links);

            var bookmarks = CollectBookmarks(container, ppp, fragmentainers, slotToPage, maxMappedSlot, bookmarkBoxes);

            return new DocumentAnnotations { Links = links, Bookmarks = bookmarks };
        }

        /// <summary>
        /// The second, per-page link source: a link inside a css-gcpm-3 running element's content. The main loop is driven by the
        /// live box tree's geometry, which never holds a running box's own links (and the same link can legitimately produce a
        /// different region on every page its running element was selected onto), so these come from each page's captured margin
        /// box content, which already names exactly one page with an already-final rectangle.
        /// </summary>
        private static void CollectRunningElementLinks(
            HtmlContainerInt container, double ppp, IReadOnlyList<FragmentainerFragment> fragmentainers,
            Func<string, PagePosition?> resolveAnchorTarget, List<PageLink> links)
        {
            for (var pageIndex = 0; pageIndex < fragmentainers.Count; pageIndex++)
            {
                var marginBoxes = fragmentainers[pageIndex].MarginBoxes;
                if (marginBoxes.Count == 0) continue;

                var geometry = fragmentainers[pageIndex].Geometry;

                foreach (var marginBox in marginBoxes)
                {
                    foreach (var (fragment, box) in FindLinkFragments(marginBox.Content))
                    {
                        var href = box.HtmlTag?.TryGetAttribute("href") ?? "";
                        if (href.Length == 0) continue;

                        var rectPt = Utilities.Utils.ToPoints(fragment.Rect, ppp);
                        var bounds = new Rect(rectPt.X, rectPt.Y, rectPt.Width, rectPt.Height);

                        // A running element's content is laid out in page coordinates, so its frame has no content-box offset to undo.
                        PaintPoint[]? quad = null;
                        if ((box.HtmlContainer ?? container).Placements.TryGet(fragment, out var placement))
                        {
                            var frame = new PageFrame(0, 0, 0, 0, ppp, geometry.SheetHeightPt);
                            if (!AnnotationPlacement.TryPlaceLink(placement, fragment.Rect, frame, geometry.SheetWidthPt, geometry.SheetHeightPt, out bounds, out quad))
                                continue;
                        }

                        if (href[0] == '#')
                        {
                            var anchorId = href[1..];
                            if (resolveAnchorTarget(anchorId) is not { } destination) continue;

                            links.Add(new PageLink { PageIndex = pageIndex, Bounds = bounds, Quad = quad, Anchor = anchorId, Destination = destination, Source = box });
                        }
                        else
                        {
                            // Read once per external link per page (a running element's link is re-resolved for every page it was
                            // selected onto), so the document base is memoized - see HtmlContainerInt.DocumentBaseUri.
                            links.Add(new PageLink { PageIndex = pageIndex, Bounds = bounds, Quad = quad, Uri = ResolveHref(container, href), Source = box });
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recursively collects every clickable, visible box fragment in <paramref name="fragment"/>'s subtree - the fragment-tree
        /// analog of <see cref="DomUtils.GetAllLinkBoxes"/>, which walks the live box tree and is unusable for a running element.
        /// </summary>
        private static IEnumerable<(BoxFragment Fragment, CssBox Box)> FindLinkFragments(BoxFragment fragment)
        {
            // Not drawn (a transform that leaves no visible area), so not clickable - the same rule the main-tree collectors apply.
            if (DomUtils.LeavesNoVisibleArea(fragment.Box))
            {
                yield break;
            }

            if (fragment.Box is { IsClickable: true, Visibility.Value: Visibility.Visible })
            {
                yield return (fragment, fragment.Box);
            }

            foreach (var child in fragment.Children)
            {
                foreach (var found in FindLinkFragments(child))
                {
                    yield return found;
                }
            }
        }

        /// <summary>
        /// Resolves a relative href (from an <c>&lt;a href&gt;</c> or a <c>-peachpdf-bookmark-target: attr()/url()</c> value) against
        /// the document's <c>&lt;base&gt;</c> element or base URI, so an external bookmark target resolves exactly like an equivalent
        /// link. A fragment-only href is left as it is.
        /// </summary>
        private static string ResolveHref(HtmlContainerInt container, string href)
        {
            // Read per link, so the document base has to be memoized rather than re-walked - see HtmlContainerInt.DocumentBaseUri.
            var baseUri = container.DocumentBaseUri;

            return href.StartsWith('#') || baseUri is null ? href : new RUri(baseUri, href).AbsoluteUri;
        }

        /// <summary>The rectangle of the element with id <paramref name="elementId"/> in points, or null when there is none.</summary>
        private static Rect? ResolveElementRectangle(HtmlContainerInt container, double ppp, string elementId)
        {
            var r = container.GetElementRectangle(elementId);
            return r.HasValue ? Utilities.Utils.ToPoints(r.Value, ppp) : null;
        }

        private static PagePosition ToPosition((int PageIndex, double TopPt) resolved) => new(resolved.PageIndex, resolved.TopPt);

        private static List<Bookmark> CollectBookmarks(
            HtmlContainerInt container, double ppp, IReadOnlyList<FragmentainerFragment> fragmentainers,
            IReadOnlyDictionary<int, int> slotToPage, int maxMappedSlot, IReadOnlyList<CssBox> bookmarkBoxes)
        {
            var bookmarks = new List<Bookmark>(bookmarkBoxes.Count);

            foreach (var box in bookmarkBoxes)
            {
                if (ResolveLevel(box) is not { } level) continue;

                var (uri, destination) = ResolveDestination(box, container, ppp, fragmentainers, slotToPage, maxMappedSlot);
                bookmarks.Add(new Bookmark
                {
                    Level = level,
                    Title = CssContentEngine.ResolveBookmarkLabel(box),
                    IsOpen = ResolveState(box) != BookmarkState.Closed,
                    Uri = uri,
                    Destination = destination,
                });
            }

            return bookmarks;
        }

        /// <summary>
        /// Re-parses the raw <c>none | &lt;integer&gt;</c> <c>bookmark-level</c> string - the CSS-OM converter already validated the
        /// grammar at declaration time, so an invalid string here can only be a global-keyword spelling or genuinely absent.
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
        /// Resolves <c>-peachpdf-bookmark-target</c> (self by default) into either a position in the document (self, or an in-document
        /// <c>url(#id)</c>/<c>attr()</c> fragment) or an external URI. A target that can't be resolved (a <c>display: none</c> heading,
        /// say) yields neither - kept as an entry without a destination rather than dropped, since dropping it would change the
        /// nesting of its siblings and descendants.
        /// </summary>
        private static (string? Uri, PagePosition? Destination) ResolveDestination(
            CssBox box, HtmlContainerInt container, double ppp, IReadOnlyList<FragmentainerFragment> fragmentainers,
            IReadOnlyDictionary<int, int> slotToPage, int maxMappedSlot)
        {
            // Resolved (not the raw declared value) so a relative external target is followed the same way an equivalent <a href> link
            // would be. ResolveHref leaves a #-prefixed fragment untouched, so the classification below still sees it.
            var target = ResolveTargetValue(box) is { } raw ? ResolveHref(container, raw) : null;

            Rect? rect;
            if (target is null)
            {
                // A display:none (or otherwise unrendered) box was never painted - falling back to its (typically all-zero) bounds
                // would produce a nonsense page-0/Y-0 destination instead of none.
                if (box.DerivedStyle.ActualDisplay == Keywords.None) return (null, null);

                // A display:contents element has no geometry of its own: its content's is where it points.
                var geometryBox = DomUtils.ResolveGeometryBox(box);
                var ownRect = CommonUtils.GetFirstValueOrDefault(geometryBox.Rectangles, geometryBox.Bounds);
                rect = Utilities.Utils.ToPoints(ownRect, ppp);
            }
            else if (target.Length > 1 && target[0] == '#')
            {
                rect = ResolveElementRectangle(container, ppp, target[1..]);
            }
            else if (target.Length > 0)
            {
                return (target, null);
            }
            else
            {
                return (null, null);
            }

            if (rect is not { } r) return (null, null);

            var resolved = PageAnchorResolver.ResolveRectToPage(container, ppp, fragmentainers, slotToPage, maxMappedSlot, fragmentainers.Count, r);
            return resolved.PageIndex < 0 || resolved.PageIndex >= fragmentainers.Count ? (null, null) : (null, ToPosition(resolved));
        }

        /// <summary>
        /// Evaluates <c>-peachpdf-bookmark-target</c>'s <c>self | url() | attr()</c> grammar to a raw target string, or null for
        /// <c>self</c>. The caller classifies the result by a <c>#</c> prefix, as <c>&lt;a href="#id"&gt;</c> is.
        /// </summary>
        private static string? ResolveTargetValue(CssBox box)
        {
            var raw = box.BookmarkTarget;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            using var pooledTokens = CssValueParser.GetCssTokensPooled(raw);
            List<Token> tokens = pooledTokens;
            if (tokens.Count == 0) return null;

            return tokens[0] switch
            {
                { Type: TokenType.Hash or TokenType.AtKeyword or TokenType.Ident, Data: Keywords.Self } => null,
                { Type: TokenType.Url } urlToken => urlToken.Data.IsEmpty ? null : urlToken.Data.ToString(),
                { Type: TokenType.Function, Data: "attr" } attrToken => ResolveAttrTarget(box, attrToken),
                _ => null
            };
        }

        private static string? ResolveAttrTarget(CssBox box, Token attrToken)
        {
            if (attrToken.ArgumentTokens.FirstOrDefault() is not { Type: TokenType.Hash or TokenType.AtKeyword or TokenType.Ident } nameToken) return null;

            // Same pseudo-element -> parent redirect CssContentEngine's own attr() handling uses - a pseudo-element's own attributes
            // don't exist, so it must read the real source element's.
            var sourceBox = box.OriginatingElement;
            var value = sourceBox.GetAttribute(nameToken.Data.ToString(), "");
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
