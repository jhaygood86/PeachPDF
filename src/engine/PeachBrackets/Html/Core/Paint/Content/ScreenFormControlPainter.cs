using PeachBrackets.Annotations;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Handlers;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using System;

namespace PeachBrackets.Html.Paint.Content
{
    /// <summary>
    /// Paints how a form control looks for a host that shows the page (<see cref="HtmlDocumentOptions.PaintFormControls"/>): a check box and its check
    /// mark, a radio button and its dot, a text field with its value or its placeholder, a button with its label, a drop-down with the chosen option
    /// and an arrow, and a range with its track and thumb. The look is drawn from the control's own resolved CSS - its border, background, padding,
    /// font and colour, <c>accent-color</c> - exactly as the control's box would be, so everything an author styles applies.
    /// </summary>
    /// <remarks>
    /// A PDF is not drawn this way: without the option a control keeps the plain box it has always been, and with
    /// <see cref="HtmlDocumentOptions.CollectFormFields"/> the widget draws its own appearance (<see cref="FormFieldFragmentPainter"/>).
    /// </remarks>
    internal sealed class ScreenFormControlPainter : IFragmentContentPainter
    {
        public void Paint(FragmentPainter painter, Canvas g, BoxFragment fragment)
        {
            var box = (CssBoxFormField)fragment.Box;
            var rect = fragment.PrimaryRect;
            if (rect is not { Width: > 0, Height: > 0 })
            {
                painter.PaintBoxContent(g, fragment);
                return;
            }

            var tag = box.HtmlTag!.Name;
            var type = box.GetAttribute("type", "text").ToLowerInvariant();
            var disabled = box.HtmlTag.HasAttribute("disabled");

            if (tag.Equals("select", StringComparison.OrdinalIgnoreCase))
            {
                FormFieldChrome.PaintBorderAndBackground(g, box, rect);
                PaintSelect(g, box, rect);
            }
            else if (type == "checkbox")
            {
                FormFieldChrome.PaintBorderAndBackground(g, box, rect);
                FormFieldChrome.PaintCheckboxGlyph(g, box, rect, box.HtmlTag.HasAttribute("checked"));
            }
            else if (type == "radio")
            {
                FormFieldChrome.PaintRadioBackground(g, box, rect);
                FormFieldChrome.PaintRadioGlyph(g, box, rect, box.HtmlTag.HasAttribute("checked"));
            }
            else if (type is "button" or "submit" or "reset")
            {
                FormFieldChrome.PaintBorderAndBackground(g, box, rect);
                var label = box.GetAttribute("value", string.Empty);
                if (label.Length == 0)
                    label = type switch { "submit" => "Submit", "reset" => "Reset", _ => string.Empty };
                PaintText(g, box, rect, label, centered: true, box.ActualColor, disabled);
            }
            else if (type == "range")
            {
                FormFieldChrome.PaintBorderAndBackground(g, box, rect);
                PaintRange(g, box, rect);
            }
            else if (type is "hidden" or "image")
            {
                painter.PaintBoxContent(g, fragment);
            }
            else
            {
                FormFieldChrome.PaintBorderAndBackground(g, box, rect);
                var value = box.GetAttribute("value", string.Empty);
                if (type == "password")
                    value = new string('•', value.Length);

                if (value.Length > 0)
                {
                    PaintText(g, box, rect, value, centered: false, box.ActualColor, disabled);
                }
                else if (box.GetAttribute("placeholder", string.Empty) is { Length: > 0 } placeholder)
                {
                    var style = box.ResolvedPlaceholderStyle ?? box;
                    PaintText(g, box, rect, placeholder, centered: false, style.ActualColor, disabled);
                }
            }

            // Like any replaced control, it takes an outline (css-ui-4 section 3.1), painted after its look.
            var (overflowClip, overflowClipCurve) = painter.OverflowClipOf(g, fragment);
            var overflowClipRecorded = painter.PushOverflowClip(overflowClip, overflowClipCurve);
            painter.PaintReplacedOutline(g, box, rect);
            painter.PopOverflowClip(overflowClipRecorded);
        }

