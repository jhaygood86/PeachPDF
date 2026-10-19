namespace PeachPDF.CSS
{
    internal sealed class BorderBlockEndColorProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.CurrentColorConverter.OrDefault(Color.Transparent);

        internal BorderBlockEndColorProperty()
            : base(PropertyNames.BorderBlockEndColor)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
