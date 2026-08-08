using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using System;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// Where one of a box's decoration rectangles paints its background, border, corner radii and shadow,
    /// once <c>box-decoration-break</c>
    /// (<see href="https://www.w3.org/TR/css-break-3/#break-decoration">css-break-3 §6.2</see>) has had its
    /// say. The whole of §6.2 lives in <see cref="For"/>, so the painter never branches on the property
    /// itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// §6.2 is one rule read two ways. <c>slice</c> — the initial value — renders the box "as though the
    /// element were rendered with no breaks present, and then sliced by the breaks afterward": every
    /// decoration resolves against the <b>unbroken</b> box and a clip does the cutting, so no border or
    /// padding is inserted at a break, no shadow is drawn at a broken edge, and a background or
    /// <c>border-radius</c> follows the whole box's geometry. <c>clone</c> wraps <b>each fragment</b>
    /// independently: its own border, padding, radii and shadow, and its own background — a
    /// <c>no-repeat</c> background image appearing once per fragment rather than once per box.
    /// </para>
    /// <para>
    /// Both readings collapse to the same thing for a box that is not broken, which is nearly every box.
    /// </para>
    /// </remarks>
    /// <param name="DecorationRect">
    /// the rectangle every decoration resolves against — the unbroken box under <c>slice</c>, this
    /// fragment's own box under <c>clone</c>
    /// </param>
    /// <param name="ClipRect">
    /// this fragment's own decoration rectangle, which is what confines a <c>slice</c> decoration to its
    /// own slice of <paramref name="DecorationRect"/>
    /// </param>
    /// <param name="HasLeftEdge">
    /// whether the box's leading border and padding belong to this rectangle. Gates the left border edge
    /// and the leading inset of an underline.
    /// </param>
    /// <param name="HasRightEdge">whether the box's trailing border and padding belong to this rectangle</param>
    /// <param name="HasTopEdge">
    /// whether the box's own top border belongs to this rectangle. False on a fragment that resumes an
    /// earlier fragmentainer. At a <i>page</i> break this only restates what the page clip already does — the
    /// box's real top edge is on another page — but at a <b>column</b> boundary there is no clip, because two
    /// columns share one page band, so this is the only thing that keeps <c>slice</c> from closing the box at
    /// the break.
    /// </param>
    /// <param name="HasBottomEdge">whether the box's own bottom border belongs to this rectangle</param>
    /// <param name="NeedsClip">
    /// whether <paramref name="ClipRect"/> has to be pushed before painting. False whenever
    /// <paramref name="DecorationRect"/> is this rectangle already, which keeps the common case's content
    /// stream free of clip pairs it does not need.
    /// </param>
    internal readonly record struct BoxDecorationGeometry(
        RRect DecorationRect,
        RRect ClipRect,
        bool HasLeftEdge,
        bool HasRightEdge,
        bool HasTopEdge,
        bool HasBottomEdge,
        bool NeedsClip)
    {
        /// <summary>
        /// A rectangle no break touches: it is its own decoration area and owns all four of its edges. The
        /// page canvas and every replaced element (monolithic, per
        /// <see href="https://www.w3.org/TR/css-break-3/#monolithic">§4.1</see>) paint through this.
        /// </summary>
        internal static BoxDecorationGeometry Unbroken(RRect rect) =>
            new(rect, rect, HasLeftEdge: true, HasRightEdge: true,
                HasTopEdge: true, HasBottomEdge: true, NeedsClip: false);

        /// <summary>
        /// Resolves §6.2 for one of <paramref name="box"/>'s decoration rectangles.
        /// </summary>
        internal static BoxDecorationGeometry For(CssBox box, LineFragment line)
        {
            var slice = line.Slice;

            if (box.BoxDecorationBreak.Value == BoxDecorationBreakMode.Clone)
            {
                // Each fragment is wrapped independently, so its own band-cut box IS its decoration area
                // and every edge of it is a real edge - a box crossing a page boundary gets a closed
                // border at the break rather than one the page clip cuts away. Nothing to clip: the
                // decoration area is already this fragment's.
                return new BoxDecorationGeometry(slice.FragmentRect, slice.FragmentRect,
                    HasLeftEdge: true, HasRightEdge: true,
                    HasTopEdge: true, HasBottomEdge: true, NeedsClip: false);
            }

            // Slicing an unbroken box, or one whose decorations do not depend on where they sit, is
            // indistinguishable from painting the rectangle directly - so don't pay for it.
            if (slice.UnbrokenStrip == line.Rect || !NeedsUnbrokenGeometry(box))
            {
                return new BoxDecorationGeometry(line.Rect, line.Rect,
                    slice.HasLeftEdge, slice.HasRightEdge,
                    slice.HasTopEdge, slice.HasBottomEdge, NeedsClip: false);
            }

            return new BoxDecorationGeometry(slice.UnbrokenStrip, line.Rect,
                slice.HasLeftEdge, slice.HasRightEdge,
                slice.HasTopEdge, slice.HasBottomEdge, NeedsClip: true);
        }

        /// <summary>
        /// Whether any of <paramref name="box"/>'s decorations actually depend on the rectangle they are
        /// resolved against, and so must see the unbroken box rather than one slice of it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A decoration whose painted result is the same wherever along the break axis it is measured from
        /// can be painted per-fragment and produces identical output, so the unbroken path — which costs a
        /// clip pair and a much wider fill — is skipped for it. That holds for a solid
        /// <c>background-color</c> clipped to the border box, and for a border edge, whose trapezoid over
        /// the unbroken box clipped to this rectangle is point-for-point the trapezoid on the rectangle
        /// itself with the same mitre cuts. It does not hold for:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <c>border-radius</c>: <see cref="DerivedStyle.ComputeRadii"/>'s overlap reduction scales
        /// with the rectangle's own width.
        /// </description></item>
        /// <item><description>
        /// any <c>background-image</c> layer — a URL, an SVG or a gradient: position, size and repeat are
        /// all measured against the rectangle. This entry is also what lets
        /// <see cref="Handlers.CssImagePainter"/> paint a layer unconditionally instead of gating it to the
        /// first rectangle: a box with an image layer never takes the short path, so its layer is always
        /// positioned in the unbroken box and appears wherever in the box it belongs.
        /// </description></item>
        /// <item><description>
        /// <c>box-shadow</c>: offsets, blur and spread are measured from the rectangle's edges.
        /// </description></item>
        /// <item><description>
        /// a <c>background-clip</c> other than <c>border-box</c>: the solid fill's inset by the box's own
        /// border and padding belongs at the box's true start and end, not at every break.
        /// (<c>background-origin</c> needs no entry of its own — it positions image layers only, and its
        /// initial value is <c>padding-box</c>, so testing it would put every box on the unbroken path.)
        /// </description></item>
        /// </list>
        /// <para>
        /// A dotted or dashed border's dash phase restarts at each fragment rather than running through the
        /// unbroken box, which this deliberately does not correct — it would put a very common and cheap
        /// case on the expensive path for a sub-pixel difference.
        /// </para>
        /// </remarks>
        private static bool NeedsUnbrokenGeometry(CssBox box) =>
            box.IsRounded
            || box.BackgroundImages is { Count: > 0 }
            || HasBoxShadow(box)
            || IsNonDefaultBackgroundClip(box.BackgroundClip);

        /// <summary>Whether the box declares a shadow that would actually be drawn.</summary>
        internal static bool HasBoxShadow(CssBox box) =>
            !string.IsNullOrEmpty(box.BoxShadow) &&
            !string.Equals(box.BoxShadow, Keywords.None, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether <c>background-clip</c> is anything other than its plain initial value. Deliberately not
        /// parsed into layers: this runs for every decoration rectangle of every box, and the cost of
        /// treating a redundant <c>border-box, border-box</c> as non-default is one clip pair, while the
        /// cost of getting it wrong is a mis-inset background.
        /// </summary>
        private static bool IsNonDefaultBackgroundClip(string value) =>
            !string.IsNullOrEmpty(value) && !string.Equals(value, Keywords.BorderBox, StringComparison.Ordinal);
    }
}
