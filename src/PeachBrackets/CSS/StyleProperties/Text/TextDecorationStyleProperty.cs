namespace PeachPDF.CSS
{
    internal sealed class TextDecorationStyleProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.TextDecorationStyleConverter.OrDefault(TextDecorationStyle.Solid);

        internal TextDecorationStyleProperty()
            : base(PropertyNames.TextDecorationStyle)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}