using PeachDrawing.Core;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Handlers;
using System.Collections.Generic;
using System.Numerics;

namespace PeachBrackets.Html.Annotations
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
        public StructureNode? Source { get; init; }
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
    /// A form control (<c>&lt;input&gt;</c>, <c>&lt;select&gt;</c>) that landed on a page and can be made interactive, with everything needed to
    /// do that: what it is, where it is, and how to draw each state of it.
    /// </summary>
    /// <remarks>
    /// Geometry follows the same convention as <see cref="PageLink"/>: points from the page's top-left corner, y down.
    /// </remarks>
    internal sealed class PageFormField
    {
        /// <summary>The zero-based index of the page the control is on.</summary>
        public required int PageIndex { get; init; }

        /// <summary>
        /// Where the control sits: its box, or the bounding box of the drawn shape when a <c>transform</c> moved it. A control that was not
        /// drawn on its page (a projected plane outside the clip, wholly behind the viewer) keeps its field - it still holds a value and
        /// belongs to its form, and a radio group would lose a member - so this is then an empty rectangle at its bottom-left corner,
        /// with nothing to see or click.
        /// </summary>
        public required Rect Bounds { get; init; }

        /// <summary>
        /// The size, in points, the control's appearance is drawn at. It is still drawn upright at its own size when a transform moved
        /// it; <see cref="AppearanceTransform"/> carries the drawing to where it landed.
        /// </summary>
        public required Size ContentSize { get; init; }

        /// <summary>
        /// The linear part of the transform that carries the upright appearance to where the control was drawn (translation left out, since
        /// the consumer fits the transformed appearance to <see cref="Bounds"/>), in the same y-down convention as <see cref="Bounds"/>;
        /// null when the control was not transformed or the transform is not affine (it is then drawn as its upright bounding box).
        /// </summary>
        public Matrix3x2? AppearanceTransform { get; init; }

        /// <summary>What the control is: its kind, name, value, options and attributes.</summary>
        public required FormFieldClassification Classification { get; init; }

        /// <summary>Draws each state of the control.</summary>
        public required FormFieldAppearance Appearance { get; init; }
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

        /// <summary>The form controls that landed on a page, in document order; empty unless interactive forms were asked for.</summary>
        public required IReadOnlyList<PageFormField> FormFields { get; init; }
    }
}
