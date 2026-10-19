namespace PeachPDF.CSS
{
    internal sealed class TextDecorationThicknessProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.TextDecorationThicknessConverter.OrDefault(TextDecorationThicknessKeyword.Auto);

        internal TextDecorationThicknessProperty()
            : base(PropertyNames.TextDecorationThickness)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
