using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachDrawing.Core;
using System;

namespace PeachBrackets.Html.Paint
{
    /// <summary>
    /// The scrollbars of a scroll container: thin overlay bars along the right and bottom edges of its padding box, drawn over its content. A host that has a
    /// native scrollbar of its own for the container can turn them off by not showing the container as a scroll container; these are the engine's own, so a
    /// scrolled container shows that it is scrolled and where.
    /// </summary>
    internal sealed partial class FragmentPainter
    {
        // The width of the bars, and the shortest thumb, in CSS pixels.
        private const double ScrollbarThickness = 8;
        private const double ScrollbarInset = 2;
        private const double MinimumThumb = 24;

        private void PaintScrollbars(Canvas g, BoxFragment fragment)
        {
            var box = fragment.Box;

            // overflow: hidden scrolls by script only, and shows no bar; auto shows one only when there is something to scroll to.
            if (box.Overflow.Value is not (Overflow.Auto or Overflow.Scroll) || container.View.ScrollStateOf(container, box) is not { } state)
                return;

            var always = box.Overflow.Value == Overflow.Scroll;
            var vertical = box.OverflowY.Value is Overflow.Auto or Overflow.Scroll && (state.MaxY > 0 || (always && box.OverflowY.Value == Overflow.Scroll));
            var horizontal = box.OverflowX.Value is Overflow.Auto or Overflow.Scroll && (state.MaxX > 0 || (always && box.OverflowX.Value == Overflow.Scroll));
            if (!vertical && !horizontal)
                return;

            var px = Length.PointsPerPx * container.RenderContext.LayoutUnitsPerPoint;
            var thickness = ScrollbarThickness * px;
            var inset = ScrollbarInset * px;
            var port = state.Port;
            var thumb = g.GetSolidBrush(PaintColor.FromArgb(140, 70, 70, 70));
            var track = g.GetSolidBrush(PaintColor.FromArgb(36, 0, 0, 0));
            var reserved = vertical && horizontal ? thickness + inset : 0;

            if (vertical)
            {
                var x = port.Right - thickness - inset;
                var length = port.Height - reserved - (2 * inset);
                if (always || state.MaxY > 0) g.DrawRectangle(track, x, port.Y + inset, thickness, length);

                var (start, size) = Thumb(length, state.ContentHeight, port.Height, state.Y, state.MaxY, MinimumThumb * px);
                g.DrawRectangle(thumb, x, port.Y + inset + start, thickness, size);
            }

            if (horizontal)
            {
                var y = port.Bottom - thickness - inset;
                var length = port.Width - reserved - (2 * inset);
                if (always || state.MaxX > 0) g.DrawRectangle(track, port.X + inset, y, length, thickness);

                var (start, size) = Thumb(length, state.ContentWidth, port.Width, state.X, state.MaxX, MinimumThumb * px);
                g.DrawRectangle(thumb, port.X + inset + start, y, size, thickness);
            }
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
