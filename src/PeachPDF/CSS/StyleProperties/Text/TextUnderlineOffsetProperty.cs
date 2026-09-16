namespace PeachPDF.CSS
{
    internal sealed class TextUnderlineOffsetProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.TextUnderlineOffsetConverter.OrDefault(TextUnderlineOffsetKeyword.Auto);

        internal TextUnderlineOffsetProperty()
            : base(PropertyNames.TextUnderlineOffset)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
