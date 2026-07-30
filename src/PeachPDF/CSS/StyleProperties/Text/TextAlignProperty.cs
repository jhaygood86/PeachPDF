namespace PeachPDF.CSS
{
    internal sealed class TextAlignProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.HorizontalAlignmentConverter.OrDefault(HorizontalAlignment.Start);

        internal TextAlignProperty()
            : base(PropertyNames.TextAlign, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}