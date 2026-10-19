namespace PeachBrackets.Css
{
    internal sealed class CaptionSideProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.CaptionSideConverter.OrDefault(true);

        internal CaptionSideProperty() : base(PropertyNames.CaptionSide)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}