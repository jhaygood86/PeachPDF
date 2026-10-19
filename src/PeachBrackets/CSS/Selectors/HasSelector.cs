using System.IO;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The relational pseudo-class ":has(S)" - matches an element that has a descendant matching S. Also
    /// supports CSS Selectors 4's leading-combinator relative-selector forms - ":has(&gt; S)" (direct
    /// child), ":has(+ S)" (next sibling), ":has(~ S)" (any following sibling) - and a forgiving,
    /// comma-separated list of such alternatives (":has(&gt; .a, + .b)"), each carrying its own leading
    /// combinator. <see cref="Inner"/> is either a plain <see cref="ISelector"/> (default descendant
    /// form), a <see cref="RelativeSelector"/> (single non-default alternative), or a
    /// <see cref="ListSelector"/> mixing either shape per alternative - see
    /// SelectorConstructor.GetRelativeResult() (parsing) and CssData.HasRelativeMatch (matching).
    /// </summary>
    internal sealed class HasSelector : StylesheetNode, ISelector
    {
        public HasSelector(ISelector inner)
        {
            Inner = inner;
        }

        public ISelector Inner { get; }

        // Per spec, :has() takes the specificity of its most specific argument, same as :is()/:not().
        public Priority Specificity => Inner.Specificity;

        public string Text => this.ToCss();

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            writer.Write(":has(");
            writer.Write(Inner.Text);
            writer.Write(')');
        }
    }
}
