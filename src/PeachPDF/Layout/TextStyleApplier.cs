using PeachBrackets.Building;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Applies <see cref="ITextStyle"/>/<see cref="ITextSpan"/> members directly to a <c>CssBox</c> -
    /// shared by a page/container's <c>DefaultTextStyle</c> (applied to the container box itself, so
    /// every child inherits it through the normal cascade) and an individual <see cref="ITextSpan"/>
    /// (applied to that span's own inline box).
    /// </summary>
    internal sealed class TextStyleApplier(DocumentNode box) : ITextStyle, ITextSpan
    {
        /// <summary>The node this applier styles; reached by tests that assert on the resulting box.</summary>
        internal DocumentNode Node => box;

        private readonly HashSet<string> _decorationLines = [];
        private readonly List<(string Tag, bool Enabled)> _fontFeatures = [];

        public ITextStyle Bold() => FontWeight(700);

        public ITextStyle FontWeight(int weight)
        {
            box.SetStyle("font-weight", weight.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public ITextStyle Italic()
        {
            box.SetStyle("font-style", "italic");
            return this;
        }

        public ITextStyle FontSize(PdfLength size)
        {
            box.SetStyle("font-size", size);
            return this;
        }

        public ITextStyle FontColor(PdfColor color)
        {
            box.SetStyle("color", color);
            return this;
        }

        public ITextStyle FontFamily(string family)
        {
            box.SetStyle("font-family", family);
            return this;
        }

        public ITextStyle FontFamily(string primary, string fallback)
        {
            box.SetStyle("font-family", $"{primary}, {fallback}");
            return this;
        }

        public ITextStyle BackgroundColor(PdfColor color)
        {
            box.SetStyle("background-color", color);
            return this;
        }

        public ITextStyle Underline() => AddDecorationLine("underline");

        public ITextStyle Overline() => AddDecorationLine("overline");

        public ITextStyle Strikethrough() => AddDecorationLine("line-through");

        private ITextStyle AddDecorationLine(string line)
        {
            _decorationLines.Add(line);
            box.SetStyle("text-decoration-line", string.Join(' ', _decorationLines));
            return this;
        }

        public ITextStyle LetterSpacing(PdfLength spacing)
        {
            box.SetStyle("letter-spacing", spacing);
            return this;
        }

        public ITextStyle WordSpacing(PdfLength spacing)
        {
            box.SetStyle("word-spacing", spacing);
            return this;
        }

        public ITextStyle LineHeight(double multiplier)
        {
            box.SetStyle("line-height", multiplier.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public ITextStyle Subscript()
        {
            box.SetStyle("vertical-align", "sub");
            return this;
        }

        public ITextStyle Superscript()
        {
            box.SetStyle("vertical-align", "super");
            return this;
        }

        public ITextStyle FontFeature(string tag, bool enabled = true)
        {
            _fontFeatures.Add((tag, enabled));
            var value = string.Join(", ", _fontFeatures.Select(f =>
                string.Create(CultureInfo.InvariantCulture, $"\"{f.Tag}\" {(f.Enabled ? 1 : 0)}")));
            box.SetStyle("font-feature-settings", value);
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

            box.SetStyle("direction", direction == PdfTextDirection.Rtl ? "rtl" : "ltr");
            return this;
        }

        public ITextStyle BreakAnywhere()
        {
            box.SetStyle("word-break", "break-all");
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
            box.SetStyle("text-decoration-style", value);
            return this;
        }

        public ITextSpan DecorationColor(PdfColor color)
        {
            box.SetStyle("text-decoration-color", color);
            return this;
        }

        public ITextSpan DecorationThickness(PdfLength thickness)
        {
            box.SetStyle("text-decoration-thickness", thickness);
            return this;
        }
    }
}
