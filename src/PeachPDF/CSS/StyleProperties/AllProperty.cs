namespace PeachPDF.CSS
{
    /// <summary>
    /// The <c>all</c> shorthand (CSS Cascade 4 §3.2): resets every property except <c>direction</c>,
    /// <c>unicode-bidi</c> and custom properties to a single CSS-wide keyword. Its grammar is exactly the five
    /// keywords, so <see cref="ShorthandProperty.Export"/> hands each longhand the keyword unchanged.
    /// </summary>
    internal sealed class AllProperty : ShorthandProperty
    {
        private static readonly IValueConverter KeywordConverter =
            new IdentifierValueConverter<object?>(Keywords.Initial, null)
                .Or(Keywords.Inherit)
                .Or(Keywords.Unset)
                .Or(Keywords.Revert)
                .Or(Keywords.RevertLayer);

        internal AllProperty()
            : base(PropertyNames.All)
        {
        }

        internal override IValueConverter Converter => KeywordConverter;

        // A closed keyword set: a var() reference is not deferred, so `all: var(--x)` is rejected at parse time.
        internal override bool AllowsVarSubstitution => false;
    }
}
