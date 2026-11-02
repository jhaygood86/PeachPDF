using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using System;
using System.Globalization;

namespace PeachBrackets.Html.Paint.Content
{
    /// <summary>
    /// Paints a <c>&lt;progress&gt;</c> bar or a <c>&lt;meter&gt;</c> gauge: the box's own border and background, a track, and a fill to the element's
    /// value. The fill is the <c>accent-color</c> for a progress bar (a blue for <c>auto</c>, as browsers use); a meter takes the traffic-light color the HTML
    /// standard's rendering section describes - green in the optimum region, yellow next to it, red beyond - as the default.
    /// </summary>
    /// <remarks>
    /// An indeterminate progress bar (no <c>value</c>) draws an empty track. The element's fallback content is not painted: the UA style sheet gives it
    /// no size, so it takes no room (see <see cref="CssDefaults"/>).
    /// </remarks>
    internal sealed class MeterFragmentPainter : IFragmentContentPainter
    {
        private static readonly PaintColor Track = PaintColor.FromArgb(255, 224, 224, 224);
        private static readonly PaintColor Accent = PaintColor.FromArgb(255, 0, 117, 255);
        private static readonly PaintColor Good = PaintColor.FromArgb(255, 31, 163, 71);
        private static readonly PaintColor Caution = PaintColor.FromArgb(255, 245, 189, 31);
        private static readonly PaintColor Bad = PaintColor.FromArgb(255, 214, 54, 56);

        /// <summary>Whether a box is a <c>progress</c> or <c>meter</c> element, which this painter draws.</summary>
        internal static bool IsMeterLike(CssBox box) =>
            box.HtmlTag is { Name: { Length: 5 or 8 } name } && (name.Equals("meter", StringComparison.OrdinalIgnoreCase) || name.Equals("progress", StringComparison.OrdinalIgnoreCase));

        public void Paint(FragmentPainter painter, Canvas g, BoxFragment fragment)
        {
            var box = fragment.Box;
            var rect = fragment.PrimaryRect;
            if (rect is not { Width: > 0, Height: > 0 })
            {
                painter.PaintBoxContent(g, fragment);
                return;
            }

            FormFieldChrome.PaintBorderAndBackground(g, box, rect);

            var isMeter = box.HtmlTag!.Name.Equals("meter", StringComparison.OrdinalIgnoreCase);
            var content = new Rect(
                rect.X + box.ActualBorderLeftWidth + box.ActualPaddingLeft,
                rect.Y + box.ActualBorderTopWidth + box.ActualPaddingTop,
                Math.Max(0, rect.Width - box.ActualBorderLeftWidth - box.ActualBorderRightWidth - box.ActualPaddingLeft - box.ActualPaddingRight),
                Math.Max(0, rect.Height - box.ActualBorderTopWidth - box.ActualBorderBottomWidth - box.ActualPaddingTop - box.ActualPaddingBottom));
            if (content is not { Width: > 0, Height: > 0 })
                return;

            var radius = content.Height / 2;
            using (var track = g.GetSolidBrush(Track))
            using (var trackPath = RenderUtils.GetRoundRect(g, content, radius, radius, radius, radius, radius, radius, radius, radius))
                g.DrawPath(track, trackPath);

            var (fraction, color) = isMeter ? MeterFill(box) : ProgressFill(box);
            if (fraction is not { } f || f <= 0)
                return;

            var fill = new Rect(content.X, content.Y, Math.Max(content.Width * Math.Min(f, 1), content.Height), content.Height);
            if (fill.Width > content.Width)
                fill = content;

            using var brush = g.GetSolidBrush(color);
            using var path = RenderUtils.GetRoundRect(g, fill, radius, radius, radius, radius, radius, radius, radius, radius);
            g.DrawPath(brush, path);
        }

        private static double? Number(CssBox box, string name) =>
            double.TryParse(box.GetAttribute(name, string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;

        private static (double? Fraction, PaintColor Color) ProgressFill(CssBox box)
        {
            var accent = box.ActualAccentColor ?? Accent;
            if (Number(box, "value") is not { } value || value < 0)
                return (null, accent);

            var max = Number(box, "max") is { } m and > 0 ? m : 1;
            return (Math.Min(value, max) / max, accent);
        }

        // HTML's rendering of a meter: the bar is full to the value between min and max, and its color says how good that is.
        private static (double? Fraction, PaintColor Color) MeterFill(CssBox box)
        {
            var min = Number(box, "min") ?? 0;
            var max = Math.Max(Number(box, "max") ?? 1, min);
            var value = Math.Clamp(Number(box, "value") ?? 0, min, max);
            var low = Math.Clamp(Number(box, "low") ?? min, min, max);
            var high = Math.Clamp(Number(box, "high") ?? max, low, max);
            var optimum = Math.Clamp(Number(box, "optimum") ?? (min + ((max - min) / 2)), min, max);

            var fraction = max > min ? (value - min) / (max - min) : 0;
            return (fraction, MeterColor(value, low, high, optimum));
        }

        /// <summary>The color of a meter's fill: green when the value is in the region the optimum is in, yellow in the next one, red in the farthest.</summary>
        internal static PaintColor MeterColor(double value, double low, double high, double optimum)
        {
            // The three regions are below low, between low and high, and above high; the optimum says which is best.
            static int Region(double v, double low, double high) => v < low ? 0 : v > high ? 2 : 1;
            var best = Region(optimum, low, high);
            var distance = Math.Abs(Region(value, low, high) - best);
            return distance == 0 ? Good : distance == 1 ? Caution : Bad;
        }
    }
}
