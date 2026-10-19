namespace PeachBrackets.Css
{
    internal sealed class FontVariantNumericProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontVariantNumericConverter.OrDefault();

        internal FontVariantNumericProperty()
            : base(PropertyNames.FontVariantNumeric, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