        private static Rect ContentRect(CssBox box, Rect rect) => new(
            rect.X + box.ActualBorderLeftWidth + box.ActualPaddingLeft,
            rect.Y + box.ActualBorderTopWidth + box.ActualPaddingTop,
            Math.Max(0, rect.Width - box.ActualBorderLeftWidth - box.ActualBorderRightWidth - box.ActualPaddingLeft - box.ActualPaddingRight),
            Math.Max(0, rect.Height - box.ActualBorderTopWidth - box.ActualBorderBottomWidth - box.ActualPaddingTop - box.ActualPaddingBottom));

        private static PaintColor Dimmed(PaintColor color) => PaintColor.FromArgb(color.A / 2, color.R, color.G, color.B);

        private static void PaintText(Canvas g, CssBox box, Rect rect, string text, bool centered, PaintColor color, bool disabled)
        {
            var content = ContentRect(box, rect);
            if (content is not { Width: > 0, Height: > 0 })
                return;

            var font = box.ActualFont;
            var width = Math.Min(g.MeasureString(text, font).Width, content.Width);
            var x = centered ? content.X + ((content.Width - width) / 2) : content.X;
            var y = content.Y + Math.Max((content.Height - font.Height) / 2, 0);

            g.PushClip(content);
            g.DrawString(text, font, disabled ? Dimmed(color) : color, new PaintPoint(x, y), new Size(content.Width, font.Height),
                fontPalette: box.ActualFontPalette, features: box.ActualTextShapingFeatures);
            g.PopClip();
        }

        private static void PaintSelect(Canvas g, CssBox box, Rect rect)
        {
            var content = ContentRect(box, rect);
            var arrowWidth = Math.Min(content.Height, content.Width / 3);
            var classification = FormFieldMapper.Classify(box);
            var label = string.Empty;
            if (classification.Options is { } options)
            {
                foreach (var option in options)
                {
                    if (option.Selected)
                    {
                        label = option.Label;
                        break;
                    }
                }

                if (label.Length == 0 && options.Count > 0)
                    label = options[0].Label;
            }

            PaintText(g, box, new Rect(rect.X, rect.Y, rect.Width - arrowWidth, rect.Height), label, centered: false, box.ActualColor, box.HtmlTag!.HasAttribute("disabled"));

            if (arrowWidth <= 0)
                return;

            // A downward chevron, in the text colour, in the room kept at the right edge.
            var size = arrowWidth * 0.4;
            var cx = content.X + content.Width - (arrowWidth / 2);
            var cy = content.Y + (content.Height / 2);
            var pen = g.GetPen(box.ActualColor);
            pen.Width = Math.Max(size / 4, 1);
            g.DrawLine(pen, cx - size, cy - (size / 2), cx, cy + (size / 2));
            g.DrawLine(pen, cx, cy + (size / 2), cx + size, cy - (size / 2));
        }

        private static void PaintRange(Canvas g, CssBox box, Rect rect)
        {
            var content = ContentRect(box, rect);
            if (content is not { Width: > 0, Height: > 0 })
                return;

            double Number(string name, double fallback) =>
                double.TryParse(box.GetAttribute(name, string.Empty), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

            var min = Number("min", 0);
            var max = Number("max", 100);
            if (max <= min)
                max = min + 100;
            var value = Math.Clamp(Number("value", min + ((max - min) / 2)), min, max);
            var fraction = (value - min) / (max - min);

            var accent = box.ActualAccentColor ?? box.ActualColor;
            var thumb = Math.Min(content.Height, content.Width / 2);
            var trackHeight = Math.Max(thumb / 4, 1);
            var cy = content.Y + (content.Height / 2);
            var left = content.X + (thumb / 2);
            var span = Math.Max(content.Width - thumb, 0);
            var thumbX = left + (span * fraction);

            using (var track = g.GetSolidBrush(PaintColor.FromArgb(255, 200, 200, 200)))
                g.DrawRectangle(track, left, cy - (trackHeight / 2), span, trackHeight);
            using (var filled = g.GetSolidBrush(accent))
                g.DrawRectangle(filled, left, cy - (trackHeight / 2), span * fraction, trackHeight);
            var thumbRect = new Rect(thumbX - (thumb / 2), cy - (thumb / 2), thumb, thumb);
            using var thumbPath = RenderUtils.GetRoundRect(g, thumbRect, thumb / 2, thumb / 2, thumb / 2, thumb / 2, thumb / 2, thumb / 2, thumb / 2, thumb / 2);
            using var thumbBrush = g.GetSolidBrush(accent);
            g.DrawPath(thumbBrush, thumbPath);
        }
    }
}
