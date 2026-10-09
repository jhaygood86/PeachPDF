namespace PeachPDF.CSS
{
    internal sealed class AnimationNameProperty : Property
    {
        // Exposed for css-properties.json's "cssom-grammar" validator, which calls this same real grammar
        // directly instead of the full cssom round trip - see CLAUDE.md's "one parser" rule.
        internal static readonly IValueConverter ValueGrammar = Converters.AnimationNameConverter.FromList().OrNone();

        private static readonly IValueConverter ListConverter = ValueGrammar.OrDefault();

        internal AnimationNameProperty()
            : base(PropertyNames.AnimationName)
        {
        }

        internal override IValueConverter Converter => ListConverter;
    }
}