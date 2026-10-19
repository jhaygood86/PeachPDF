using PeachPDF.Html.Core.Dom;

namespace PeachPDF.CSS
{
    /// <summary>
    /// A parsed <c>@container style(...)</c> condition tree (CSS Containment 3 SS7). Structurally mirrors
    /// <see cref="IConditionFunction"/>'s <c>and</c>/<c>or</c>/<c>not</c>/group grammar shape (the exact
    /// one already implemented for <c>@supports</c> in <c>StylesheetComposer</c>'s
    /// <c>ExtractCondition</c>/<c>AggregateCondition</c>/<c>MultipleConditions</c> - this file's parser
    /// methods of the same "Style"-prefixed names mirror them token-for-token), but truthiness is
    /// per-element rather than static - a style query's leaf compares the queried declaration against a
    /// specific candidate query container's own resolved style, which <see cref="IConditionFunction.Check"/>'s
    /// argument-free signature can't express. See <see cref="StyleDeclarationCondition"/> for the leaf,
    /// and <see cref="StyleAndCondition"/>/<see cref="StyleOrCondition"/>/<see cref="StyleNotCondition"/>
    /// for the boolean combinators. A parenthesized group needs no distinct wrapper type the way
    /// <c>GroupCondition</c> exists for <c>@supports</c> - that type exists there only to round-trip
    /// <c>ToCss</c> serialization, which a one-shot-evaluated style query never needs; the parser simply
    /// returns the inner parsed condition for a <c>(...)</c> group.
    /// </summary>
    internal interface IStyleQueryCondition
    {
        bool Check(CssBox container);
    }
}
