namespace PeachPDF.CSS
{
    internal sealed class ColumnSpanProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.ColumnSpanConverter.OrDefault(false);

        internal ColumnSpanProperty()
            : base(PropertyNames.ColumnSpan)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}