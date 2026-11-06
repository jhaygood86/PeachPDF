using PeachBrackets.Css;
using PeachBrackets.Html.Fragments;
using PeachDrawing.Core;

namespace PeachBrackets.Html.Paint
{
    /// <summary>
    /// The scrollbars of a scroll container: thin overlay bars along the right and bottom edges of its padding box, drawn over its content (see
    /// <see cref="ScrollbarGeometry"/>), so a scrolled container shows that it is scrolled and where.
    /// </summary>
    internal sealed partial class FragmentPainter
    {
        private void PaintScrollbars(Canvas g, BoxFragment fragment)
        {
            var box = fragment.Box;
            if (box.Overflow.Value is not (Overflow.Auto or Overflow.Scroll) || container.View.ScrollStateOf(container, box) is not { } state)
                return;

            var (vertical, horizontal) = ScrollbarGeometry.Of(box, state, container.RenderContext.LayoutUnitsPerPoint);
            if (vertical is null && horizontal is null)
                return;

            // scrollbar-color: the thumb, then the track; auto is the built-in pair.
            var thumbColor = PaintColor.FromArgb(140, 70, 70, 70);
            var trackColor = PaintColor.FromArgb(36, 0, 0, 0);
            if (box.ScrollbarColor is { } written && ScrollGrammar.TryParseScrollbarColor(written, out var thumbText, out var trackText) && thumbText is not null)
            {
                var parser = new Parse.CssValueParser(container.RenderContext);
                thumbColor = parser.GetActualColor(thumbText);
                trackColor = parser.GetActualColor(trackText);
            }

            var thumb = g.GetSolidBrush(thumbColor);
            var track = g.GetSolidBrush(trackColor);
            foreach (var bar in new[] { vertical, horizontal })
            {
                if (bar is not { } shape) continue;

                if (shape.ShowsTrack) g.DrawRectangle(track, shape.Track.X, shape.Track.Y, shape.Track.Width, shape.Track.Height);
                g.DrawRectangle(thumb, shape.Thumb.X, shape.Thumb.Y, shape.Thumb.Width, shape.Thumb.Height);
            }
        }
    }
}
