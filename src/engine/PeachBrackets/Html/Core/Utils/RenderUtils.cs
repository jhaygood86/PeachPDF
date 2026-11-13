// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachBrackets.Css;
using PeachDrawing.Core;
using PeachDrawing.Core.Geometry;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// Provides some drawing functionality
    /// </summary>
    internal static class RenderUtils
    {
        /// <summary>
        /// Check if the given color is visible if painted (has alpha and color values)
        /// </summary>
        /// <param name="color">the color to check</param>
        /// <returns>true - visible, false - not visible</returns>
        public static bool IsColorVisible(PaintColor color)
        {
            return color.A > 0;
        }

        /// <summary>
        /// Pushes the clip an <c>overflow: hidden</c> ancestor imposes on the fragment being painted, as
        /// resolved by <see cref="Fragmentation.FragmentEmitter"/> and carried on
        /// <see cref="Fragments.BoxFragment.OverflowClip"/>/<see cref="Fragments.BoxFragment.OverflowClipCurve"/>.
        /// Already in the fragment's own space, so there is nothing here to map. When the clipping
        /// ancestor has a <c>border-radius</c>, an additional path clip confines content to the rounded
        /// curve itself (CSS Backgrounds and Borders Level 3 §5.5), not just its padding-edge rectangle.
        /// </summary>
        /// <param name="g">the graphics to clip</param>
        /// <param name="overflowClip">
        /// the rectangular clip resolved for the fragment being painted, or null when no ancestor clips it
        /// </param>
        /// <param name="curve">
        /// the clipping ancestor's rounded-corner curve, or null when it has no <c>border-radius</c>
        /// </param>
        /// <returns>the number of clips actually pushed (callers must pop exactly this many afterward)</returns>
        /// <param name="mayDrawText">
        /// false when the content the clip surrounds is known to draw no text, so the canvas can leave out what a clip that text is drawn inside costs it
        /// (see <see cref="Canvas.PushClipAroundShapes"/>); true, the default, whenever that is not known
        /// </param>
        public static int ClipGraphicsByOverflow(Canvas g, Rect? overflowClip, OverflowClipCurve? curve, bool mayDrawText = true)
        {
            if (overflowClip is not { } clip) return 0;

            // Intersecting with what is already on the stack is the one part that cannot be precomputed:
            // it depends on where in the paint walk this fragment is reached.
            // (Inside a frame a scroll container has carried back, the rectangle on the stack is in another frame: the canvas does the intersecting there.)
            if (!Paint.FragmentPainter.InScrolledFrame) clip.Intersect(g.GetClip());
            g.PushClip(clip);
            var pushed = 1;

            if (curve is { } c) pushed += PushRoundedClipIfRounded(g, c.Rect, c.Radii, mayDrawText);

            return pushed;
        }

        /// <summary>
        /// Pushes <paramref name="ancestor"/>'s own clip (padding-edge rect, per CSS spec, plus its
        /// rounded-corner curve if it has a <c>border-radius</c>) if it has <c>overflow: hidden</c>.
        /// Resolved from the ancestor's own fragment rectangle - already in the fragment-local space of
        /// the participant being painted, since both come from the same page's fragment tree - rather
        /// than the live box's <see cref="CssBox.Bounds"/>, so this is correct for a box shown at more
        /// than one place at once (a hoisted participant inside a repeated table header), where the
        /// live box only ever carries whichever page positioned it last.
        /// </summary>
        /// <returns>the number of clips actually pushed (callers must pop exactly this many afterward)</returns>
        internal static int TryPushOverflowClip(Canvas g, BoxFragment ancestor, bool snapToCssPixels)
        {
            if (ResolveOverflowClip(g, ancestor, snapToCssPixels) is not var (rect, curve)) return 0;

            return ClipGraphicsByOverflow(g, rect, curve);
        }

        /// <summary>
        /// The clip <paramref name="ancestor"/> imposes on its descendants - its padding edge, open on an
        /// unclipped axis, with the rounded-corner curve when it has a <c>border-radius</c> - or null when it
        /// does not clip. Split from <see cref="TryPushOverflowClip"/> so a painter can hold a clip back and
        /// push it later (or not at all, for a descendant that escapes it).
        /// </summary>
        internal static (Rect Rect, OverflowClipCurve? Curve)? ResolveOverflowClip(Canvas g, BoxFragment ancestor, bool snapToCssPixels)
        {
            var overflowBox = ancestor.Box;
            if (overflowBox.Overflow.Value != Overflow.Hidden && !overflowBox.ClipsWithoutScrolling && !DomUtils.IsScrollerOnScreen(overflowBox)) return null;

            // Snapped like the ancestor's own decorations, on the edges they snap (the fragment may be a cut
            // of it, and box-decoration-break decides which edges count), so a hoisted descendant is clipped
            // where an ordinary child would be. The padding box is taken from the snapped border box, which
            // is where the border's inner edge is drawn.
            var borderRect = ancestor.Rect;
            var paddingRect = PaddingEdgeOf(overflowBox, borderRect);
            if (snapToCssPixels)
            {
                var owned = ancestor.Lines is [var firstLine, ..]
                    ? Paint.BoxDecorationGeometry.For(overflowBox, firstLine)
                    : Paint.BoxDecorationGeometry.Unbroken(ancestor.Rect);
                (borderRect, paddingRect) = Paint.DecorationPixelSnapping.SnapPaddingBox(g, borderRect, paddingRect,
                    owned.HasLeftEdge, owned.HasTopEdge, owned.HasRightEdge, owned.HasBottomEdge);
            }

            var oneAxis = ClipsOneAxisOnly(overflowBox);
            var edge = OverflowClipEdgeOf(overflowBox, borderRect, paddingRect);
            var rect = oneAxis ? OpenUnclippedAxes(overflowBox, edge) : edge;

            OverflowClipCurve? curve = null;
            if (overflowBox.IsRounded && !oneAxis)
            {
                curve = new OverflowClipCurve(edge, OverflowClipRadiiOf(overflowBox, borderRect, paddingRect, edge));
            }

            return (rect, curve);
        }

        /// <summary>
        /// Pushes a rounded-path clip for <paramref name="radii"/> over <paramref name="rect"/>, shared by
        /// <see cref="ClipGraphicsByOverflow"/> (a precomputed <see cref="OverflowClipCurve"/>) and
        /// <see cref="TryPushOverflowClip"/> (a live <see cref="CssBox.ComputeInnerRadii"/> call) so the
        /// two don't each carry their own copy of the same "build the path, push it, count it" sequence.
        /// </summary>
        /// <returns>1 if a clip was pushed, 0 if <paramref name="radii"/> has no rounded corner</returns>
        private static int PushRoundedClipIfRounded(Canvas g, Rect rect, BorderRadii radii, bool mayDrawText = true)
        {
            if (!radii.IsRounded) return 0;

            using var path = GetRoundRect(g, rect,
                radii);
            if (mayDrawText)
                g.PushClip(path);
            else
                g.PushClipAroundShapes(path);

            return 1;
        }

        /// <summary>
        /// The rectangle an <c>overflow: hidden</c> box clips its descendants to: its padding edge, not
        /// its content edge (<see href="https://www.w3.org/TR/css-overflow-3/#overflow-propagation">CSS
        /// Overflow Level 3 §2</see>).
        /// </summary>
        /// <param name="box">the clipping box, read for its border widths only</param>
        /// <param name="borderBox">
        /// that box's border box. Passed in rather than read from <paramref name="box"/> so a caller
        /// holding a <see cref="Fragments.BoxGeometrySnapshot"/> can resolve the box at the position that
        /// snapshot recorded — the same box can be shown at several places in one document.
        /// </param>
        internal static Rect PaddingEdgeOf(CssBox box, Rect borderBox) => Rect.FromLTRB(
            borderBox.Left + box.ActualBorderLeftWidth,
            borderBox.Top + box.ActualBorderTopWidth,
            borderBox.Right - box.ActualBorderRightWidth,
            borderBox.Bottom - box.ActualBorderBottomWidth);

        /// <summary>
        /// Whether <paramref name="box"/> clips only one axis (<c>overflow-x: clip</c> beside a
        /// <c>visible</c> <c>overflow-y</c>, or the reverse), leaving the other open.
        /// </summary>
        internal static bool ClipsOneAxisOnly(CssBox box) =>
            box.ClipsOverflowHorizontally != box.ClipsOverflowVertically;

        /// <summary>
        /// The border box a clipping box's <c>overflow</c> clip is measured from: its <paramref name="bounds"/>, or for an
        /// inline-level box the union of its line rectangles when it has any. Such a box is flowed into a line rather than
        /// laid out as a box and so never has a <c>Location</c>/<c>Size</c> assigned; its bounds read as the unassigned
        /// <c>(0,0,0,0)</c> and the clip came out with zero area, clipping every word inside it away. Shared by every
        /// builder of fragments, so a box clips the same wherever it is shown.
        /// </summary>
        /// <param name="bounds">the box's own bounds</param>
        /// <param name="isInline">whether the box is inline-level</param>
        /// <param name="rectangles">the box's per-line rectangles</param>
        internal static Rect ClipSourceBoundsOf(Rect bounds, bool isInline, Dictionary<CssLineBox, Rect> rectangles)
        {
            if (!isInline || rectangles.Count == 0) return bounds;

            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;

            foreach (var rect in rectangles.Values)
            {
                left = Math.Min(left, rect.Left);
                top = Math.Min(top, rect.Top);
                right = Math.Max(right, rect.Right);
                bottom = Math.Max(bottom, rect.Bottom);
            }

            return Rect.FromLTRB(left, top, right, bottom);
        }

        /// <summary>
        /// The clip <paramref name="box"/>'s <c>overflow</c> imposes on its content, given where its border box
        /// sits: the padding edge (with any axis it does not clip opened out), the corner curve that bounds it,
        /// and whether an axis was opened. The one place this is derived, shared by every builder of fragments.
        /// </summary>
        /// <param name="box">the clipping box, read for its border widths, radii and clipped axes</param>
        /// <param name="borderBox">its border box, in whatever space the caller's fragments are in</param>
        internal static (Rect ClipRect, BorderRadii? Radii, bool AxisOpen) OverflowClipGeometryOf(CssBox box, Rect borderBox)
        {
            var paddingRect = PaddingEdgeOf(box, borderBox);
            var edge = OverflowClipEdgeOf(box, borderBox, paddingRect);
            // A box clipping a single axis has that axis's edge only: the other is opened out, and the
            // corners no longer bound the clip.
            var axisOpen = ClipsOneAxisOnly(box);
            var radii = box.IsRounded && !axisOpen
                ? OverflowClipRadiiOf(box, borderBox, paddingRect, edge)
                : (BorderRadii?)null;
            return (axisOpen ? OpenUnclippedAxes(box, edge) : edge, radii, axisOpen);
        }

        /// <summary>
        /// The overflow clip edge of <paramref name="box"/> (css-overflow-4 §3.1): the edge of its visual box (the padding box unless
        /// <c>overflow-clip-margin</c> names another) pushed out by the margin, on each axis that is cut without scrolling. An axis that scrolls, or is
        /// not clipped, keeps the padding edge it always had.
        /// </summary>
        /// <param name="box">the clipping box</param>
        /// <param name="borderBox">its border box (as the caller places it)</param>
        /// <param name="paddingRect">its padding edge in the same space</param>
        internal static Rect OverflowClipEdgeOf(CssBox box, Rect borderBox, Rect paddingRect)
        {
            var clipX = box.ClipsAxisAtTheOverflowClipEdge(horizontal: true);
            var clipY = box.ClipsAxisAtTheOverflowClipEdge(horizontal: false);
            if (!clipX && !clipY) return paddingRect;

            var left = box.OverflowClipMarginOf(PhysicalSide.Left);
            var top = box.OverflowClipMarginOf(PhysicalSide.Top);
            var right = box.OverflowClipMarginOf(PhysicalSide.Right);
            var bottom = box.OverflowClipMarginOf(PhysicalSide.Bottom);

            if (IsInitialMargin(left) && IsInitialMargin(top) && IsInitialMargin(right) && IsInitialMargin(bottom)) return paddingRect;

            return Rect.FromLTRB(
                clipX ? SideEdgeOf(box, borderBox, paddingRect, PhysicalSide.Left, left) : paddingRect.Left,
                clipY ? SideEdgeOf(box, borderBox, paddingRect, PhysicalSide.Top, top) : paddingRect.Top,
                clipX ? SideEdgeOf(box, borderBox, paddingRect, PhysicalSide.Right, right) : paddingRect.Right,
                clipY ? SideEdgeOf(box, borderBox, paddingRect, PhysicalSide.Bottom, bottom) : paddingRect.Bottom);
        }

        private static bool IsInitialMargin((OverflowClipVisualBox VisualBox, double Margin) margin) => margin.VisualBox == OverflowClipVisualBox.PaddingBox && margin.Margin == 0;

        // Where one side of the clip edge is: the side of the visual box it names, moved out by the margin (in by it, when it is negative).
        private static double SideEdgeOf(CssBox box, Rect borderBox, Rect paddingRect, PhysicalSide side, (OverflowClipVisualBox VisualBox, double Margin) margin)
        {
            var (visualBox, length) = margin;

            return side switch
            {
                PhysicalSide.Left => (visualBox == OverflowClipVisualBox.BorderBox ? borderBox.Left : visualBox == OverflowClipVisualBox.ContentBox ? paddingRect.Left + box.ActualPaddingLeft : paddingRect.Left) - length,
                PhysicalSide.Top => (visualBox == OverflowClipVisualBox.BorderBox ? borderBox.Top : visualBox == OverflowClipVisualBox.ContentBox ? paddingRect.Top + box.ActualPaddingTop : paddingRect.Top) - length,
                PhysicalSide.Right => (visualBox == OverflowClipVisualBox.BorderBox ? borderBox.Right : visualBox == OverflowClipVisualBox.ContentBox ? paddingRect.Right - box.ActualPaddingRight : paddingRect.Right) + length,
                _ => (visualBox == OverflowClipVisualBox.BorderBox ? borderBox.Bottom : visualBox == OverflowClipVisualBox.ContentBox ? paddingRect.Bottom - box.ActualPaddingBottom : paddingRect.Bottom) + length
            };
        }

        /// <summary>
        /// The corner radii of the overflow clip edge (css-overflow-4 §3.2): shaped as an outer box-shadow spread by the same offset from the border edge
        /// (css-backgrounds-3 §6.1.1), each side of a corner taking the offset of the side it is on, then reduced to fit the edge as every set of radii is
        /// (§5.5). A corner that is square stays square.
        /// </summary>
        internal static BorderRadii OverflowClipRadiiOf(CssBox box, Rect borderBox, Rect paddingRect, Rect edge)
        {
            var outer = box.DerivedStyle.ComputeRadii(borderBox);

            // How far each side of the edge lies outside the border edge (inside, when negative).
            double left = borderBox.Left - edge.Left, top = borderBox.Top - edge.Top, right = edge.Right - borderBox.Right, bottom = edge.Bottom - borderBox.Bottom;

            return DerivedStyle.ApplyCornerOverlap(edge,
                Spread(outer.TLX, left), Spread(outer.TLY, top), Spread(outer.TRX, right), Spread(outer.TRY, top),
                Spread(outer.BRX, right), Spread(outer.BRY, bottom), Spread(outer.BLX, left), Spread(outer.BLY, bottom))
                .WithShapes(outer.KTL, outer.KTR, outer.KBR, outer.KBL);
        }

        // css-backgrounds-3 §6.1.1: a radius spread by s keeps its shape as s grows: r + s when r is at least s, and r + s * (1 + (r/s - 1)^3) when it is
        // less (so a small radius does not jump to a large one), and none at all when the corner is square. A shrink is the radius less the distance.
        private static double Spread(double radius, double spread)
        {
            if (radius <= 0) return 0;
            if (spread == 0) return radius;
            if (spread < 0) return Math.Max(0, radius + spread);
            if (radius >= spread) return radius + spread;

            var ratio = (radius / spread) - 1;
            return radius + (spread * (1 + (ratio * ratio * ratio)));
        }

        /// <summary>
        /// <paramref name="paddingEdge"/> with each axis <paramref name="box"/> does not clip opened out to
        /// the extent of any page, so one clip rectangle serves a box that clips a single axis
        /// (css-overflow-3 §3.2: <c>clip</c> cuts at the overflow clip edge on its own axis only).
        /// </summary>
        internal static Rect OpenUnclippedAxes(CssBox box, Rect paddingEdge)
        {
            const double Open = 1e6;

            var left = box.ClipsOverflowHorizontally ? paddingEdge.Left : -Open;
            var right = box.ClipsOverflowHorizontally ? paddingEdge.Right : Open;
            var top = box.ClipsOverflowVertically ? paddingEdge.Top : -Open;
            var bottom = box.ClipsOverflowVertically ? paddingEdge.Bottom : Open;

            return Rect.FromLTRB(left, top, right, bottom);
        }

        /// <summary>
        /// Pushes the <c>overflow: hidden</c> clip of every fragment in <paramref name="ancestors"/> that
        /// has one, in order. Used when painting a box that <see cref="Paint.StackingOrder.Flatten"/>
        /// hoisted past one or more plain ancestor boxes for stacking-context z-order purposes - since it
        /// paints via the claiming stacking context's own paint loop rather than those ancestors' own
        /// (nested) paint calls, their overflow clipping isn't already active on the graphics
        /// clip stack the way it would be for normally-painted content, and must be applied explicitly
        /// here instead. <paramref name="ancestors"/> is the exact, already-known chain of ancestor
        /// fragments between the claiming stacking context and the box being painted (see
        /// <see cref="Paint.StackingOrder.StackingParticipant"/>), so no walk is needed; each ancestor is
        /// checked directly. Each ancestor's rectangle comes from its own fragment rather than its live
        /// box, so this is correct even for a box shown at more than one place at once - a hoisted
        /// participant inside a repeated table header - whose live box only ever carries whichever page
        /// positioned it last (#345).
        /// </summary>
        /// <remarks>
        /// An ancestor that is not on <paramref name="box"/>'s clipping containing-block chain
        /// (<see cref="DomUtils.IsOnClippingChainOf"/>) is skipped: an <c>overflow: hidden</c> box between
        /// an absolutely positioned box and its positioned containing block does not clip it.
        /// </remarks>
        /// <param name="g">the graphics to clip</param>
        /// <param name="box">the hoisted box being painted</param>
        /// <param name="ancestors">the ancestor fragments it was hoisted past, outer to inner</param>
        /// <param name="snapToCssPixels">
        /// whether each clip is snapped to whole CSS pixels, like the ancestor's own decorations
        /// (<c>PdfGenerateConfig.SnapBoxDecorationsToCssPixels</c>)
        /// </param>
        /// <returns>the number of clips actually pushed (callers must pop exactly this many afterward)</returns>
        public static int PushAncestorOverflowClips(Canvas g, CssBox box, IReadOnlyList<BoxFragment> ancestors,
            bool snapToCssPixels = false)
        {
            var pushed = 0;
            foreach (var ancestor in ancestors)
            {
                if (!DomUtils.ClipsItsOverflow(ancestor.Box) || !DomUtils.IsOnClippingChainOf(box, ancestor.Box))
                    continue;

                pushed += TryPushOverflowClip(g, ancestor, snapToCssPixels);
            }
            return pushed;
        }


        /// <summary>
        /// Creates a rounded rectangle path. Each corner has separate horizontal (X) and vertical (Y) radii,
        /// supporting elliptical corners per the CSS border-radius spec.
        /// <code>
        /// NW-----NE
        ///  |       |
        /// SW-----SE
        /// </code>
        /// </summary>
        /// <remarks>
        /// <paramref name="rect"/> and every radius are in the caller's layout-space units (the same
        /// <c>PixelsPerInch</c>-inflated space as <see cref="Dom.CssBox"/> geometry) — unlike every other
        /// <see cref="Canvas"/> draw primitive (<c>DrawLine</c>, <c>DrawRectangle</c>,
        /// <c>PushClip(Rect)</c>), neither <c>Canvas.PushClip(GraphicsPath)</c> nor <c>DrawPath</c>
        /// ever divides a path's coordinates by <see cref="Canvas.PixelsPerPoint"/> before handing them
        /// to the backend (that division is left to whichever ambient <c>PushTransform</c> is active for
        /// SVG/glyph-outline paths, the other consumers of an <see cref="GraphicsPath"/> — see
        /// <c>PdfGraphicsPath</c>'s own comment). A box-geometry path has no such
        /// ambient transform, so this method divides by <see cref="Canvas.PixelsPerPoint"/> itself,
        /// once, before building the path (issue #812).
        /// </remarks>
        /// <summary>
        /// <see cref="GetRoundRect(Canvas, Rect, double, double, double, double, double, double, double, double)"/> for a box's
        /// <see cref="BorderRadii"/>, which also carry the corners' <c>corner-shape</c>: a box with every corner round takes
        /// the plain elliptical-arc path, any other shape the superellipse one (<see cref="CornerShapePath"/>).
        /// </summary>
        public static GraphicsPath GetRoundRect(Canvas g, Rect rect, BorderRadii r)
        {
            if (!r.HasShapes)
                return GetRoundRect(g, rect, r.TLX, r.TLY, r.TRX, r.TRY, r.BRX, r.BRY, r.BLX, r.BLY);

            var ppp = g.PixelsPerPoint;
            var scaled = new BorderRadii(r.TLX / ppp, r.TLY / ppp, r.TRX / ppp, r.TRY / ppp, r.BRX / ppp, r.BRY / ppp, r.BLX / ppp, r.BLY / ppp)
                .WithShapes(r.KTL, r.KTR, r.KBR, r.KBL);
            var path = g.GetGraphicsPath();
            CornerShapePath.AddRectangle(path, new Rect(rect.Left / ppp, rect.Top / ppp, rect.Width / ppp, rect.Height / ppp), scaled);
            return path;
        }

        public static GraphicsPath GetRoundRect(Canvas g, Rect rect,
            double nwX, double nwY, double neX, double neY,
            double seX, double seY, double swX, double swY)
        {
            var ppp = g.PixelsPerPoint;
            rect = new Rect(rect.Left / ppp, rect.Top / ppp, rect.Width / ppp, rect.Height / ppp);
            nwX /= ppp; nwY /= ppp; neX /= ppp; neY /= ppp;
            seX /= ppp; seY /= ppp; swX /= ppp; swY /= ppp;

            var path = g.GetGraphicsPath();
            path.AddRoundedRectangle(rect, nwX, nwY, neX, neY, seX, seY, swX, swY);
            return path;
        }
    }
}
