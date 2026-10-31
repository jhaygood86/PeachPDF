namespace PeachBrackets.Css
{
    internal sealed class StrokeProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.PaintConverter.OrGlobalValue();

        internal StrokeProperty()
            : base(PropertyNames.Stroke, PropertyFlags.Inherited | PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}