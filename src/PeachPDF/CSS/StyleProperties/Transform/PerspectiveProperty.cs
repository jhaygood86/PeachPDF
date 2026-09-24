namespace PeachPDF.CSS
{
    internal sealed class PerspectiveProperty : Property
    {
        // Exposed for css-properties.json's "cssom-grammar" validator, which calls this same real grammar directly.
        internal static readonly IValueConverter ValueGrammar = Converters.LengthConverter.OrNone();

        private static readonly IValueConverter StyleConverter = ValueGrammar.OrDefault(Length.Zero);

        internal PerspectiveProperty()
            : base(PropertyNames.Perspective, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}