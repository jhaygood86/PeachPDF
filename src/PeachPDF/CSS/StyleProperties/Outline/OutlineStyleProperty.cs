namespace PeachPDF.CSS
{
    internal sealed class OutlineStyleProperty : Property
    {
        private static readonly IValueConverter
            StyleConverter = Converters.OutlineStyleConverter.OrDefault(OutlineStyle.None);

        internal OutlineStyleProperty()
            : base(PropertyNames.OutlineStyle)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}