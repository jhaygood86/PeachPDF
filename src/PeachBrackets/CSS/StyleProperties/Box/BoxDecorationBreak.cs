namespace PeachBrackets.Css
{
    internal sealed class BoxDecorationBreak : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.BoxDecorationConverter.OrDefault(false);

        internal BoxDecorationBreak()
            : base(PropertyNames.BoxDecorationBreak)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}