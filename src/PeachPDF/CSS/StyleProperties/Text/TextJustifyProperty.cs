namespace PeachPDF.CSS
{
    internal sealed class TextJustifyProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.TextJustifyConverter.OrDefault();

        public TextJustifyProperty()
            : base(PropertyNames.TextJustify)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}