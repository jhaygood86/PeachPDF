namespace PeachBrackets.Css
{
    internal sealed class TextDecorationLineProperty : Property
    {
        private static readonly IValueConverter ListConverter = Converters.TextDecorationLinesConverter.OrDefault();

        internal TextDecorationLineProperty()
            : base(PropertyNames.TextDecorationLine)
        {
        }

        internal override IValueConverter Converter => ListConverter;
    }
}