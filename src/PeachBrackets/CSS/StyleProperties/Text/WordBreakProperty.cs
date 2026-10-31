namespace PeachBrackets.Css
{
    internal sealed class WordBreakProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.WordBreakConverter.OrDefault();

        public WordBreakProperty()
            : base(PropertyNames.WordBreak, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}