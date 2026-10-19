namespace PeachPDF.CSS
{
    internal sealed class ColumnFillProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.ColumnFillConverter.OrDefault(true);

        internal ColumnFillProperty()
            : base(PropertyNames.ColumnFill)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}