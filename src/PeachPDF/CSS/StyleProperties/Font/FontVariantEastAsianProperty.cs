namespace PeachPDF.CSS
{
    internal sealed class FontVariantEastAsianProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontVariantEastAsianConverter.OrDefault();

        internal FontVariantEastAsianProperty()
            : base(PropertyNames.FontVariantEastAsian, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
