namespace PeachPDF.CSS
{
    internal sealed class QuotesProperty : Property
    {
        // Exposed for css-properties.json's "cssom-grammar" validator (ValidatorExpressionBuilder),
        // which calls this same real grammar directly instead of the full cssom PropertyFactory/
        // StylesheetParser round trip - see CLAUDE.md's "one parser" rule.
        internal static readonly IValueConverter ValueGrammar = Converters.EvenStringsConverter.OrNone();

        private static readonly IValueConverter StyleConverter = ValueGrammar.OrDefault(new[] { "«", "»" });

        internal QuotesProperty()
            : base(PropertyNames.Quotes, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}