namespace PeachPDF.CSS
{
    internal sealed class FontKerningProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontKerningModeConverter.OrDefault(FontKerningMode.Auto);

        internal FontKerningProperty()
            : base(PropertyNames.FontKerning, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
