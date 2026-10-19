namespace PeachPDF.CSS
{
    internal sealed class TextDecorationSkipInkProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.TextDecorationSkipInkConverter.OrDefault(TextDecorationSkipInk.Auto);

        internal TextDecorationSkipInkProperty()
            : base(PropertyNames.TextDecorationSkipInk, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
