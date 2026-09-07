namespace PeachPDF.CSS
{
    using static Converters;

    internal sealed class CounterSetProperty : Property
    {
        // Exposed for css-properties.json's "cssom-grammar" validator (ValidatorExpressionBuilder),
        // which calls this same real grammar directly instead of the full cssom PropertyFactory/
        // StylesheetParser round trip - see CLAUDE.md's "one parser" rule.
        internal static readonly IValueConverter ValueGrammar = Continuous(
            WithOrder(IdentifierConverter.Required(), IntegerConverter.Option(0)));

        private static readonly IValueConverter StyleConverter = ValueGrammar.OrDefault();

        internal CounterSetProperty()
            : base(PropertyNames.CounterSet)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
