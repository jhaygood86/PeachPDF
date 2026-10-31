namespace PeachBrackets.Css
{
    internal sealed class StrokeLinecapProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.StrokeLinecapConverter.OrDefault(StrokeLinecap.Butt);

        public StrokeLinecapProperty()
            : base(PropertyNames.StrokeLinecap, PropertyFlags.Inherited | PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}