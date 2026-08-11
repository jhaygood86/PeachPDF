using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Parse;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
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

        internal override ValueTask MeasureWordsSize(RGraphics g)
        {
            if (_wordsSizeMeasured)
                return ValueTask.CompletedTask;

            MeasureWordSpacing(g);
            _wordsSizeMeasured = true;

            var kind = FormFieldMapper.Classify(this).Kind;
            var (intrinsicWidth, intrinsicHeight) = kind is FormFieldKind.Checkbox or FormFieldKind.Radio
                ? (13.0, 13.0)
                : (170.0, 20.0);

            CssLayoutEngine.MeasureIntrinsicSize(_word, intrinsicWidth, intrinsicHeight);
            return ValueTask.CompletedTask;
        }
    }
}
