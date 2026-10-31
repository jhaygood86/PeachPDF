namespace PeachBrackets.Css
{
    internal sealed class TextJustifyProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.TextJustifyConverter.OrDefault();

        public TextJustifyProperty()
            : base(PropertyNames.TextJustify, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}