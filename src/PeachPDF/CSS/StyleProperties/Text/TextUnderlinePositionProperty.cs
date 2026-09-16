namespace PeachPDF.CSS
{
    internal sealed class TextUnderlinePositionProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.TextUnderlinePositionConverter.OrDefault(TextUnderlinePosition.Auto);

        internal TextUnderlinePositionProperty()
            : base(PropertyNames.TextUnderlinePosition, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
