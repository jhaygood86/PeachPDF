namespace PeachPDF.CSS
{
    internal sealed class TextAlignAllProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.HorizontalAlignmentConverter.OrDefault(HorizontalAlignment.Start);

        internal TextAlignAllProperty()
            : base(PropertyNames.TextAlignAll, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
