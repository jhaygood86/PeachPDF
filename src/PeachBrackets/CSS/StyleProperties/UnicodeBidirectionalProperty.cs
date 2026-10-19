namespace PeachPDF.CSS
{
    internal sealed class UnicodeBidirectionalProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.UnicodeModeConverter.OrDefault(UnicodeMode.Normal);

        internal UnicodeBidirectionalProperty()
            : base(PropertyNames.UnicodeBidirectional)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}