namespace PeachPDF.CSS
{
    internal sealed class AnimationDelayProperty : Property
    {
        // Exposed for css-properties.json's "cssom-grammar" validator, which calls this same real grammar
        // directly instead of the full cssom round trip - see CLAUDE.md's "one parser" rule.
        internal static readonly IValueConverter ValueGrammar = Converters.TimeConverter.FromList();

        private static readonly IValueConverter ListConverter = ValueGrammar.OrDefault(Time.Zero);

        internal AnimationDelayProperty()
            : base(PropertyNames.AnimationDelay)
        {
        }

        internal override IValueConverter Converter => ListConverter;
    }
}