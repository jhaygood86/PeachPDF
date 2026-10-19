namespace PeachPDF.CSS
{
    internal sealed class AnimationPlayStateProperty : Property
    {
        // Exposed for css-properties.json's "cssom-grammar" validator, which calls this same real grammar
        // directly instead of the full cssom round trip - see CLAUDE.md's "one parser" rule.
        internal static readonly IValueConverter ValueGrammar = Converters.PlayStateConverter.FromList();

        private static readonly IValueConverter ListConverter = ValueGrammar.OrDefault(PlayState.Running);

        internal AnimationPlayStateProperty()
            : base(PropertyNames.AnimationPlayState)
        {
        }

        internal override IValueConverter Converter => ListConverter;
    }
}