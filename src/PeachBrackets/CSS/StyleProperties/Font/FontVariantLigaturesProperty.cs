namespace PeachPDF.CSS
{
    internal sealed class FontVariantLigaturesProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontVariantLigaturesConverter.OrDefault();

        internal FontVariantLigaturesProperty()
            : base(PropertyNames.FontVariantLigatures, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
