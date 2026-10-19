using PeachDrawing.Core;
using PeachPDF.Html.Core.Dom;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Annotations
{
    /// <summary>
    /// A place in the laid-out document: a page and a vertical offset on it.
    /// </summary>
    /// <param name="PageIndex">the zero-based index of the page, in the order the pages were produced</param>
    /// <param name="Top">how far below the top edge of that page the position is, in points</param>
    internal readonly record struct PagePosition(int PageIndex, double Top);

    /// <summary>
    /// A clickable region of a page: the area a hyperlink covers, and where it leads.
    /// </summary>
    /// <remarks>
    /// Everything is in points and measured from the page's top-left corner with y running down, the way the page was
    /// laid out, so a consumer that writes a format with another convention (PDF's default user space runs y up from the
    /// bottom edge) converts once at its own boundary.
    /// </remarks>
    internal sealed class PageLink
    {
        /// <summary>The zero-based index of the page the link is on.</summary>
        public required int PageIndex { get; init; }

        /// <summary>
        /// The box to click: the area the link covers, or the bounding box of the drawn shape when a <c>transform</c>
        /// moved or reshaped the link.
        /// </summary>
        public required Rect Bounds { get; init; }

        /// <summary>
        /// The drawn shape's four corners (top-left, top-right, bottom-left, bottom-right of the rectangle it started as)
        /// when a transform made it something other than <see cref="Bounds"/>; null when the box says it all.
        /// </summary>
        public PaintPoint[]? Quad { get; init; }

        /// <summary>The absolute URI of an external link, or null for a link within the document.</summary>
        public string? Uri { get; init; }

        /// <summary>The id of the element a link within the document names, or null for an external link.</summary>
        public string? Anchor { get; init; }

        /// <summary>Where <see cref="Anchor"/> lands; null for an external link.</summary>
        public PagePosition? Destination { get; init; }

        /// <summary>The element the link was collected from, when it has one (a link inside an SVG image has none).</summary>
        public CssBox? Source { get; init; }
    }

    /// <summary>
    /// One entry of the document's outline, from an element whose <c>bookmark-level</c> is not <c>none</c>
    /// (CSS Generated Content Module Level 3, §2).
    /// </summary>
    internal sealed class Bookmark
    {
        /// <summary>The nesting level, 1 or more. An entry nests under the nearest preceding entry of a shallower level.</summary>
        public required int Level { get; init; }

        /// <summary>The label to show.</summary>
        public required string Title { get; init; }

        /// <summary>Whether the entry's children start expanded (<c>bookmark-state</c> is not <c>closed</c>).</summary>
        public required bool IsOpen { get; init; }

        /// <summary>The absolute URI the entry opens, when its <c>bookmark-target</c> is an external one.</summary>
        public string? Uri { get; init; }

        /// <summary>Where the entry leads within the document; null when it has no target (an unrendered element) or leads to <see cref="Uri"/>.</summary>
        public PagePosition? Destination { get; init; }
    }

    /// <summary>
    /// What a reader of a paged rendering can click or navigate: the hyperlinks of each page and the document's outline.
    /// </summary>
    internal sealed class DocumentAnnotations
    {
        /// <summary>Every hyperlink that lands on a page, in document order (links within running elements follow the document's own).</summary>
        public required IReadOnlyList<PageLink> Links { get; init; }

        /// <summary>The outline entries in document order.</summary>
        public required IReadOnlyList<Bookmark> Bookmarks { get; init; }
    }
}
