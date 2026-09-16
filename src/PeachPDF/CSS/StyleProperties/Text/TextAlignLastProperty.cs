namespace PeachPDF.CSS
{
    internal sealed class TextAlignLastProperty : Property
    {
        // .OrDefault is required (not just cosmetic global-keyword support): ShorthandProperty.Export
        // resets an omitted longhand via TrySetValue(null), which re-parses the literal "initial"
        // keyword through this exact converter - without .OrDefault, TextAlignmentsLast has no
        // "initial" entry, Convert returns null, TrySetValue fails, and the text-align shorthand's
        // reset-to-auto never actually applies. See TextAlignProperty (the text-align shorthand).
        private static readonly IValueConverter StyleConverter = Converters.TextAlignLastConverter.OrDefault(TextAlignLast.Auto);

        public TextAlignLastProperty()
            : base(PropertyNames.TextAlignLast)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}