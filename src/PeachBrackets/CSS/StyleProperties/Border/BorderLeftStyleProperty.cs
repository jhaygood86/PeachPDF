namespace PeachPDF.CSS
{
    internal sealed class BorderLeftStyleProperty : Property
    {
        private static readonly IValueConverter
            StyleConverter = Converters.LineStyleConverter.OrDefault(LineStyle.None);

        internal BorderLeftStyleProperty()
            : base(PropertyNames.BorderLeftStyle)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}