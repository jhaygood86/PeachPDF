namespace PeachPDF.CSS
{
    internal sealed class FontVariantPositionProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontVariantPositionConverter.OrDefault(FontVariantPosition.Normal);

        internal FontVariantPositionProperty()
            : base(PropertyNames.FontVariantPosition, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
