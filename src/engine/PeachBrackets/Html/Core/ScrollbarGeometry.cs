using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using System;

namespace PeachBrackets.Html
{
    /// <summary>One scrollbar of a scroll container: its track along an edge of the scrollport, and the thumb in it, in the layout units of the container's frame.</summary>
    /// <param name="Track">the track</param>
    /// <param name="Thumb">the thumb: as long as the share of the content the scrollport shows, and as far along as the content is scrolled</param>
    /// <param name="ShowsTrack">whether the track is drawn (it is for <c>scroll</c>, and for <c>auto</c> once there is something to scroll to)</param>
    internal readonly record struct ScrollbarShape(Rect Track, Rect Thumb, bool ShowsTrack);

    /// <summary>
    /// Where the overlay scrollbars of a scroll container are: thin bars along the right and bottom edges of its scrollport, drawn over its content and taking no room
    /// from it. The painter draws them and the hit test finds them from the same numbers.
    /// </summary>
    internal static class ScrollbarGeometry
    {
        // The width of the bars, their distance from the edge and the shortest thumb, in CSS pixels.
        internal const double Thickness = 8;
        internal const double ThinThickness = 5;
        internal const double Inset = 2;
        internal const double MinimumThumb = 24;

        /// <summary>The scrollbars of <paramref name="box"/> as it is scrolled in <paramref name="state"/>; each is null when the container shows none on that axis.</summary>
        internal static (ScrollbarShape? Vertical, ScrollbarShape? Horizontal) Of(CssBox box, ScrollState state, double layoutUnitsPerPoint)
        {
            // overflow: hidden scrolls by script only and shows no bar; auto shows one only when there is something to scroll to; scrollbar-width: none shows none either.
            if (box.Overflow.Value is not (Overflow.Auto or Overflow.Scroll))
                return (null, null);

            var width = box.ScrollbarWidth?.Trim().ToLowerInvariant();
            if (width == "none")
                return (null, null);

            var verticalAlways = box.OverflowY.Value == Overflow.Scroll;
            var horizontalAlways = box.OverflowX.Value == Overflow.Scroll;
            var vertical = box.OverflowY.Value is Overflow.Auto or Overflow.Scroll && (state.MaxY - state.MinY > 0 || verticalAlways);
            var horizontal = box.OverflowX.Value is Overflow.Auto or Overflow.Scroll && (state.MaxX - state.MinX > 0 || horizontalAlways);
            if (!vertical && !horizontal)
                return (null, null);

            var px = Length.PointsPerPx * layoutUnitsPerPoint;
            var thickness = (width == "thin" ? ThinThickness : Thickness) * px;
            var inset = Inset * px;
            var port = state.Port;
            var reserved = vertical && horizontal ? thickness + inset : 0;

            ScrollbarShape? v = null, h = null;
            if (vertical)
            {
                var x = port.Right - thickness - inset;
                var length = Math.Max(0, port.Height - reserved - (2 * inset));
                var (start, size) = Thumb(length, state.ContentHeight, port.Height, state.Y - state.MinY, state.MaxY - state.MinY, MinimumThumb * px);
                v = new ScrollbarShape(new Rect(x, port.Y + inset, thickness, length), new Rect(x, port.Y + inset + start, thickness, size), verticalAlways || state.MaxY - state.MinY > 0);
            }

            if (horizontal)
            {
                var y = port.Bottom - thickness - inset;
                var length = Math.Max(0, port.Width - reserved - (2 * inset));
                var (start, size) = Thumb(length, state.ContentWidth, port.Width, state.X - state.MinX, state.MaxX - state.MinX, MinimumThumb * px);
                h = new ScrollbarShape(new Rect(port.X + inset, y, length, thickness), new Rect(port.X + inset + start, y, size, thickness), horizontalAlways || state.MaxX - state.MinX > 0);
            }

            return (v, h);
        }

        // Where the thumb is along a track of <length>: as long as the share of the content the scrollport shows, and as far along as the content is scrolled.
        private static (double Start, double Size) Thumb(double length, double content, double port, double offset, double max, double minimum)
        {
            if (!(content > 0) || !(length > 0)) return (0, Math.Max(0, length));

            var size = Math.Clamp(length * (port / content), Math.Min(minimum, length), length);
            var start = max > 0 ? (length - size) * (offset / max) : 0;
            return (start, size);
        }
    }
}
