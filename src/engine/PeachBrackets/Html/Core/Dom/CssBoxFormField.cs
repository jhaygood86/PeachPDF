using PeachBrackets.Annotations;
using PeachDrawing.Core;
using PeachBrackets.Html.Handlers;
using PeachBrackets.Html.Parse;
using System.Threading.Tasks;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// CSS box for an <c>&lt;input&gt;</c> or <c>&lt;select&gt;</c> element. Its layout is the same
    /// ordinary inline-block flow every <see cref="CssBox"/> gets from the UA stylesheet's
    /// <c>input, select { display: inline-block }</c> rule, but - like <see cref="CssBoxImage"/> -
    /// it owns a phantom <c>Words</c> entry so <see cref="CssLayoutEngine.MeasureIntrinsicSize"/>
    /// gives it a sane default size (a real browser's own UA default for an unstyled text input/select,
    /// or a small square for a checkbox/radio) when the author sets no explicit CSS width/height -
    /// without this, an empty, content-less inline-block box resolves to zero size regardless of any
    /// explicit width/height, since (unlike a block-level box) inline-block sizing for a box with no
    /// words is otherwise never driven through the width/height-resolving codepath at all.
    /// <c>&lt;select&gt;</c>'s own <c>&lt;option&gt;</c> children stay real <see cref="CssBox"/>
    /// children (read directly by <c>FormFieldMapper.ClassifySelect</c>'s tree walk, off
    /// <see cref="CssBox.Boxes"/>/<see cref="CssBox.HtmlTag"/> - a DOM-structure read, not a layout
    /// one) but are never laid out or painted as flowed content: <see cref="MeasureWordsSize"/> below
    /// takes the same "replaced element" shortcut <see cref="CssBoxImage"/>'s own override does -
    /// skipping the base implementation entirely, the same base call that would otherwise recurse
    /// into measuring each child's own words - so an &lt;option&gt;'s text is simply never measured,
    /// and unmeasured content never reaches a paintable fragment.
    /// This type also exists so <c>FragmentContentPainters.For</c> can dispatch a dedicated painter and
    /// <c>MonolithicContent.IsReplaced</c> can keep a field from splitting across a page break.
    /// </summary>
    internal sealed class CssBoxFormField : CssBox
    {
        readonly CssRectFormField _word;

        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="parent">the parent box of this box</param>
        /// <param name="tag">the html tag data of this box</param>
        public CssBoxFormField(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
            _word = new CssRectFormField(this);
            Words.Add(_word);
        }

        /// <summary>Whether this is a <c>&lt;textarea&gt;</c>, a multi-line text control whose text is its value rather than flowed content.</summary>
        internal bool IsTextArea => HtmlTag?.Name.Equals("textarea", System.StringComparison.OrdinalIgnoreCase) == true;

        // The size HTML gives a text area with no CSS size: cols (20) characters wide and rows (2) lines tall, in CSS pixels.
        private (double Width, double Height) TextAreaSize(Canvas g)
        {
            var unit = (HtmlContainer?.RenderContext?.LayoutUnitsPerPoint ?? 1.0) * 0.75;
            var cols = PositiveInt("cols", 20);
            var rows = PositiveInt("rows", 2);
            var font = ActualFont;
            var characterWidth = g.MeasureString("0", font).Width / unit;
            return (cols * characterWidth, rows * font.Height / unit);
        }

        private int PositiveInt(string attribute, int fallback) =>
            int.TryParse(GetAttribute(attribute, string.Empty), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0 ? n : fallback;

        // The input types whose control is one line of text in a box (HTML "The input element as a text entry widget"); every other type is a button, a glyph,
        // a picker or a file chooser, which keep the replaced element's bottom-margin-edge baseline.
        private static readonly string[] TextEntryTypes = ["text", "search", "tel", "url", "email", "password", "number"];

        /// <summary>
        /// Whether the control is one line of text, so its baseline is that line's rather than its bottom margin edge: a text entry input, or a drop-down
        /// <c>&lt;select&gt;</c> (a list box shows several lines and a <c>&lt;textarea&gt;</c> is not one). HTML renders these as an <c>inline-block</c> "depicting
        /// a one-line text control", and CSS 2.1 section 10.8.1 puts an inline-block's baseline on its last line box, which here is that line of text.
        /// </summary>
        internal bool HasTextLineBaseline
        {
            get
            {
                if (HtmlTag is not { } tag) return false;

                if (tag.Name.Equals("select", System.StringComparison.OrdinalIgnoreCase))
                    return !tag.HasAttribute("multiple") && !(int.TryParse(GetAttribute("size", string.Empty), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size) && size > 1);

                if (!tag.Name.Equals("input", System.StringComparison.OrdinalIgnoreCase)) return false;

                var type = GetAttribute("type", "text").Trim();
                if (type.Length == 0) return true;
                foreach (var known in TextEntryTypes)
                    if (known.Equals(type, System.StringComparison.OrdinalIgnoreCase)) return true;

                // An unknown type falls back to the Text state.
                return !IsKnownNonTextType(type);
            }
        }

        private static bool IsKnownNonTextType(string type) =>
            type.ToLowerInvariant() is "hidden" or "checkbox" or "radio" or "submit" or "reset" or "button" or "image" or "file" or "range" or "color"
                or "date" or "month" or "week" or "time" or "datetime-local";

        /// <summary>
        /// How far below the top of the border box <paramref name="borderBoxHeight"/> tall the baseline of the control's line of text is: the line is
        /// centered in the content box, as <c>ScreenFormControlPainter</c> places it.
        /// </summary>
        internal double TextLineBaselineOffset(double borderBoxHeight)
        {
            var font = ActualFont;
            var contentHeight = System.Math.Max(0, borderBoxHeight - ActualBorderTopWidth - ActualBorderBottomWidth - ActualPaddingTop - ActualPaddingBottom);
            return ActualBorderTopWidth + ActualPaddingTop + System.Math.Max((contentHeight - font.Height) / 2, 0) + font.Ascent;
        }

        /// <summary>The baseline of the control's line of text, given the border box the flow left it at.</summary>
        internal double TextLineBaseline(Rect borderBox) => borderBox.Top + TextLineBaselineOffset(borderBox.Height);

        internal override ValueTask MeasureWordsSize(Canvas g)
        {
            if (_wordsSizeMeasured)
                return ValueTask.CompletedTask;

            MeasureWordSpacing(g);
            _wordsSizeMeasured = true;

            var kind = FormFieldMapper.Classify(this).Kind;
            var (intrinsicWidth, intrinsicHeight) = kind is FormFieldKind.Checkbox or FormFieldKind.Radio
                ? (13.0, 13.0)
                : IsTextArea ? TextAreaSize(g) : (170.0, 20.0);

            CssLayoutEngine.MeasureIntrinsicSize(_word, intrinsicWidth, intrinsicHeight);
            return ValueTask.CompletedTask;
        }
    }
}
