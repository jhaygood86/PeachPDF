namespace PeachPDF.CSS
{
    internal sealed class TextOverflowProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.TextOverflowConverter.OrDefault(TextOverflow.Clip);

        internal TextOverflowProperty()
            : base(PropertyNames.TextOverflow)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
