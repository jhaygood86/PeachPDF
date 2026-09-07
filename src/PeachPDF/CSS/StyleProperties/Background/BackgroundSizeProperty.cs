namespace PeachPDF.CSS
{
    internal sealed class BackgroundSizeProperty : Property
    {
        // Exposed for css-properties.json's "cssom-grammar" validator (ValidatorExpressionBuilder),
        // which calls this same real grammar directly instead of the full cssom PropertyFactory/
        // StylesheetParser round trip - see CLAUDE.md's "one parser" rule.
        internal static readonly IValueConverter ValueGrammar = Converters.BackgroundSizeConverter.FromList();
        private static readonly IValueConverter ListConverter = ValueGrammar.OrDefault();

        internal BackgroundSizeProperty()
            : base(PropertyNames.BackgroundSize, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => ListConverter;
    }
}