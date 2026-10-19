namespace PeachPDF.CSS
{
    internal sealed class FontVariantEmojiProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontVariantEmojiConverter.OrDefault(FontVariantEmoji.Normal);

        internal FontVariantEmojiProperty()
            : base(PropertyNames.FontVariantEmoji, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
