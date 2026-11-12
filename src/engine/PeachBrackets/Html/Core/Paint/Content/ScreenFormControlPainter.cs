using PeachBrackets.Annotations;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Handlers;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;

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
        // A static page (neither a host's live control nor an interactive field) keeps the box as any other box is painted - background, border,
        // shadow - and this painter only adds what the control shows inside it.
        private readonly bool _genericChrome;

        public ScreenFormControlPainter(bool genericChrome = false) => _genericChrome = genericChrome;

        private void Chrome(Canvas g, CssBox box, Rect rect)
        {
            if (!_genericChrome)
                FormFieldChrome.PaintBorderAndBackground(g, box, rect);
        }

        public void Paint(FragmentPainter painter, Canvas g, BoxFragment fragment)
        {
            var box = (CssBoxFormField)fragment.Box;
            var rect = fragment.PrimaryRect;
            if (rect is not { Width: > 0, Height: > 0 })
            {
                painter.PaintBoxContent(g, fragment);
                return;
            }

            var isRadio = !box.HtmlTag!.Name.Equals("select", StringComparison.OrdinalIgnoreCase) && box.GetAttribute("type", "text").Equals("radio", StringComparison.OrdinalIgnoreCase);
            if (_genericChrome && !isRadio && !box.IsTextArea)
                painter.PaintBoxContent(g, fragment);

            var tag = box.HtmlTag!.Name;
            var type = box.GetAttribute("type", "text").ToLowerInvariant();
            var disabled = box.HtmlTag.HasAttribute("disabled");

            if (tag.Equals("select", StringComparison.OrdinalIgnoreCase))
            {
                Chrome(g, box, rect);
                PaintSelect(g, box, rect);
            }
            else if (type == "checkbox")
            {
                Chrome(g, box, rect);
                FormFieldChrome.PaintCheckboxGlyph(g, box, rect, box.HtmlTag.HasAttribute("checked"));
            }
            else if (type == "radio")
            {
                FormFieldChrome.PaintRadioBackground(g, box, rect);
                FormFieldChrome.PaintRadioGlyph(g, box, rect, box.HtmlTag.HasAttribute("checked"));
            }
            else if (type is "button" or "submit" or "reset")
            {
                Chrome(g, box, rect);
                var label = box.GetAttribute("value", string.Empty);
                if (label.Length == 0)
                    label = type switch { "submit" => "Submit", "reset" => "Reset", _ => string.Empty };
                PaintText(g, box, rect, label, centered: true, box.ActualColor, disabled);
            }
            else if (type == "range")
            {
                Chrome(g, box, rect);
                PaintRange(g, box, rect);
            }
            else if (type is "hidden" or "image")
            {
                painter.PaintBoxContent(g, fragment);
            }
            else if (type == "color")
            {
                Chrome(g, box, rect);
                PaintColorSwatch(g, box, rect);
            }
            else if (type == "file")
            {
                Chrome(g, box, rect);
                PaintFileChooser(g, box, rect, disabled);
            }
            else if (type is "date" or "time" or "datetime-local" or "month" or "week" && box.GetAttribute("value", string.Empty).Length == 0)
            {
                // A date or time field with no value shows the shape its value would take, in the placeholder's color.
                Chrome(g, box, rect);
                var style = box.ResolvedPlaceholderStyle ?? box;
                PaintText(g, box, rect, DateTimeHint(type), centered: false, style.ActualColor, disabled);
            }
            else if (box.IsTextArea)
            {
                Chrome(g, box, rect);
                PaintTextArea(g, box, rect, box.GetAttribute("value", string.Empty), disabled);
            }
            else
            {
                Chrome(g, box, rect);
                var rawValue = box.GetAttribute("value", string.Empty);
                var value = DisplayText(box, rawValue);

                if (value.Length > 0)
                {
                    PaintText(g, box, rect, value, centered: false, box.ActualColor, disabled);
                }
                else if (box.GetAttribute("placeholder", string.Empty) is { Length: > 0 } placeholder)
                {
                    var style = box.ResolvedPlaceholderStyle ?? box;
                    PaintText(g, box, rect, placeholder, centered: false, style.ActualColor, disabled);
                }

                PaintEditing(g, box, rect, value);
            }

            // Like any replaced control, it takes an outline (css-ui-4 section 3.1), painted after its look.
            var (overflowClip, overflowClipCurve) = painter.OverflowClipOf(g, fragment);
            var overflowClipRecorded = painter.PushOverflowClip(overflowClip, overflowClipCurve);
            painter.PaintReplacedOutline(g, box, rect);
            painter.PopOverflowClip(overflowClipRecorded);
        }

        // A text area: its value (or its placeholder) broken into lines for its width, then the selection, composition and caret of the one being edited.
        internal static void PaintTextArea(Canvas g, CssBox box, Rect rect, string value, bool disabled)
        {
            var content = ContentRect(box, rect);
            if (content is not { Width: > 0, Height: > 0 })
                return;

            var font = box.ActualFont;
            var text = value;
            var color = box.ActualColor;
            if (value.Length == 0 && box.GetAttribute("placeholder", string.Empty) is { Length: > 0 } placeholder)
            {
                text = placeholder;
                color = (box.ResolvedPlaceholderStyle ?? box).ActualColor;
            }

            var lines = TextAreaLayout.Wrap(g, font, text, content.Width);
            var scroll = ScrollToCaret(box, value, lines, font.Height, content.Height);
            g.PushClip(content);
            for (var i = 0; i < lines.Count; i++)
            {
                var top = content.Y + (i * font.Height) - scroll;
                if (top + font.Height <= content.Y)
                    continue;
                if (top >= content.Y + content.Height)
                    break;

                var line = lines[i];
                if (line.End > line.Start)
                {
                    g.DrawString(text[line.Start..line.End].TrimEnd(' '), font, disabled ? Dimmed(color) : color, new PaintPoint(content.X, top),
                        new Size(content.Width, font.Height), fontPalette: box.ActualFontPalette, features: box.ActualTextShapingFeatures);
                }
            }

            PaintTextAreaEditing(g, box, new Rect(content.X, content.Y - scroll, content.Width, content.Height), value,
                value.Length > 0 ? lines : TextAreaLayout.Wrap(g, font, value, content.Width));
            g.PopClip();
        }

        // How far the lines of a text area are scrolled: what a wheel set, kept inside the text, and - after the caret moved or the text changed in the
        // field being edited - enough to bring the caret's line into view. The container remembers it.
        private static double ScrollToCaret(CssBox box, string text, IReadOnlyList<TextAreaLine> lines, double lineHeight, double viewHeight)
        {
            if (box.HtmlContainer is not { } container)
                return 0;

            var ordinal = box.HtmlTag!.SourceOrdinal;
            var scroll = container.TextAreaScroll.GetValueOrDefault(ordinal);
            if (container.Interaction?.Focused == ordinal)
            {
                // A field that has just been focused starts at the end of its text, as the document's own editing calls start it.
                var session = container.FormEdit;
                if (session.Ordinal != ordinal)
                {
                    session.Ordinal = ordinal;
                    session.Start = session.End = text.Length;
                    session.CompositionStart = session.CompositionEnd = -1;
                    session.FollowCaret = true;
                }

                if (session.FollowCaret)
                {
                    var lineTop = TextAreaLayout.LineOf(lines, Math.Clamp(session.End, 0, text.Length)) * lineHeight;
                    if (lineTop < scroll)
                        scroll = lineTop;
                    else if (lineTop + lineHeight > scroll + viewHeight)
                        scroll = lineTop + lineHeight - viewHeight;

                    session.FollowCaret = false;
                }
            }

            scroll = Math.Clamp(scroll, 0, Math.Max((lines.Count * lineHeight) - viewHeight, 0));
            container.TextAreaScroll[ordinal] = scroll;
            return scroll;
        }

        private static void PaintTextAreaEditing(Canvas g, CssBox box, Rect content, string text, IReadOnlyList<TextAreaLine> lines)
        {
            var ordinal = box.HtmlTag!.SourceOrdinal;
            if (box.HtmlContainer is not { } container || container.Interaction?.Focused != ordinal || box.HtmlTag.HasAttribute("disabled"))
                return;

            var session = container.FormEdit;
            var owned = session.Ordinal == ordinal;
            var start = owned ? Math.Clamp(Math.Min(session.Start, session.End), 0, text.Length) : text.Length;
            var end = owned ? Math.Clamp(Math.Max(session.Start, session.End), 0, text.Length) : text.Length;
            var font = box.ActualFont;

            if (start != end)
            {
                using var highlight = g.GetSolidBrush(PaintColor.FromArgb(90, 0, 120, 215));
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var from = Math.Max(start, line.Start);
                    var to = Math.Min(end, line.End);
                    if (to <= from)
                        continue;

                    var x1 = content.X + TextAreaLayout.OffsetOf(g, font, text, line, from);
                    var x2 = content.X + TextAreaLayout.OffsetOf(g, font, text, line, to);
                    g.DrawRectangle(highlight, x1, content.Y + (i * font.Height), Math.Max(x2 - x1, 2), font.Height);
                }
            }

            if (owned && session.CompositionStart >= 0 && session.CompositionEnd > session.CompositionStart)
            {
                var underline = g.GetPen(box.ActualColor);
                underline.Width = Math.Max(font.Height / 14, 1);
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var from = Math.Max(session.CompositionStart, line.Start);
                    var to = Math.Min(session.CompositionEnd, line.End);
                    if (to <= from)
                        continue;

                    var y = content.Y + (i * font.Height) + font.Height - underline.Width;
                    g.DrawLine(underline, content.X + TextAreaLayout.OffsetOf(g, font, text, line, from), y, content.X + TextAreaLayout.OffsetOf(g, font, text, line, to), y);
                }
            }

            if (start == end && session.CaretVisible)
            {
                var number = TextAreaLayout.LineOf(lines, start);
                var caret = g.GetPen(box.ActualColor);
                caret.Width = Math.Max(font.Height / 14, 1);
                var x = content.X + TextAreaLayout.OffsetOf(g, font, text, lines[number], start) + (caret.Width / 2);
                var top = content.Y + (number * font.Height);
                g.DrawLine(caret, x, top, x, top + font.Height);
            }
        }

        /// <summary>What a text field shows for its value: the value, or a bullet for each character of a password.</summary>
        internal static string DisplayText(CssBox box, string value) =>
            box.GetAttribute("type", "text").Equals("password", StringComparison.OrdinalIgnoreCase) ? new string('•', value.Length) : value;

        /// <summary>How far from the start of the text a position is, in the field's font: the width of what comes before it.</summary>
        internal static double OffsetOf(Canvas g, CssBox box, string display, int index) =>
            index <= 0 || display.Length == 0 ? 0 : g.MeasureString(display[..Math.Min(index, display.Length)], box.ActualFont).Width;

        // The selection, the text an input method is composing, and the caret of the text field that has the focus.
        private static void PaintEditing(Canvas g, CssBox box, Rect rect, string display)
        {
            var ordinal = box.HtmlTag!.SourceOrdinal;
            if (box.HtmlContainer is not { } container || container.Interaction?.Focused != ordinal || box.HtmlTag.HasAttribute("disabled"))
                return;

            var session = container.FormEdit;
            var owned = session.Ordinal == ordinal;
            var start = owned ? Math.Clamp(Math.Min(session.Start, session.End), 0, display.Length) : display.Length;
            var end = owned ? Math.Clamp(Math.Max(session.Start, session.End), 0, display.Length) : display.Length;
            var content = ContentRect(box, rect);
            if (content is not { Width: > 0, Height: > 0 })
                return;

            var font = box.ActualFont;
            var top = content.Y + Math.Max((content.Height - font.Height) / 2, 0);
            double X(int index) => content.X + Math.Min(OffsetOf(g, box, display, index), content.Width);

            g.PushClip(content);
            if (start != end)
            {
                using var highlight = g.GetSolidBrush(PaintColor.FromArgb(90, 0, 120, 215));
                g.DrawRectangle(highlight, X(start), top, Math.Max(X(end) - X(start), 1), font.Height);
            }

            if (owned && session.CompositionStart >= 0 && session.CompositionEnd > session.CompositionStart)
            {
                var underline = g.GetPen(box.ActualColor);
                underline.Width = Math.Max(font.Height / 14, 1);
                var y = top + font.Height - underline.Width;
                g.DrawLine(underline, X(Math.Min(session.CompositionStart, display.Length)), y, X(Math.Min(session.CompositionEnd, display.Length)), y);
            }

            if (start == end && session.CaretVisible)
            {
                var caret = g.GetPen(box.ActualColor);
                caret.Width = Math.Max(font.Height / 14, 1);
                var x = X(start) + (caret.Width / 2);
                g.DrawLine(caret, x, top, x, top + font.Height);
            }

            g.PopClip();
        }

        /// <summary>The text a date or time field shows while it has no value: the pattern its value follows.</summary>
        internal static string DateTimeHint(string type) => type switch
        {
            "date" => "mm/dd/yyyy",
            "time" => "--:-- --",
            "datetime-local" => "mm/dd/yyyy, --:-- --",
            "month" => "------ ----",
            _ => "Week --, ----",
        };

        /// <summary>The color a <c>color</c> input holds: <c>#rrggbb</c> or <c>#rgb</c>, black when it holds none or something else.</summary>
        internal static PaintColor ColorOf(string value)
        {
            var hex = value.Trim().TrimStart('#');
            if (hex.Length == 3 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var shortValue))
                return PaintColor.FromArgb(255, ((shortValue >> 8) & 0xF) * 17, ((shortValue >> 4) & 0xF) * 17, (shortValue & 0xF) * 17);
            if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var longValue))
                return PaintColor.FromArgb(255, (longValue >> 16) & 0xFF, (longValue >> 8) & 0xFF, longValue & 0xFF);

            return PaintColor.FromArgb(255, 0, 0, 0);
        }

        // The color the input holds, as a filled block inside its border.
        private static void PaintColorSwatch(Canvas g, CssBox box, Rect rect)
        {
            var content = ContentRect(box, rect);
            if (content is not { Width: > 0, Height: > 0 })
                return;

            var swatch = new Rect(content.X + 1, content.Y + 1, Math.Max(content.Width - 2, 0), Math.Max(content.Height - 2, 0));
            using var brush = g.GetSolidBrush(ColorOf(box.GetAttribute("value", string.Empty)));
            g.DrawRectangle(brush, swatch.X, swatch.Y, swatch.Width, swatch.Height);
        }

        // A button that says what it does, and beside it what has been chosen.
        /// <summary>The attribute a host's choice of files is kept in (the names, one to a line); see <see cref="HtmlDocument.SetFileNames"/>.</summary>
        internal const string ChosenFilesAttribute = "peachpdf-chosen-files";

        /// <summary>What a file input shows beside its button: "No file chosen", the name of the one file, or the number of files.</summary>
        internal static string FileLabel(string chosen)
        {
            var names = chosen.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return names.Length switch
            {
                0 => "No file chosen",
                1 => names[0],
                _ => names.Length + " files",
            };
        }

        private static void PaintFileChooser(Canvas g, CssBox box, Rect rect, bool disabled)
        {
            var content = ContentRect(box, rect);
            if (content is not { Width: > 0, Height: > 0 })
                return;

            var font = box.ActualFont;
            const string label = "Choose File";
            var buttonWidth = Math.Min(g.MeasureString(label, font).Width + 8, content.Width);
            using (var fill = g.GetSolidBrush(PaintColor.FromArgb(255, 239, 239, 239)))
                g.DrawRectangle(fill, content.X, content.Y, buttonWidth, content.Height);

            PaintText(g, box, new Rect(rect.X, rect.Y, buttonWidth + box.ActualBorderLeftWidth + box.ActualPaddingLeft + box.ActualBorderRightWidth + box.ActualPaddingRight, rect.Height),
                label, centered: true, box.ActualColor, disabled);

            var restX = content.X + buttonWidth + 6;
            if (restX < content.X + content.Width)
            {
                var rest = new Rect(restX - box.ActualBorderLeftWidth - box.ActualPaddingLeft, rect.Y, rect.Width - (restX - rect.X) + box.ActualBorderLeftWidth + box.ActualPaddingLeft, rect.Height);
                PaintText(g, box, rest, FileLabel(box.GetAttribute(ChosenFilesAttribute, string.Empty)), centered: false, PaintColor.FromArgb(255, 117, 117, 117), disabled);
            }
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
