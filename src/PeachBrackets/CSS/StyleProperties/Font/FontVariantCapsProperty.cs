namespace PeachPDF.CSS
{
    internal sealed class FontVariantCapsProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontVariantCapsConverter.OrDefault(FontVariantCaps.Normal);

        internal FontVariantCapsProperty()
            : base(PropertyNames.FontVariantCaps, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
