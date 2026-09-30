namespace PeachPDF.CSS
{
    internal sealed class FontStretchProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontStretchConverter.Or(Converters.NonNegativePercentConverter).OrDefault(FontStretch.Normal);

        internal FontStretchProperty()
            : base(PropertyNames.FontStretch, PropertyFlags.Inherited | PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}