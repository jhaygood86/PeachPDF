using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Applies <see cref="ITextStyle"/>/<see cref="ITextSpan"/> members directly to a <see cref="CssBox"/> -
    /// shared by a page/container's <c>DefaultTextStyle</c> (applied to the container box itself, so
    /// every child inherits it through the normal cascade) and an individual <see cref="ITextSpan"/>
    /// (applied to that span's own inline box).
    /// </summary>
    internal sealed class TextStyleApplier(CssBox box, CssPropertyFactory properties) : ITextStyle, ITextSpan
    {
        private readonly HashSet<string> _decorationLines = [];
        private readonly List<(string Tag, bool Enabled)> _fontFeatures = [];

        internal CssBox Box => box;

        public ITextStyle Bold() => FontWeight(700);

        public ITextStyle FontWeight(int weight)
        {
            properties.Set(box, "font-weight", weight.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public ITextStyle Italic()
        {
            properties.Set(box, "font-style", "italic");
            return this;
        }

        public ITextStyle FontSize(PdfLength size)
        {
            properties.Set(box, "font-size", size);
            return this;
        }

        public ITextStyle FontColor(PdfColor color)
        {
            properties.Set(box, "color", color);
            return this;
        }

        public ITextStyle FontFamily(string family)
        {
            properties.Set(box, "font-family", family);
            return this;
        }

        public ITextStyle FontFamily(string primary, string fallback)
        {
            properties.Set(box, "font-family", $"{primary}, {fallback}");
            return this;
        }

        public ITextStyle BackgroundColor(PdfColor color)
        {
            properties.Set(box, "background-color", color);
            return this;
        }

        public ITextStyle Underline() => AddDecorationLine("underline");

        public ITextStyle Overline() => AddDecorationLine("overline");

        public ITextStyle Strikethrough() => AddDecorationLine("line-through");

        private ITextStyle AddDecorationLine(string line)
        {
            _decorationLines.Add(line);
            properties.Set(box, "text-decoration-line", string.Join(' ', _decorationLines));
            return this;
        }

        public ITextStyle LetterSpacing(PdfLength spacing)
        {
            properties.Set(box, "letter-spacing", spacing);
            return this;
        }

        public ITextStyle WordSpacing(PdfLength spacing)
        {
            properties.Set(box, "word-spacing", spacing);
            return this;
        }

        public ITextStyle LineHeight(double multiplier)
        {
            properties.Set(box, "line-height", multiplier.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public ITextStyle Subscript()
        {
            properties.Set(box, "vertical-align", "sub");
            return this;
        }

        public ITextStyle Superscript()
        {
            properties.Set(box, "vertical-align", "super");
            return this;
        }

        public ITextStyle FontFeature(string tag, bool enabled = true)
        {
            _fontFeatures.Add((tag, enabled));
            var value = string.Join(", ", _fontFeatures.Select(f =>
                string.Create(CultureInfo.InvariantCulture, $"\"{f.Tag}\" {(f.Enabled ? 1 : 0)}")));
            properties.Set(box, "font-feature-settings", value);
            return this;
        }

        public ITextStyle Direction(PdfTextDirection direction)
        {
            if (direction == PdfTextDirection.Auto)
            {
                // Can't resolve this eagerly here - a decorator like DefaultTextStyle typically runs
                // before the text it needs to scan even exists (it's chained ahead of the terminal
                // Text/Span call that supplies it). Deferred to a single pass over the whole page once
                // its content is fully built - see CssBox.PendingAutoDirection's own doc comment.
                box.PendingAutoDirection = true;
                return this;
            }

            properties.Set(box, "direction", direction == PdfTextDirection.Rtl ? "rtl" : "ltr");
            return this;
        }

        public ITextStyle BreakAnywhere()
        {
            properties.Set(box, "word-break", "break-all");
            return this;
        }

        public ITextSpan DecorationStyle(PdfTextDecorationStyle style)
        {
            var value = style switch
            {
                PdfTextDecorationStyle.Double => "double",
                PdfTextDecorationStyle.Dotted => "dotted",
                PdfTextDecorationStyle.Dashed => "dashed",
                PdfTextDecorationStyle.Wavy => "wavy",
                _ => "solid"
            };
            properties.Set(box, "text-decoration-style", value);
            return this;
        }

        public ITextSpan DecorationColor(PdfColor color)
        {
            properties.Set(box, "text-decoration-color", color);
            return this;
        }

        public ITextSpan DecorationThickness(PdfLength thickness)
        {
            properties.Set(box, "text-decoration-thickness", thickness);
            return this;
        }
    }
}
