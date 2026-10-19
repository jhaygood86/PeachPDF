namespace PeachPDF.CSS
{
    internal sealed class FontStyleProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontStyleConverter.OrDefault(FontStyle.Normal);

        internal FontStyleProperty()
            : base(PropertyNames.FontStyle, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}