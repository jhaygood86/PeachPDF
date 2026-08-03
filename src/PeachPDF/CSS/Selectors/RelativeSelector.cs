using System.IO;

namespace PeachPDF.CSS
{
    /// <summary>
    /// One alternative of :has()'s forgiving-relative-selector-list argument (CSS Selectors 4 §4.5),
    /// pairing an explicit leading combinator (">"/"+"/"~") with the complex selector that follows it.
    /// Only produced by SelectorConstructor.GetRelativeResult() for a genuinely non-default leading
    /// combinator - the default (descendant, i.e. no leading combinator) case stays a plain ISelector,
    /// matched via the existing any-depth descendant search.
    /// </summary>
    internal sealed class RelativeSelector : StylesheetNode, ISelector
    {
        public RelativeSelector(Combinator combinator, ISelector selector)
        {
            Combinator = combinator;
            Selector = selector;
        }

        public Combinator Combinator { get; }
        public ISelector Selector { get; }

        // Per spec, a relative selector's specificity is that of its complex selector - the leading
        // combinator itself never contributes.
        public Priority Specificity => Selector.Specificity;

        public string Text => this.ToCss();

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            writer.Write(Combinator.Delimiter);
            writer.Write(' ');
            writer.Write(Selector.Text);
        }
    }
}
