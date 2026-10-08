namespace PeachPDF.CSS
{
    internal sealed class AnimationDirectionProperty : Property
    {
        // Exposed for css-properties.json's "cssom-grammar" validator, which calls this same real grammar
        // directly instead of the full cssom round trip - see CLAUDE.md's "one parser" rule.
        internal static readonly IValueConverter ValueGrammar = Converters.AnimationDirectionConverter.FromList();

        private static readonly IValueConverter ListConverter = ValueGrammar.OrDefault(AnimationDirection.Normal);

        internal AnimationDirectionProperty()
            : base(PropertyNames.AnimationDirection)
        {
        }

        internal override IValueConverter Converter => ListConverter;
    }
}