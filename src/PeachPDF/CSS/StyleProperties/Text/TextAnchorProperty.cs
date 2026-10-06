namespace PeachPDF.CSS
{
    internal sealed class TextAnchorProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.TextAnchorConverter.OrDefault();

        public TextAnchorProperty()
            : base(PropertyNames.TextAnchor)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}