namespace PeachPDF.CSS
{
    internal sealed class AnimationIterationCountProperty : Property
    {
        // Exposed for css-properties.json's "cssom-grammar" validator, which calls this same real grammar
        // directly instead of the full cssom round trip - see CLAUDE.md's "one parser" rule.
        internal static readonly IValueConverter ValueGrammar = Converters.PositiveOrInfiniteNumberConverter.FromList();

        private static readonly IValueConverter ListConverter = ValueGrammar.OrDefault(1f);

        internal AnimationIterationCountProperty()
            : base(PropertyNames.AnimationIterationCount)
        {
        }

        internal override IValueConverter Converter => ListConverter;
    }
}